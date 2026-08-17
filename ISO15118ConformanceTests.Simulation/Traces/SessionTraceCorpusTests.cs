/*
 * Copyright (c) 2021-2025 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Net;
using System.Text;
using System.Security.Cryptography;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118_2.Generated;
using cloud.charging.open.protocols.ISO15118.Metering;
using EVSimulatorApp.Ocpp;
using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using ISO15118ConformanceTests.Simulation.Timing;
using cloud.charging.open.protocols.ISO15118.Transport;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

namespace ISO15118ConformanceTests.Simulation.Traces;

/// <summary>
/// The session-trace corpus: whole EV↔station sessions recorded frame by frame, checked in, and replayed
/// against the EVCC that produced them.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> The EVCC state machines have to exist three times (C#, Kotlin, Swift — <c>A4</c>),
/// and unlike the codecs they have no reference implementation to be held to. This is the substitute:
/// one implementation's sessions, recorded, so the ports can be checked against something outside
/// themselves. <see cref="SessionTrace"/> spells out what that does and does not prove.
/// </para>
/// <para>
/// <b>Why the C# EVCC replays its own recording.</b> It sounds circular, and as a correctness check it
/// is. It is not there for correctness — it is there to prove the corpus is <i>replayable</i>: that a
/// session really is a pure function of the responses, that nothing in the loop reaches for a clock or a
/// random number that a file cannot supply, and that the replay harness the two ports are about to copy
/// actually works. Finding that out from Kotlin, with a fresh port also under suspicion, would cost far
/// more than finding it out here. It is the same reason the cross-emitter comparison had to be built
/// before it could be counted as a gate (§5, Track A note).
/// </para>
/// <para>
/// <b>EIM only, for now.</b> Byte-exact replay needs the requests to be a pure function of the responses.
/// ECDSA signing is randomised, so a PnC session's signed AuthorizationReq differs on every run and could
/// only be compared with the signature excluded. That comparison is owed; until it exists, saying "the
/// corpus is EIM" is more honest than quietly recording PnC and comparing everything but the part that
/// makes it PnC.
/// </para>
/// </remarks>
[TestFixture]
public class SessionTraceCorpusTests
{

    /// <summary>Pinned so a re-recording is byte-identical. -20 puts a timestamp in every message header,
    /// so without this the corpus could not be regenerated and diffed at all.</summary>
    private static readonly DateTimeOffset RecordedAt = DateTimeOffset.FromUnixTimeSeconds(1_767_225_600);

    /// <summary>Likewise pinned — see <c>Secc2.FixedSessionId</c> for why this seam exists.</summary>
    private static readonly byte[] RecordedSessionId = [0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11];

    /// <summary>Pinned too: -20 sends a 16-byte challenge in every AuthorizationSetupRes, EIM sessions
    /// included. Test material, never a live challenge.</summary>
    private static readonly byte[] RecordedGenChallenge =
        [0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F];

    private static readonly TimeSpan SeccSequenceTimeout = TimeSpan.FromSeconds(60);


    /// <summary>One recordable session. Both sides are spelled out as delegates because the recorder needs
    /// to drive the EVCC over a stream of its choosing — a socket when recording, a file when replaying —
    /// and that is the whole trick: the EVCC never learns which it got.</summary>
    private sealed record Scenario(
        string                                            Name,
        string                                            Protocol,
        string                                            Mode,
        string                                            Note,
        Func<Stream, TimeProvider, CancellationToken, Task> RunEvcc,
        Func<Stream, TimeProvider, Func<string, OcppTransactionRecorder>?, CancellationToken, Task> RunSecc,
        /// <summary>The identity this car's requests are signed with, or <c>null</c> for a session that
        /// signs nothing. Its public half goes into the trace so a replay can verify what a port signed —
        /// the half the byte comparison deliberately discards.
        /// <para>
        /// A function rather than a flag because there is now more than one answer. A Plug &amp; Charge
        /// session signs with its contract key; a car asking for its first contract has no contract and
        /// signs with the OEM key it was built with; a renewal signs with the contract it is replacing. It
        /// was a <c>bool</c> until contract provisioning, when the corpus stopped having one signer.
        /// </para></summary>
        Func<ECDsa>?                                      SignedBy = null,
        bool                                              Metered  = false,
        /// <summary>The station signs its SalesTariffs (§7.9.2.5). Like <see cref="SignedBy"/> this makes
        /// the recording randomised — ECDSA picks a fresh nonce per run — but it attaches no key to the
        /// trace: the verify key lives in <c>Tariff.signature.vectors.json</c>, which the ports already read,
        /// and duplicating it into the trace schema would give one operator identity two homes.</summary>
        bool                                              TariffSigned = false,
        /// <summary>
        /// Response message names whose bytes a second recording cannot reproduce, because the station
        /// mints something new every time it answers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only contract provisioning is like this, and it is not a defect to be pinned down — it is
        /// what the exchange <i>is</i>. Issuing a contract means minting a certificate and a key pair,
        /// and wrapping that key means a fresh ephemeral ECDH pair and a fresh IV. A station that
        /// answered two requests with the same bytes would be handing two cars the same private key.
        /// </para>
        /// <para>
        /// So <see cref="RecordingTheCorpusAgainProducesTheSameBytes"/> skips these responses and
        /// compares everything else, requests included — and the requests are what the ports are held
        /// to, since a replay feeds them the recorded responses out of the file. What is given up is
        /// the readability of a re-record's diff for three files, which is why the names are listed
        /// here one by one rather than the whole scenario being waved through.
        /// </para>
        /// </remarks>
        IReadOnlyList<string>?                            FreshlyIssuedResponses = null)
    {

        /// <summary>Whether this session's requests carry a signature — which is exactly whether it named
        /// somebody to sign them.</summary>
        public bool Signed => SignedBy is not null;

    }


    /// <summary>
    /// The station meter the metered scenarios fit, keyed off a fixed private key so the corpus
    /// records one meter identity rather than a fresh one per regeneration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fixing the key does <b>not</b> make the recording reproducible — ECDSA still picks its nonce
    /// at random, so every reading is 64 fresh bytes. What it fixes is <c>meterKey</c>, which would
    /// otherwise change on every regeneration and make the diff of a re-record useless. The same
    /// distinction <c>MeterVectorTests</c> had to correct in its own summary.
    /// </para>
    /// <para>
    /// Test material. It is checked into a public repository, which is exactly why it must never be
    /// a key any real meter uses.
    /// </para>
    /// </remarks>
    private const string MeterKeyD = "4a1f0f0b1d5f7c3e9a2b8c6d4e0f1a3b5c7d9e0f2a4b6c8d0e1f3a5b7c9d0e1f";

    private static ECDsa MeterKeyPair()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        key.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D     = Convert.FromHexString(MeterKeyD),
        });
        return key;
    }

    /// <summary>
    /// A meter that starts at zero and counts what the session actually delivers.
    /// </summary>
    /// <remarks>
    /// It used to be primed with a fixed 4200 Wh, which made the recorded reading a constant — fine
    /// while nothing compared it to anything, and wrong the moment the vehicle grew a counter of its
    /// own (2026-08-03): two unrelated numbers side by side would have shown a difference that meant
    /// nothing. Now both sides count the same <see cref="ChargeLoopSample.Period"/> at the same power,
    /// so the reading climbs through the session and lands where the EV's does.
    /// </remarks>
    private static SigningMeter Meter(TimeProvider clock) =>
        new("VAN*M*4711", MeterKeyPair(), clock);


    private static readonly Scenario[] Scenarios =
    [
        new("iso2-ac-eim", "iso15118-2", "ac",
            "AC, external payment, happy path: SAP, setup, service discovery, authorization poll, " +
            "charge-parameter discovery, three charging-status cycles, stop.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Evcc2(stream, PowerMode.Ac, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage).RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Secc2(PowerMode.Ac, SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        new("iso2-dc-eim", "iso15118-2", "dc",
            "DC, external payment: as the AC session, plus cable check, pre-charge, current demand " +
            "and welding detection — the phases AC does not have.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Dc);
                await new Evcc2(stream, PowerMode.Dc, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage).RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Dc);
                await new Secc2(PowerMode.Dc, SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        // The two scenarios below were added on 2026-08-16, and they are the point of the exercise:
        // everything above them records an EVCC that stopped changing on 2026-08-07, so the ports
        // could be held to it and still be nine days behind. A recording is the only way a port
        // learns that the car it mirrors has moved.

        new("iso2-dc-eim-battery", "iso15118-2", "dc",
            "DC, external payment, and the loop ends because the battery is full rather than after a " +
            "fixed three cycles. Every session above this one charges for a count; this one charges " +
            "to a state of charge, which is what the car actually does since 2026-08-08.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Dc);
                await new Evcc2(stream, PowerMode.Dc, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage)
                          { Battery = new cloud.charging.open.protocols.ISO15118.Simulation.EvBattery(60.0, 20.0)
                                      { TargetSoC = 22.0, MaxIterations = 100 } }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Dc);
                await new Secc2(PowerMode.Dc, SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        new("iso2-dc-eim-renegotiate", "iso15118-2", "dc",
            "DC, external payment, with one renegotiation ([V2G2-841]) — which on DC returns through " +
            "CableCheck and PreCharge rather than straight back to the charge loop. Ported EVCCs have " +
            "the reactive and proactive triggers; nothing has ever held them to the DC return path.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Dc);
                await new Evcc2(stream, PowerMode.Dc, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage)
                          { Renegotiate = true }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Dc);
                await new Secc2(PowerMode.Dc, SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        new("iso20-ac-eim", "iso15118-20", "ac",
            "-20 AC, EIM. Crosses between the CommonMessages and AC message sets, which are separate " +
            "grammars with separate V2GTP payload types — a port that muddles the two is visible here " +
            "in the frame header, two bytes in.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Evcc20Ac(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage).RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Secc20Ac(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        new("iso20-dc-eim", "iso15118-20", "dc",
            "-20 DC, EIM: CommonMessages plus the DC set, including the cable-check and pre-charge " +
            "poll phases and welding detection.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage).RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        // ── Dynamic control mode ──────────────────────────────────────────
        //
        // Recorded the day the EVCC learned to drive Dynamic (2026-08-03), and for the ports rather
        // than for C#: the Kotlin and Swift EVCCs are validated against this corpus, so a mode with
        // no recorded session is a mode the ports can claim without ever being checked — the same
        // blind spot the roadmap names, one layer along. The mode touches four places (parameter
        // set, ScheduleExchange, EVPowerProfile, charge loop), and every one of them is in these
        // bytes.

        new("iso20-dc-eim-dynamic", "iso15118-20", "dc",
            "-20 DC, EIM, Dynamic control mode: the EV states energy needs and a departure time " +
            "instead of picking a schedule tuple — ControlMode = 2 in ServiceSelection, the Dynamic " +
            "arms in ScheduleExchange, the EVPowerProfile and the charge loop, the station " +
            "answering in kind ([V2G20-1600]).",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage) { PreferDynamicControlMode = true }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        // The -20 counterpart of `iso2-dc-eim-renegotiate`, and the first recorded session in which a -20
        // station changes its mind mid-charge. [V2G20-1477] is not the -2 renegotiation with different
        // spelling: there the EV re-runs ChargeParameterDiscovery within the same phase, here the session
        // goes all the way back to ServiceDiscovery and re-selects the energy service before it may charge
        // again. What must NOT repeat is authorization — a car that re-authorized mid-session would be
        // telling the station it might be a different car — and only a recording can hold that, because
        // "authorization did not happen again" is a claim about bytes that are absent.
        new("iso20-dc-eim-renegotiate", "iso15118-20", "dc",
            "-20 DC, EIM, with a service renegotiation ([V2G20-1477]): the station puts " +
            "EvseNotification.ServiceRenegotiation in its first charge-loop response, the EV stops power " +
            "delivery and sends SessionStopReq(ServiceRenegotiation) — the one SessionStop that does not " +
            "stop the session — and both sides return to ServiceDiscovery, re-select the service, " +
            "re-negotiate charge parameters and the schedule, and charge again.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage).RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId       = RecordedSessionId,
                            FixedGenChallenge    = RecordedGenChallenge,
                            RequestRenegotiation = true,
                            Backend              = backend }.RunAsync(stream, ct);
            }),

        new("iso20-ac-eim-dynamic", "iso15118-20", "ac",
            "-20 AC, EIM, Dynamic control mode: as the DC one, on the AC message set — the arm " +
            "Evcc20Ac carries and the DC trace cannot reach.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Evcc20Ac(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage) { PreferDynamicControlMode = true }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Secc20Ac(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        // ── the multi-protocol offer ──────────────────────────────────────
        //
        // Both protocols in ONE SupportedAppProtocol request, the state machine chosen after the
        // handshake — the case a multiplexing station exists for. Two traces because the two answers
        // differ in the one byte that decides everything: the station's SchemaID names which entry
        // won, and the ports must run the machine it names rather than the one they would prefer.

        new("iso2-ac-eim-sapboth", "iso15118-2", "ac",
            "AC, EIM, negotiated: the EV offers -20 AC at priority 1 and -2 at priority 2 in one " +
            "handshake; this station speaks only -2, answers SchemaID 2 — the priority-2 entry — " +
            "and the -2 session runs. Also the first recording in which the SECC's answered " +
            "SchemaID is not 1, which is what shows it echoes the accepted entry rather than a literal.",
            async (stream, clock, ct) =>
            {
                var accepted = await SapHandshake.RunEvccSideAsync(stream,
                    new SapOffer[] { new(ProtocolVariant.Iso15118_20, PowerMode.Ac),
                                     new(ProtocolVariant.Iso15118_2,  PowerMode.Ac) }, ct);
                if (accepted.Protocol != ProtocolVariant.Iso15118_2)
                    throw new InvalidOperationException("this station is -2-only; the negotiation must settle on -2");
                await new Evcc2(stream, PowerMode.Ac, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage).RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Secc2(PowerMode.Ac, SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        new("iso20-dc-eim-sapboth", "iso15118-20", "dc",
            "-20 DC, EIM, negotiated: the same two-entry offer at a -20 station, which answers " +
            "SchemaID 1 — the priority-1 entry — and the -20 session runs.",
            async (stream, clock, ct) =>
            {
                var accepted = await SapHandshake.RunEvccSideAsync(stream,
                    new SapOffer[] { new(ProtocolVariant.Iso15118_20, PowerMode.Dc),
                                     new(ProtocolVariant.Iso15118_2,  PowerMode.Dc) }, ct);
                if (accepted.Protocol != ProtocolVariant.Iso15118_20)
                    throw new InvalidOperationException("this station speaks -20; the negotiation must settle on -20");
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage).RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        // ── metered sessions ──────────────────────────────────────────────
        //
        // A station whose meter signs what it measured. The field is standard and almost never
        // populated — that is the whole reason to populate it (docs/CONCEPT.md §4.3) — and until
        // these two were recorded no session in the corpus carried one, so nothing downstream of the
        // recorder had ever seen a signed reading: not the bridge, not the app, not the ports.
        //
        // EIM, deliberately. A meter reports every cycle regardless of how the driver authorized, so
        // EIM is both the simpler case and the commoner one; and it keeps the meter's signature out
        // of any *signed* request, where it would land inside the fragment the EV digests and make
        // two substitutions fight over one digest. That case is real and is written down as open
        // rather than quietly avoided — see TheMeterSignatureIsNotInsideASignedFragment.
        //
        // Both protocols, because the protocol byte in the signed payload is the one thing keeping a
        // -20 reading from being presented as a -2 one, and it never appears on the wire: only a
        // recorded session can show the two are actually signed differently.

        new("iso2-ac-eim-meter", "iso15118-2", "ac",
            "AC, EIM, with a signing meter fitted: every ChargingStatusRes carries a MeterInfo whose " +
            "SigMeterReading is a real P-256 signature over the station's own reading, bound to this " +
            "session. The first recorded session a vehicle could verify a station's meter from.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Evcc2(stream, PowerMode.Ac, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage).RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Secc2(PowerMode.Ac, SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            InstalledMeter    = Meter(clock),
                            Backend           = backend }.RunAsync(stream, ct);
            },
            Metered: true),

        // The offer stops being a formality here. Every other -2 session gets the plain single unsigned
        // tuple; this station offers two, prices them, and signs both SalesTariffs into the response
        // header (§7.9.2.5). So this is the only recorded session in which the EV's tuple choice is a
        // *choice* — it picks tuple 2 on average price, 1.5 against 2.5 — and the only one whose header
        // carries a signature the EV is expected to check rather than relay.
        //
        // What the trace can and cannot show: the bytes, and only the bytes. The verdict itself never
        // reaches the wire, which is why Tariff.signature.vectors.json exists beside this. The two are
        // deliberately signed by the same key, so a port can read the verify key out of that corpus and
        // apply it to this session — one Mobility Operator, two kinds of evidence.
        new("iso2-ac-eim-tariff", "iso15118-2", "ac",
            "AC, EIM, with signed SalesTariffs: the station offers two priced tuples and signs both " +
            "into the ChargeParameterDiscoveryRes header (one reference per tariff, §7.9.2.5). The EV " +
            "picks the cheaper tuple on average EPriceLevel and shapes its ChargingProfile to it. The " +
            "only recorded session where the schedule offer is a decision rather than a formality.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                using var verifyKey = TariffSignatureCorpusTests.TariffKey();
                await new Evcc2(stream, PowerMode.Ac, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage)
                          { TariffVerifyKey = verifyKey }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                using var signKey = TariffSignatureCorpusTests.TariffKey();
                await new Secc2(PowerMode.Ac, SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            TariffSignKey     = signKey,
                            Backend           = backend }.RunAsync(stream, ct);
            },
            TariffSigned: true),

        new("iso20-dc-eim-meter", "iso15118-20", "dc",
            "-20 DC, EIM, with the same meter fitted: the reading rides in MeterInfo.MeterSignature " +
            "instead of SigMeterReading, over the same payload layout with the protocol byte set to " +
            "20 — so this trace and the -2 one differ in a byte that is never transmitted.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage).RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            InstalledMeter    = Meter(clock),
                            Backend           = backend }.RunAsync(stream, ct);
            },
            Metered: true),

        // ── signed sessions ───────────────────────────────────────────────
        //
        // The reason schema 2 exists. Both of these carry a signed AuthorizationReq, which is not
        // byte-reproducible: ECDSA picks its nonce at random. SignedFrame is what makes them
        // comparable anyway — see it for the argument, and TheSignatureAwareComparisonActuallyBites
        // below for the evidence that it is a check rather than a hole.

        new("iso2-ac-pnc", "iso15118-2", "ac",
            "AC, Plug & Charge: PaymentDetails carries the contract chain, the AuthorizationReq is " +
            "signed over its own EXI fragment in the Josev interop form. The only session in the " +
            "corpus whose requests are not a pure function of the responses.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                using var contractKey = PncMaterial.Key();
                await new Evcc2(stream, PowerMode.Ac, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage)
                          {
                              Pnc = new PncEvccOptions(PncMaterial.Certificate(),
                                                       [PncMaterial.Certificate()], contractKey),
                          }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Secc2(PowerMode.Ac, SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            },
            SignedBy: PncMaterial.Key),

        new("iso20-dc-pnc", "iso15118-20", "dc",
            "-20 DC, Plug & Charge: the AuthorizationReq echoes the station's GenChallenge and " +
            "carries the contract chain, signed over the PnC_AReqAuthorizationMode fragment.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                using var contractKey = PncMaterial.Key();
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
                          {
                              Pnc = new PncEvccOptions(PncMaterial.Certificate(),
                                                       [PncMaterial.Certificate()], contractKey),
                          }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            },
            SignedBy: PncMaterial.Key),

        // ── contract provisioning ─────────────────────────────────────────
        //
        // The sessions of a car that does not yet have what every other signed session here starts
        // with. Signed like the two above and for the same mechanical reason, but by a different
        // identity — which is what turned Scenario.Signed from a bool into a signer.
        //
        // What these traces can and cannot show, as with the tariff recording: the sequence, and only
        // the sequence. Whether the answer's four-reference signature held, and whether the unwrapped
        // key is the one the operator issued, never reach the wire — Contract.provisioning.vectors.json
        // is where those live. What is only recordable is *where in the session the exchange sits*: -2
        // finds the certificate service in the ServiceList and selects it by id before PaymentDetails,
        // and -20 asks before its first AuthorizationReq. A port that put either in the wrong place
        // would still pass every corpus case.

        new("iso2-ac-eim-certinstall", "iso15118-2", "ac",
            "AC, external payment, and a car carrying only the certificate its manufacturer built it " +
            "with. It finds the contract-certificate service in the station's ServiceList, selects it " +
            "by id alongside the charge service, and sends a signed CertificateInstallationReq before " +
            "PaymentDetails. The answer carries four separately signed elements and a contract private " +
            "key ECDH-wrapped for the OEM key (§7.9.2.4).",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                using var signKey   = OemMaterial.Iso2SignKey();
                using var agreement = OemMaterial.Iso2KeyAgreement();
                await new Evcc2(stream, PowerMode.Ac, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage)
                          {
                              CertInstallRequest = new Iso2CertInstallOptions(
                                                       OemMaterial.Iso2Certificate(), signKey, agreement),
                          }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Secc2(PowerMode.Ac, SeccSequenceTimeout, clock)
                          { FixedSessionId          = RecordedSessionId,
                            FixedGenChallenge       = RecordedGenChallenge,
                            OfferCertificateService = true,
                            Backend                 = backend }.RunAsync(stream, ct);
            },
            SignedBy: OemMaterial.Iso2SignKey,
            FreshlyIssuedResponses: ["CertificateInstallationResType"]),

        new("iso2-ac-eim-certupdate", "iso15118-2", "ac",
            "The renewal. Same shape as the installation and a different credential throughout: the " +
            "car presents the contract that is running out, selects parameter set 2 rather than 1, " +
            "and the answer is wrapped for the expiring contract's own key — which is what makes an " +
            "update need no other proof of who is asking. The eMAID comes back unchanged: a renewal " +
            "renews a contract, it does not issue a different one.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                using var signKey   = OemMaterial.Iso2ExpiringSignKey();
                using var agreement = OemMaterial.Iso2ExpiringKeyAgreement();
                await new Evcc2(stream, PowerMode.Ac, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage)
                          {
                              CertInstallRequest = new Iso2CertInstallOptions(
                                                       OemMaterial.Iso2ExpiringCertificate(), signKey, agreement,
                                                       Iso2CertificateAction.Update,
                                                       OemMaterial.Iso2ExpiringEmaid()),
                          }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Secc2(PowerMode.Ac, SeccSequenceTimeout, clock)
                          { FixedSessionId          = RecordedSessionId,
                            FixedGenChallenge       = RecordedGenChallenge,
                            OfferCertificateService = true,
                            Backend                 = backend }.RunAsync(stream, ct);
            },
            SignedBy: OemMaterial.Iso2ExpiringSignKey,
            FreshlyIssuedResponses: ["CertificateUpdateResType"]),

        new("iso20-dc-eim-certinstall", "iso15118-20", "dc",
            "-20 DC, and the same errand by an entirely different route: no value-added service to " +
            "discover and select, just a flag in AuthorizationSetupRes, and the request goes out " +
            "BEFORE the first AuthorizationReq rather than after service selection. One signed " +
            "element instead of four, on a P-521 key with SHA-512 — the first recorded session whose " +
            "signing identity is not P-256.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                using var signKey   = OemMaterial.Iso20SignKey();
                using var agreement = OemMaterial.Iso20KeyAgreement();
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
                          {
                              CertInstallRequest = new CertInstallEvccOptions(
                                                       OemMaterial.Iso20Certificate(), [], signKey, agreement),
                          }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            },
            SignedBy: OemMaterial.Iso20SignKey,
            FreshlyIssuedResponses: ["CertificateInstallationRes"]),
    ];


    private static IEnumerable<string> ScenarioNames => Scenarios.Select(s => s.Name);

    private static string FileName(string name) => $"Session.{name}.trace.json";

    /// <summary>Where the tests read it from — copied next to the assembly by the csproj.</summary>
    private static string TracePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors", FileName(name));

    /// <summary>Where the regenerator writes it: <c>libs/EVSimulatorApp/vectors/</c>, the submodule's
    /// source tree. Writing to the output directory would produce a corpus that vanishes on the next clean,
    /// and one the Kotlin and Swift suites — which read it out of the source tree — would never see.
    /// <para>
    /// It writes DOWN into the submodule rather than beside these tests because that is where everything
    /// held to it lives. The ports used to read it back up here as
    /// <c>../../ISO15118ConformanceTests.Simulation/Vectors/</c>, which made a submodule depend on its own
    /// superproject: EVSimulatorApp could not pass its own suite standalone, and its CI had to lay this
    /// repository out first purely to put a directory of JSON above the tree under test. Regenerating now
    /// dirties the submodule, so it takes a commit there and a gitlink bump here — which is the honest
    /// shape of generated data crossing that boundary.
    /// </para></summary>
    private static string SourceTracePath(string name)
    {

        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ISO15118ConformanceTests.Simulation.csproj")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not find the test project directory");

        var vectors = Path.Combine(dir!.FullName, "..", "libs", "EVSimulatorApp", "vectors");
        Directory.CreateDirectory(vectors);
        return Path.Combine(vectors, FileName(name));

    }


    /// <summary>
    /// Records every scenario over a real loopback socket and writes the corpus.
    /// <see cref="ExplicitAttribute"/> for the same reason the meter corpus is: these files are an oracle
    /// for two other languages, so they must change when somebody means them to and never as a side
    /// effect of a test run.
    /// </summary>
    /// <summary>
    /// Creates the fixed contract identity the signed scenarios use. Separate from the corpus
    /// regenerator and even more deliberate: its bytes are an *input* to every recorded PnC session,
    /// so running this invalidates those traces and every port checked against them. Run it, then run
    /// <see cref="RegenerateTheCorpus"/>.
    /// </summary>
    [Test, Explicit("Regenerates Vectors/Session.pnc-material.json — invalidates every signed trace")]
    public void RegenerateThePncMaterial()
    {
        TestContext.Out.WriteLine($"wrote {PncMaterial.Regenerate()}");
    }

    /// <summary>
    /// Creates the factory identities the provisioning sessions ask with — the same deliberate,
    /// separate, idempotent step as <see cref="RegenerateThePncMaterial"/>, and invalidating a
    /// different set of traces. See <see cref="OemMaterial"/> for why there are three of them.
    /// </summary>
    [Test, Explicit("Regenerates Vectors/Session.oem-material.json — invalidates every provisioning trace")]
    public void RegenerateTheOemMaterial()
    {
        TestContext.Out.WriteLine($"wrote {OemMaterial.Regenerate()}");
    }


    [Test, Explicit("Regenerates Vectors/Session.*.trace.json and Session.ocpp-transactions.json")]
    public async Task RegenerateTheCorpus()
    {

        var transactions = new SortedDictionary<string, OcppTransactionRecord>(StringComparer.Ordinal);

        foreach (var scenario in Scenarios)
        {
            var (trace, ocpp) = await RecordWithBackendAsync(scenario);
            var path = SourceTracePath(scenario.Name);
            trace.WriteTo(path);
            transactions[scenario.Name] = ocpp;
            TestContext.Out.WriteLine($"{scenario.Name}: {trace.Exchanges.Count} exchanges, " +
                                      $"{ocpp.MeterValues.Count} meter value(s) → {path}");
        }

        var ocppPath = Path.Combine(Path.GetDirectoryName(SourceTracePath("x"))!, OcppFileName);
        File.WriteAllText(ocppPath, OcppCorpusJson(transactions), new UTF8Encoding(false));
        TestContext.Out.WriteLine($"-> {ocppPath}");

    }


    /// <summary>Every session's transaction record in one file, keyed by session name.</summary>
    /// <remarks>
    /// One file rather than a companion per trace, for the reason <c>Bridge.events.json</c> is one
    /// file: these are small, they are always read together, and a reviewer judging a regeneration
    /// wants one diff rather than twelve.
    /// </remarks>
    private const string OcppFileName = "Session.ocpp-transactions.json";

    private static string OcppCorpusJson(IReadOnlyDictionary<string, OcppTransactionRecord> transactions)
    {
        var body = string.Join(",\n", transactions.Select(pair =>
            $"    \"{pair.Key}\": " + Indent(pair.Value.ToJson(), "    ")));

        return "{\n" +
               "  \"schemaVersion\": 1,\n" +
               "  \"generator\": \"ISO15118ConformanceTests.Simulation.Traces.SessionTraceCorpusTests.RegenerateTheCorpus\",\n" +
               "  \"note\": \"What each recorded station would have reported to its backend. NOT an " +
               "independent third measurement: it is the same station as the MeterInfo on the wire, so " +
               "the energies cannot disagree in a way that means anything. What it does support is the " +
               "one question a phone can settle without a CSMS — whether the station told the backend " +
               "the same signed values it showed the car. See Ocpp/OcppTransactionRecord.cs.\",\n" +
               "  \"transactions\": {\n" + body + "\n  }\n}\n";
    }

    private static string Indent(string json, string by) =>
        string.Join("\n", json.Split('\n').Select((line, i) => i == 0 ? line : by + line));


    private static async Task<SessionTrace> RecordAsync(Scenario scenario) =>
        (await RecordWithBackendAsync(scenario)).Trace;


    /// <summary>
    /// Records one session and keeps what the station would have told its backend.
    /// </summary>
    /// <remarks>
    /// The recorder is handed <em>to</em> the SECC rather than derived from the frames afterwards,
    /// and that is the whole point: a record reconstructed from the wire could never contain a value
    /// the car did not see, which is exactly the case worth being able to look at.
    /// </remarks>
    private static async Task<(SessionTrace Trace, OcppTransactionRecord Ocpp)>
        RecordWithBackendAsync(Scenario scenario)
    {

        using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

        var clock = new ManualTimeProvider(RecordedAt);

        OcppTransactionRecorder? recorder = null;

        var seccTask = Task.Run(async () =>
        {
            using var seccStream = await listener.AcceptAsync(cts.Token);
            await scenario.RunSecc(seccStream, clock, sessionId =>
                recorder = new OcppTransactionRecorder($"TX-{scenario.Name}", "DE*ABC*E1", sessionId),
                cts.Token);
        }, cts.Token);

        using var socket = await TcpV2GClient.ConnectAsync(
                                     IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);

        var recordingStream = new RecordingStream(socket);
        await Task.WhenAll(scenario.RunEvcc(recordingStream, clock, cts.Token), seccTask);

        TraceSigningKey? signingKey = null;
        if (scenario.SignedBy is { } signer)
        {
            using var key = signer();
            signingKey = PublicHalfOf(key);
        }

        TraceSigningKey? meterKey = null;
        if (scenario.Metered)
        {
            using var key = MeterKeyPair();
            meterKey = PublicHalfOf(key);
        }

        var trace = SessionTrace.Build(scenario.Name, scenario.Protocol, scenario.Mode, scenario.Note,
                                       recordingStream.Sent, recordingStream.Received, signingKey, meterKey);

        return (trace,
                recorder?.Build()
                    ?? new OcppTransactionRecord($"TX-{scenario.Name}", "DE*ABC*E1", "", []));

    }


    /// <summary>The public half of a signing identity, with the curve named. Written down rather than
    /// inferred from the coordinate width: a reader that guessed would be right only until a third curve
    /// arrived, and would then be wrong silently.</summary>
    private static TraceSigningKey PublicHalfOf(ECDsa key)
    {
        var q = key.ExportParameters(includePrivateParameters: false).Q;
        return new TraceSigningKey(Convert.ToHexString(q.X!).ToLowerInvariant(),
                                   Convert.ToHexString(q.Y!).ToLowerInvariant(),
                                   key.KeySize == 521 ? "P-521" : "P-256");
    }


    /// <summary>
    /// The corpus is replayable: the EVCC, given nothing but the recorded responses, emits byte for byte
    /// the requests that were recorded — including the V2GTP headers and the SupportedAppProtocol
    /// handshake. This is the gate the Kotlin and Swift ports will meet in their own languages.
    /// </summary>
    [Test]
    public async Task EveryTraceReplaysByteExactly([ValueSource(nameof(ScenarioNames))] string name)
    {

        var scenario = Scenarios.Single(s => s.Name == name);
        var path     = TracePath(name);

        Assert.That(File.Exists(path), Is.True,
                    $"trace missing: {path} — run RegenerateTheCorpus");

        var trace  = SessionTrace.ReadFrom(path);
        var replay = new TraceReplayStream(trace);

        await scenario.RunEvcc(replay, new ManualTimeProvider(RecordedAt), CancellationToken.None);

        Assert.That(replay.Complete, Is.True,
                    $"the session stopped after {replay.Replayed} of {trace.Exchanges.Count} recorded " +
                     "exchanges — it ended early, which sends no wrong bytes and would otherwise pass.");

    }


    /// <summary>
    /// Recording again produces the same file. That is what makes a checked-in corpus reviewable: a
    /// regeneration whose diff is total says nothing about what actually changed, and the session id and
    /// the -20 GenChallenge — both random by default, both present in every session — would each be
    /// enough on their own to make every frame differ.
    /// </summary>
    /// <remarks>
    /// A running check rather than a note in a doc comment, because this repository has now recorded that
    /// distinction four times (<c>docs/CONCEPT.md</c> §5, Track A note) and once more here: the meter
    /// corpus claims in its own summary that "anyone can regenerate it and get identical bytes", and its
    /// signatures are randomised ECDSA, so that sentence is true only of the payload field.
    /// </remarks>
    /// <remarks>
    /// <b>Both directions, since 2026-08-03.</b> This used to compare a signed session's *requests*
    /// only and leave its responses out of the comparison entirely — harmless while the EV was the
    /// sole signer, and a hole the moment a station's meter started signing too. A station's reading
    /// travels in a response, so the direction that was not being compared was exactly the one the
    /// next feature needed. Comparing both is also strictly stronger for the existing PnC traces,
    /// whose responses had never been checked at all.
    /// </remarks>
    [Test]
    public async Task RecordingTheCorpusAgainProducesTheSameBytes(
        [ValueSource(nameof(ScenarioNames))] string name)
    {

        var scenario = Scenarios.Single(s => s.Name == name);
        var recorded = await RecordAsync(scenario);
        var onDisk   = SessionTrace.ReadFrom(TracePath(name));

        Assert.Multiple(() =>
        {
            Assert.That(recorded.Protocol,   Is.EqualTo(onDisk.Protocol));
            Assert.That(recorded.Mode,       Is.EqualTo(onDisk.Mode));
            Assert.That(recorded.Note,       Is.EqualTo(onDisk.Note));
            Assert.That(recorded.SigningKey, Is.EqualTo(onDisk.SigningKey));
            Assert.That(recorded.MeterKey,   Is.EqualTo(onDisk.MeterKey),
                        "the meter identity changed — a fresh key would make every re-record's diff total");
        });

        Assert.That(recorded.Exchanges, Has.Count.EqualTo(onDisk.Exchanges.Count));

        // What the substitution threw away, so it can be required to be non-empty exactly when the
        // session has a random part — and empty otherwise, which is the strong statement an EIM
        // recording gets to make.
        var randomised = new List<string>();

        for (var i = 0; i < onDisk.Exchanges.Count; i++)
        {
            foreach (var (fresh, old, direction) in new[]
                     {
                         (recorded.Exchanges[i].Request,  onDisk.Exchanges[i].Request,  "request"),
                         (recorded.Exchanges[i].Response, onDisk.Exchanges[i].Response, "response"),
                     })
            {
                Assert.That(fresh.IsSigned, Is.EqualTo(old.IsSigned),
                            $"exchange {i} {direction}: one recording thinks this frame is signed and " +
                             "the other does not");
                Assert.That(fresh.CarriesMeterSignature, Is.EqualTo(old.CarriesMeterSignature),
                            $"exchange {i} {direction}: one recording found a meter reading here and " +
                             "the other did not");

                if (fresh.Signature      != old.Signature)      randomised.Add($"{i} {direction} signature");
                if (fresh.MeterSignature != old.MeterSignature) randomised.Add($"{i} {direction} meter");

                // A response that issues a contract cannot be reproduced and must not be — see
                // Scenario.FreshlyIssuedResponses. Its identity is still checked, so a session that
                // silently sent a different message here would not slip through under this exemption.
                if (direction == "response" &&
                    (scenario.FreshlyIssuedResponses?.Contains(old.Message) ?? false))
                {
                    Assert.That(fresh.Message,     Is.EqualTo(old.Message),     $"exchange {i} {direction}");
                    Assert.That(fresh.PayloadType, Is.EqualTo(old.PayloadType), $"exchange {i} {direction}");
                    randomised.Add($"{i} {direction} issued material");
                    continue;
                }

                // Substitute the recorded random parts back in, then require everything — payload
                // type, message name, and every other byte — to be identical.
                var comparable = fresh with
                {
                    Frame          = Convert.ToHexString(Restore(fresh, old)).ToLowerInvariant(),
                    Signature      = old.Signature,
                    MeterSignature = old.MeterSignature,
                };

                Assert.That(comparable, Is.EqualTo(old),
                            $"exchange {i} {direction} ({old.Message}) differs — either something is no " +
                             "longer pinned, or the session really changed and the corpus needs " +
                             "regenerating on purpose");
            }
        }

        // The half the substitution discards. Two runs must produce *different* random parts, or the
        // signing is not randomised and the whole mechanism was unnecessary; and a session with no
        // signer at all must reproduce exactly, which is what makes a re-record's diff readable.
        if (scenario.Signed || scenario.Metered || scenario.TariffSigned)
            Assert.That(randomised, Is.Not.Empty,
                        "two recordings came out byte-identical, so nothing here is actually " +
                        "randomised and the substitution is solving a problem that does not exist");
        else
            Assert.That(randomised, Is.Empty,
                        "an unsigned, unmetered session recorded a value that changes between runs: " +
                        $"{string.Join(", ", randomised)}");

    }


    /// <summary>Puts the checked-in random parts back into a freshly recorded frame.</summary>
    private static byte[] Restore(TraceFrame fresh, TraceFrame old)
    {
        var bytes = fresh.Bytes;

        // Order does not matter: the two live in different fields — one in the message header, one
        // in the body's MeterInfo — and each substitution decodes and re-encodes the whole frame.
        if (old.SignatureBytes      is { } signature) bytes = SignedFrame.WithSignatureValue(bytes, signature);
        if (old.MeterSignatureBytes is { } reading)   bytes = SignedFrame.WithMeterSignature(bytes, reading);

        return bytes;
    }


    /// <summary>
    /// A trace that stops before the interesting phases replays perfectly and proves nothing, so the
    /// corpus is checked for the shapes it is supposed to contain rather than only for replaying.
    /// </summary>
    [Test]
    public void TheCorpusCoversTheSessionShapes([ValueSource(nameof(ScenarioNames))] string name)
    {

        var trace    = SessionTrace.ReadFrom(TracePath(name));
        var messages = trace.Exchanges.Select(e => e.Request.Message).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(trace.Exchanges, Has.Count.GreaterThan(8));

            Assert.That(messages[0], Does.StartWith("SupportedAppProtocol"),
                        "every session starts with the SAP handshake, and the ports need it recorded");

            Assert.That(messages, Has.Some.Matches<string>(m => m.StartsWith("SessionSetupReq")));
            Assert.That(messages, Has.Some.Matches<string>(m => m.StartsWith("SessionStopReq")),
                        "a trace that never reaches SessionStop is a truncated session");

            // The DC-only phases. Their absence from an AC trace is equally part of the shape: an EVCC
            // that ran a cable check on AC would be wrong, and only a per-mode expectation says so.
            // Substring, not prefix: the same phase is WeldingDetectionReqType in -2 and
            // DC_WeldingDetectionReq in -20, which is a naming difference and not a behavioural one.
            // "mcs" counts as DC here because MCS *is* the DC message set — it differs in the service
            // catalogue (ids 8/9) and the power envelope, not in the phases. Filed under its own mode name
            // so a capture says which catalogue it negotiated; asserted as DC so an MCS trace is held to
            // the cable check and the welding detection it really runs.
            var dc = trace.Mode is "dc" or "mcs";
            Assert.That(messages.Any(m => m.Contains("WeldingDetection")), Is.EqualTo(dc),
                        $"welding detection is DC-only; this is the '{trace.Mode}' trace");
            Assert.That(messages.Any(m => m.Contains("CableCheck")), Is.EqualTo(dc),
                        $"the cable check is DC-only; this is the '{trace.Mode}' trace");
            Assert.That(messages.Any(m => m.Contains("PreCharge")), Is.EqualTo(dc),
                        $"pre-charge is DC-only; this is the '{trace.Mode}' trace");

            // Every frame's payload type must be one the dispatcher recognises, and -20 must actually
            // cross message sets — a -20 session that never leaves CommonMessages is not a -20 session.
            var payloadTypes = trace.Exchanges
                                    .SelectMany(e => new[] { e.Request.PayloadType, e.Response.PayloadType })
                                    .Distinct().ToList();

            if (trace.Protocol is "iso15118-20")
                Assert.That(payloadTypes, Has.Count.GreaterThan(1),
                            "a -20 trace whose frames all carry one payload type never left CommonMessages");

            // A trace named for something the car signs — Plug & Charge, or asking for a contract —
            // that recorded no signature would be an EIM session with a misleading name, and the
            // signature-aware comparison would never run on it.
            //
            // This was `name.EndsWith("-pnc")` until contract provisioning, which is the same mistake
            // the Scenario record made: it read the name as though PnC were the only reason a car
            // signs. A car that has no contract yet signs with the key it was built with.
            var signs = Scenarios.Single(s => s.Name == name).Signed;
            Assert.That(trace.Exchanges.Any(e => e.Request.IsSigned), Is.EqualTo(signs),
                        signs ? "a trace whose scenario names a signer recorded no signed request"
                              : "a session that names no signer must carry no signatures");
            Assert.That(trace.SigningKey is not null, Is.EqualTo(signs),
                        "the signing key and the signatures must appear together, or verification is skipped");

            // And the same pairing for the meter. A trace named for a signing meter that recorded no
            // reading would be an ordinary session under a promising name — and the app, which reads
            // this corpus to show a station's signed reading, would have nothing to show and no way
            // to tell that apart from a station that simply has no meter.
            var isMetered = name.EndsWith("-meter");
            Assert.That(trace.Exchanges.Any(e => e.Response.CarriesMeterSignature), Is.EqualTo(isMetered),
                        isMetered ? "a metered trace whose responses carry no meter signature recorded "
                                  + "a station without a meter"
                                  : "a station with no meter fitted must sign no readings");
            Assert.That(trace.MeterKey is not null, Is.EqualTo(isMetered),
                        "the meter key and the meter signatures must appear together, or verification "
                      + "is skipped");
        });

    }


    /// <summary>
    /// Every recorded meter reading verifies, against the meter key the trace carries and the values
    /// that are actually on the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of recording a signed reading is that somebody downstream can check it, so the
    /// corpus checks it here first. Everything the verification needs is taken out of the frame —
    /// meter id, reading, timestamp, and the session id from the message header — because that is
    /// what a vehicle has, and verifying against values the recorder happens to still hold in memory
    /// would prove nothing about what was transmitted.
    /// </para>
    /// <para>
    /// <b>What a green here does not mean.</b> The key travels in the same file as the session it
    /// authenticates, so this says the reading was not altered between the meter and the file and is
    /// bound to this session. It does not say the station is who it claims — for that the key has to
    /// arrive out of band, which is the pairing code's job (<c>docs/CONCEPT.md</c> §4.5).
    /// </para>
    /// </remarks>
    [Test]
    public void EveryRecordedMeterReadingVerifies(
        [Values("iso2-ac-eim-meter", "iso20-dc-eim-meter")] string name)
    {

        var trace   = SessionTrace.ReadFrom(TracePath(name));
        var metered = trace.Exchanges.Where(e => e.Response.CarriesMeterSignature).ToList();

        Assert.That(metered, Is.Not.Empty, "no meter reading in a trace recorded with a meter fitted");

        using var key = KeyOf(trace.MeterKey!);

        Assert.Multiple(() =>
        {
            foreach (var exchange in metered)
            {
                Assert.That(exchange.Response.MeterSignatureBytes, Has.Length.EqualTo(64),
                            $"exchange {exchange.Index}: the field holds 64 bytes — one raw P-256 r‖s pair");
                Assert.That(SignedFrame.MeterReadingVerifiesWith(exchange.Response.Bytes, key), Is.True,
                            $"exchange {exchange.Index} ({exchange.Response.Message}): the recorded " +
                             "reading does not verify against the trace's own meter key");
            }
        });

    }


    /// <summary>
    /// The meter verification is a check and not a decoration: another key does not do, and neither
    /// does the right key over an altered reading.
    /// </summary>
    /// <remarks>
    /// The second half is the one worth having. A signature that verifies regardless of the number
    /// beside it would be the most convincing possible way to display a wrong reading, and a
    /// verification that reads its inputs from the wrong place — the recorder's memory rather than
    /// the frame — would behave exactly like that.
    /// </remarks>
    [Test]
    public void TheMeterVerificationActuallyBites()
    {

        var trace    = SessionTrace.ReadFrom(TracePath("iso2-ac-eim-meter"));
        var exchange = trace.Exchanges.First(e => e.Response.CarriesMeterSignature);

        using var key      = KeyOf(trace.MeterKey!);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Multiple(() =>
        {
            Assert.That(SignedFrame.MeterReadingVerifiesWith(exchange.Response.Bytes, key), Is.True,
                        "the recorded reading must verify against the recorded key");

            Assert.That(SignedFrame.MeterReadingVerifiesWith(exchange.Response.Bytes, otherKey), Is.False,
                        "any key must not do — that would make the verification decorative");

            // A CPO shaving the reading between meter and vehicle: the bytes still decode, the
            // signature is untouched, and the number no longer matches what was signed.
            var shaved = ShaveTheReading(exchange.Response.Bytes);
            Assert.That(shaved, Is.Not.EqualTo(exchange.Response.Bytes), "the tampering did nothing");
            Assert.That(SignedFrame.MeterReadingVerifiesWith(shaved, key), Is.False,
                        "an altered reading verified — the verification is not reading the number " +
                        "from the frame it is checking");
        });

    }


    /// <summary>
    /// No frame carries a meter signature *and* a header signature at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a limit written down as a running check rather than as a comment. In a Plug &amp; Charge
    /// session the EV echoes the station's <c>MeterInfo</c> back inside a <b>signed</b>
    /// <c>MeteringReceiptReq</c>, so the meter's 64 random bytes land inside the fragment the EV
    /// digests. Substituting the recorded meter signature would then leave the digest in
    /// <c>SignedInfo</c> describing a body that no longer exists, and the comparison would fail on
    /// the digest rather than on anything real.
    /// </para>
    /// <para>
    /// Solvable — verify each side's digest against its own body instead of substituting — and not
    /// solved here, because the metered scenarios are EIM and never reach that shape. If someone
    /// records a metered PnC session this fails immediately, which is the point: the alternative is
    /// a corpus that quietly compares nothing for exactly the session that motivated it.
    /// </para>
    /// </remarks>
    [Test]
    public void TheMeterSignatureIsNotInsideASignedFragment(
        [ValueSource(nameof(ScenarioNames))] string name)
    {

        var trace = SessionTrace.ReadFrom(TracePath(name));

        foreach (var exchange in trace.Exchanges)
            foreach (var frame in new[] { exchange.Request, exchange.Response })
                Assert.That(frame.IsSigned && frame.CarriesMeterSignature, Is.False,
                            $"exchange {exchange.Index} ({frame.Message}) carries both a header " +
                             "signature and a meter signature. The substitution cannot handle that " +
                             "yet: the meter's bytes are inside the fragment the header signature " +
                             "digests, so replacing them invalidates the digest.");

    }


    /// <summary>
    /// The station told its backend the same signed values it showed the car.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one question the OCPP record can settle without a CSMS.</b> Its energies cannot
    /// disagree with the wire's in a way that means anything — same station, same counter, so
    /// agreement there is arithmetic and not evidence. But the <em>signature</em> is a different
    /// matter: a station quietly reporting one figure for billing and showing another to the driver
    /// would produce two different signed values, and that is visible from the two records alone,
    /// with no key, no backend, and no trust in either party.
    /// </para>
    /// <para>
    /// Checked inside one recording rather than against the checked-in file, because ECDSA is
    /// randomised: the point is not which 64 bytes they are but that both records carry the
    /// <em>same</em> 64 bytes. A station that signed its reading twice — once for the wire, once for
    /// the backend — would produce two perfectly valid signatures and fail here, which is exactly
    /// what should happen.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheBackendRecordCarriesTheSameSignedValuesTheCarSaw(
        [Values("iso2-ac-eim-meter", "iso20-dc-eim-meter")] string name)
    {

        var (trace, ocpp) = await RecordWithBackendAsync(Scenarios.Single(s => s.Name == name));

        var onTheWire = trace.Exchanges
                             .Where(e => e.Response.CarriesMeterSignature)
                             .Select(e => e.Response.MeterSignature!)
                             .ToList();

        var reported = ocpp.MeterValues
                           .SelectMany(v => v.SampledValue)
                           .Select(v => v.SignedMeterValue?.SignedMeterData)
                           .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(onTheWire, Is.Not.Empty, "the metered session put no signature on the wire");
            Assert.That(reported, Is.EqualTo(onTheWire),
                        "the station reported different signed values to its backend than it showed "
                      + "the car — which is the whole reason to keep both records");

            Assert.That(ocpp.V2GSessionId,
                        Is.EqualTo(Convert.ToHexString(RecordedSessionId).ToLowerInvariant()),
                        "the transaction is not bound to the session, so nothing may be compared "
                      + "against it");

            Assert.That(ocpp.MeterValues[^1].SampledValue[0].Context, Is.EqualTo("Transaction.End"),
                        "the billed reading has to be identifiable as the final one");
        });

    }


    /// <summary>
    /// A station reports to its backend even when it shows the car nothing.
    /// </summary>
    /// <remarks>
    /// The ordinary case in the field and the one worth being able to look at: an EIM session at a
    /// station with no signing meter puts <c>MeterInfo</c> on the wire only when it wants a receipt,
    /// which for EIM is never — while the backend gets the full account and the driver is billed on
    /// it. A record reconstructed from the wire could not contain this, which is why the recorder is
    /// handed to the station rather than derived afterwards.
    /// </remarks>
    [Test]
    public async Task TheBackendIsToldWhatTheCarIsNot()
    {

        var (trace, ocpp) = await RecordWithBackendAsync(Scenarios.Single(s => s.Name == "iso2-ac-eim"));

        Assert.Multiple(() =>
        {
            Assert.That(trace.Exchanges.Any(e => e.Response.CarriesMeterSignature), Is.False,
                        "an EIM session at an unmetered station shows the car no signed reading");
            Assert.That(ocpp.MeterValues, Is.Not.Empty,
                        "…and the backend was told nothing either, so the record proves nothing");
            Assert.That(ocpp.MeterValues[^1].SampledValue[0].Value, Is.EqualTo("552"),
                        "the backend's final figure is the one the driver would be billed on");
        });

    }


    /// <summary>The checked-in transaction corpus still describes what these sessions produce.</summary>
    /// <remarks>
    /// Energies, contexts and the binding only. The signed values are randomised per recording and
    /// are checked by <see cref="TheBackendRecordCarriesTheSameSignedValuesTheCarSaw"/> instead —
    /// comparing them here would fail on every run for the one reason that is not a fault.
    /// </remarks>
    [Test]
    public async Task TheTransactionCorpusIsUnchanged([ValueSource(nameof(ScenarioNames))] string name)
    {

        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors", OcppFileName);
        Assert.That(File.Exists(path), Is.True, $"the transaction corpus is missing at {path}");

        var onDisk = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path))
                                                  .RootElement.GetProperty("transactions")
                                                  .GetProperty(name);

        var (_, fresh) = await RecordWithBackendAsync(Scenarios.Single(s => s.Name == name));

        Assert.Multiple(() =>
        {
            Assert.That(onDisk.GetProperty("v2gSessionId").GetString(), Is.EqualTo(fresh.V2GSessionId));
            Assert.That(onDisk.GetProperty("source").GetString(), Is.EqualTo("station-recording"),
                        "a corpus that claimed to come from a CSMS would overstate every verdict "
                      + "drawn from it");

            var values = onDisk.GetProperty("meterValues").EnumerateArray().ToList();
            Assert.That(values, Has.Count.EqualTo(fresh.MeterValues.Count), name);

            for (var i = 0; i < values.Count; i++)
            {
                var sampled = values[i].GetProperty("sampledValue")[0];
                Assert.That(sampled.GetProperty("value").GetString(),
                            Is.EqualTo(fresh.MeterValues[i].SampledValue[0].Value), $"{name} value {i}");
                Assert.That(sampled.GetProperty("context").GetString(),
                            Is.EqualTo(fresh.MeterValues[i].SampledValue[0].Context), $"{name} context {i}");
            }
        });

    }


    /// <summary>A recorded public key, on the curve the trace names. Contract keys are P-256 and a -20
    /// OEM provisioning key is P-521, so the curve cannot be assumed here any more.</summary>
    private static ECDsa KeyOf(TraceSigningKey key) =>
        ECDsa.Create(new ECParameters
        {
            Curve = key.CurveName == "P-521" ? ECCurve.NamedCurves.nistP521 : ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Convert.FromHexString(key.X),
                Y = Convert.FromHexString(key.Y),
            },
        });


    /// <summary>Re-encodes a -2 charge-loop response with 100 Wh taken off its meter reading, leaving
    /// the signature and everything else exactly as recorded.</summary>
    private static byte[] ShaveTheReading(byte[] frame)
    {

        if (!V2GTPDispatcher.TryDecode(frame, out var set, out var message, out var error))
            throw new InvalidDataException($"expected a -2 ChargingStatusRes: {error}");

        if (message is not V2G_Message m || m.Body.BodyElement is not ChargingStatusResType status)
            throw new InvalidDataException($"expected a -2 ChargingStatusRes, got {message?.GetType().Name}");

        var shaved = m with
        {
            Body = m.Body with
            {
                BodyElement = status with
                {
                    MeterInfo = status.MeterInfo! with
                    {
                        MeterReading = status.MeterInfo!.MeterReading!.Value - 100,
                    },
                },
            },
        };

        var payload = new byte[8192];
        if (!Iso2Codec.TryEncode(shaved, payload, out var n))
            throw new InvalidOperationException("re-encoding the shaved response failed");

        var result = new byte[V2GTPCodec.HeaderSize + n];
        if (!V2GTPDispatcher.TryEncode(set, payload.AsSpan(0, n), result, out var written))
            throw new InvalidOperationException("re-framing the shaved response failed");

        return result.AsSpan(0, written).ToArray();

    }


    /// <summary>
    /// The replay harness fails when it should. Without this the previous test's green could equally mean
    /// "the comparison never compares anything" — the failure mode a corpus check is most prone to, and
    /// the one that is invisible from a passing suite.
    /// </summary>
    [Test]
    public void AnAlteredRequestIsRejected()
    {

        var trace = SessionTrace.ReadFrom(TracePath("iso2-ac-eim"));

        // Flip one bit in the second request's EXI payload — the first is the SAP handshake, and the
        // point is to prove a *session* message is compared, not merely the opening frame.
        var tampered = trace.Exchanges[1].Request.Bytes;
        tampered[^1] ^= 0x01;

        var replay = new TraceReplayStream(trace);
        replay.Write(trace.Exchanges[0].Request.Bytes, 0, trace.Exchanges[0].Request.Bytes.Length);

        var mismatch = Assert.Throws<TraceMismatch>(() => replay.Write(tampered, 0, tampered.Length));

        Assert.Multiple(() =>
        {
            Assert.That(mismatch!.Message, Does.Contain("exchange 1"));
            Assert.That(mismatch.Message, Does.Contain("first difference at"));
        });

    }


    /// <summary>
    /// The signature-aware comparison is a check and not a hole.
    /// </summary>
    /// <remarks>
    /// The mechanism substitutes the recorded signature before comparing, which is exactly the shape
    /// of a comparison that accidentally accepts everything. So both halves are exercised against a
    /// real recorded frame: a body altered under a valid signature must fail the byte comparison, and
    /// a signature altered under a valid body must fail verification. If either passed, signed
    /// exchanges would be riding along unchecked while the suite stayed green.
    /// </remarks>
    [Test]
    public void TheSignatureAwareComparisonActuallyBites(
        [Values("iso2-ac-pnc", "iso20-dc-pnc",
                "iso2-ac-eim-certinstall", "iso2-ac-eim-certupdate", "iso20-dc-eim-certinstall")] string name)
    {

        var trace  = SessionTrace.ReadFrom(TracePath(name));
        var signed = trace.Exchanges.First(e => e.Request.IsSigned);

        var key = KeyOf(trace.SigningKey!);

        using (key)
        {
            Assert.Multiple(() =>
            {
                // The recorded frame itself: matches, and its signature verifies.
                Assert.That(SignedFrame.WithSignatureValue(signed.Request.Bytes, signed.Request.SignatureBytes!),
                            Is.EqualTo(signed.Request.Bytes),
                            "substituting a frame's own signature must be a no-op");
                Assert.That(SignedFrame.VerifiesWith(signed.Request.Bytes, key), Is.True,
                            "the recorded signature must verify against the recorded key");

                // A different key does not — on the same curve, so this fails because the key is wrong
                // and not because the parameters were.
                using var otherKey = ECDsa.Create(
                    trace.SigningKey!.CurveName == "P-521" ? ECCurve.NamedCurves.nistP521
                                                           : ECCurve.NamedCurves.nistP256);
                Assert.That(SignedFrame.VerifiesWith(signed.Request.Bytes, otherKey), Is.False,
                            "any key must not do — that would make the verification decorative");

                // A tampered signature survives the byte comparison (it is substituted away) and must
                // be caught by the verification. This is the case the whole design turns on.
                var tamperedSignature = signed.Request.SignatureBytes!;
                tamperedSignature[^1] ^= 0x01;
                var reSigned = SignedFrame.WithSignatureValue(signed.Request.Bytes, tamperedSignature);

                Assert.That(SignedFrame.WithSignatureValue(reSigned, signed.Request.SignatureBytes!),
                            Is.EqualTo(signed.Request.Bytes),
                            "the byte comparison cannot see a changed signature — that is the point");
                Assert.That(SignedFrame.VerifiesWith(reSigned, key), Is.False,
                            "…so the verification has to, or a wrong signature would pass both checks");
            });
        }

    }


    /// <summary>A session that ends early is the other way a replay can pass without meaning anything:
    /// nothing diverges, because nothing more is sent.</summary>
    [Test]
    public void AnEarlyEndingSessionIsNotComplete()
    {

        var trace  = SessionTrace.ReadFrom(TracePath("iso2-ac-eim"));
        var replay = new TraceReplayStream(trace);

        var sap = trace.Exchanges[0].Request.Bytes;
        replay.Write(sap, 0, sap.Length);

        Assert.That(replay.Complete, Is.False);
        Assert.That(replay.Replayed, Is.EqualTo(1));

    }

}
