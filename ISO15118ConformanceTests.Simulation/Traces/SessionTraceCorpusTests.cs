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
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using ISO15118ConformanceTests.Simulation.Timing;
using cloud.charging.open.protocols.ISO15118.Transport;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

// ChargingSession is a stop reason on both protocols and a different generated enum on each.
using I2ChargingSession  = cloud.charging.open.protocols.ISO15118_2.Generated.ChargingSession;
using C20ChargingSession = cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.ChargingSession;
using IPPort = org.GraphDefined.Vanaheimr.Hermod.IPPort;
using Ac20Msg = cloud.charging.open.protocols.ISO15118_20.AC.Generated;
using Dc20Msg = cloud.charging.open.protocols.ISO15118_20.DC.Generated;
using Common20Msg = cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;

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

    /// <summary>The id the refused resume is answered with. A second pinned value, because the station
    /// refuses by assigning an id that is <em>not</em> the one offered — so the recording seam that
    /// makes every other session reproducible cannot be handed the usual constant here.</summary>
    private static readonly byte[] RefusedSessionId = [0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20, 0x21];

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
    /// <remarks>
    /// <paramref name="scale"/> is the meter's fault switch (<see cref="SigningMeter.Scale"/>): 1 for every
    /// session but the one recorded to show a vehicle ending the charging over a meter that misreads.
    /// </remarks>
    private static SigningMeter Meter(TimeProvider clock, double scale = 1d) =>
        new("VAN*M*4711", MeterKeyPair(), clock) { Scale = scale };


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

        // ── bidirectional services, and the service filter ──────────────
        //
        // Recorded 2026-11-28 for the ports, as the Dynamic traces were: Evcc20Base.PreferBidirectionalService
        // ranks the BPT entry of the station's catalogue first, and selecting it is what makes the session
        // bidirectional. ChargeParameterDiscovery then carries the BPT_ subtype and every charge loop a
        // BPT_ control mode, Scheduled or Dynamic. A station enforces that coupling (EVerest refused a
        // charge-only request under service 9, 2026-08-05-everest-mcs-bpt), so a port that selects 5 or 6
        // and still sends the plain types fails against a real station. Four traces, because each pairs
        // one message set with one control mode, and each pair is its own arm in Evcc20Ac / Evcc20Dc.
        // The fifth holds the SupportedServiceIDs filter, which until now only the C# car could send.

        new("iso20-dc-eim-bpt", "iso15118-20", "dc",
            "-20 DC, EIM, bidirectional: the EV ranks DC_BPT (6) ahead of DC (2), so the session " +
            "selects 6 and sends BPT_DC_CPDReqEnergyTransferMode and BPT_Scheduled_DC_CLReqControlMode.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage) { PreferBidirectionalService = true }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        new("iso20-dc-eim-bpt-dynamic", "iso15118-20", "dc",
            "-20 DC, EIM, bidirectional in Dynamic control mode: service 6 and " +
            "BPT_Dynamic_DC_CLReqControlMode, whose discharge limits are mandatory.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage)
                          { PreferBidirectionalService = true, PreferDynamicControlMode = true }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        new("iso20-ac-eim-bpt", "iso15118-20", "ac",
            "-20 AC, EIM, bidirectional: the EV ranks AC_BPT (5) ahead of AC (1), so the session " +
            "selects 5 and sends BPT_AC_CPDReqEnergyTransferMode and BPT_Scheduled_AC_CLReqControlMode.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Evcc20Ac(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage) { PreferBidirectionalService = true }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Secc20Ac(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        new("iso20-ac-eim-bpt-dynamic", "iso15118-20", "ac",
            "-20 AC, EIM, bidirectional in Dynamic control mode: service 5 and " +
            "BPT_Dynamic_AC_CLReqControlMode, whose discharge limits are mandatory.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Evcc20Ac(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage)
                          { PreferBidirectionalService = true, PreferDynamicControlMode = true }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Secc20Ac(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        new("iso20-dc-eim-filter", "iso15118-20", "dc",
            "-20 DC, EIM, with the SupportedServiceIDs filter: the ServiceDiscoveryReq names services 2 " +
            "and 6 (Table 38 of [V2G20-1248]). Our station still answers with its whole catalogue, so " +
            "the rest of the session is iso20-dc-eim's.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage) { SupportedServiceIds = [2, 6] }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        // ── energy that flows back ────────────────────────────────────────
        //
        // Recorded 2026-11-28: the first sessions of this project in which energy goes from the vehicle
        // to the station. Both are bidirectional and Dynamic, because there the station sets the operating
        // point. Negative is the direction from the vehicle ([V2G20-1034], [V2G20-1035]). Both stations
        // have a signing meter, whose discharge register rides in BPT_DischargedEnergyReadingWh, and
        // in a bidirectional session the meter signs both registers (layout 2 of MeterSigningPayload).
        // DC: the station serves the current, so EVSEPresentCurrent goes out negative and the car counts
        // its own voltage times it. AC: the station asks with a negative EVSETargetActivePower, the car
        // follows from the next iteration on ([V2G20-1825]), and the station meters what the car reports.

        new("iso20-dc-eim-bpt-discharge", "iso15118-20", "dc",
            "-20 DC, EIM, bidirectional in Dynamic mode, at a station that discharges the vehicle at 100 A: " +
            "EVSEPresentCurrent is negative in every charge loop, the meter's discharge register rises, " +
            "and each reading is signed over both registers.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage)
                          { PreferBidirectionalService = true, PreferDynamicControlMode = true }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId     = RecordedSessionId,
                            FixedGenChallenge  = RecordedGenChallenge,
                            InstalledMeter     = Meter(clock),
                            DischargeCurrent_A = 100,
                            Backend            = backend }.RunAsync(stream, ct);
            },
            Metered: true),

        new("iso20-ac-eim-bpt-discharge", "iso15118-20", "ac",
            "-20 AC, EIM, bidirectional in Dynamic mode, at a station that asks for 11 kW back: " +
            "EVSETargetActivePower is negative, the car reports that figure as its EVPresentActivePower " +
            "from the second charge loop on, and the meter's discharge register rises with it.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Evcc20Ac(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage)
                          { PreferBidirectionalService = true, PreferDynamicControlMode = true }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Ac);
                await new Secc20Ac(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            InstalledMeter    = Meter(clock),
                            DischargePowerW   = 11_000,
                            Backend           = backend }.RunAsync(stream, ct);
            },
            Metered: true),

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

        // The vehicle's verdict on the meter, recorded 2026-11-28. The same station, but its meter counts
        // 10 % too much and signs what it misread, so the signature holds and only the car's own count
        // can catch it. The car is set to 5 % (never under 10 Wh, the command line's default floor) and
        // ends the charging after the first reading, which already shows 880 Wh against its 800. It keeps
        // to everything else the standard asks, and gives its reason in the one place ISO 15118-20 has
        // for one: EVTerminationCode and EVTerminationExplanation in the SessionStopReq. No code is
        // defined for that yet, so the URN is ours (MeteringDeviation.TerminationCode).
        new("iso20-dc-eim-meter-deviation", "iso15118-20", "dc",
            "-20 DC, EIM, at a station whose meter counts 10 % too much and signs what it misread. The " +
            "car compares every reading with its own count and, set to 5 %, ends the charging after the " +
            "first one, 880 Wh against 800. Its SessionStopReq says why: EVTerminationCode " +
            "urn:openchargingcloud:EVTerminationCode:MeteringDeviation, and the four figures in " +
            "EVTerminationExplanation. The first recorded message in which a vehicle tells the station " +
            "its meter is wrong.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage)
                          { StopOnMeteringDeviation = MeteringDeviationLimit.Parse("5") }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            InstalledMeter    = Meter(clock, scale: 1.10),
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

        // The -2 twin of iso20-dc-pnc-meterconfirm, recorded 2026-11-27: a Contract session at a station
        // with a signing meter. Its ChargingStatusRes asks for a receipt (ReceiptRequired, [V2G2-408])
        // and carries a MeterInfo whose SigMeterReading the meter signed. The car answers with a
        // MeteringReceiptReq that echoes that MeterInfo, SigMeterReading included, signed with its
        // contract key ([V2G2-903]). So the receipt is signed by two parties, the meter inside and the
        // car around it, and the meter's random bytes sit in the fragment the car digested.
        // SignedFrame.WithSignatureFrom and DigestHolds handle that, as they do for the -20 message.
        new("iso2-ac-pnc-meter", "iso15118-2", "ac",
            "AC, Plug & Charge, at a station with a signing meter: the ChargingStatusRes that asks for a " +
            "receipt carries a MeterInfo with the meter's SigMeterReading, and the car's signed " +
            "MeteringReceiptReq echoes it, meter signature included. The -2 message signed by two " +
            "parties, beside -20's MeteringConfirmationReq.",
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
                            InstalledMeter    = Meter(clock),
                            Backend           = backend }.RunAsync(stream, ct);
            },
            SignedBy: PncMaterial.Key,
            Metered:  true),

        // The receipt a station asks for with nothing new to sign, recorded 2026-11-28. After the first
        // receipt, Secc2.DemandReceiptWithoutMeterInfo makes the next ChargingStatusRes ask again with no
        // MeterInfo, the shape EVerest 2026.10.0's one-shot meter_info_is_used flag produces.
        // [V2G2-408] still requires the receipt, so the car signs the last MeterInfo it received (stack
        // 8e6fd3e). Before that fix the car skipped it; the ports had the same gap until this trace.
        //
        // No signing meter: the station sends a MeterInfo only with a receipt request, so the last record
        // is the first receipt's. Recorded without one because, until stack 1eedd64, an installed meter
        // put a MeterInfo into the switch's second request too. The metered shape is
        // iso2-ac-pnc-meter-norecord, below.
        new("iso2-ac-pnc-norecord", "iso15118-2", "ac",
            "AC, Plug & Charge, and a second receipt asked for without a MeterInfo: the car answers it " +
            "with a MeteringReceiptReq over the last record the station sent, as EVerest 2026.10.0's " +
            "one-shot meter_info_is_used flag requires of a car.",
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
                          { FixedSessionId                = RecordedSessionId,
                            FixedGenChallenge             = RecordedGenChallenge,
                            DemandReceiptWithoutMeterInfo = true,
                            Backend                       = backend }.RunAsync(stream, ct);
            },
            SignedBy: PncMaterial.Key),

        // The EVerest shape itself, recorded 2026-11-28: a signing meter, whose reading the station
        // reports in every ChargingStatusRes, and a receipt request that still carries none (stack
        // 1eedd64; before it, the meter filled that request too). The car signs the last reading it
        // received, so its second MeteringReceiptReq carries that reading's SigMeterReading and is
        // signed by two parties, like the first.
        new("iso2-ac-pnc-meter-norecord", "iso15118-2", "ac",
            "AC, Plug & Charge, at a station with a signing meter that asks for a second receipt without " +
            "a MeterInfo, as EVerest 2026.10.0 does: the car signs the last reading it was shown, the " +
            "meter's SigMeterReading included.",
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
                          { FixedSessionId                = RecordedSessionId,
                            FixedGenChallenge             = RecordedGenChallenge,
                            InstalledMeter                = Meter(clock),
                            DemandReceiptWithoutMeterInfo = true,
                            Backend                       = backend }.RunAsync(stream, ct);
            },
            SignedBy: PncMaterial.Key,
            Metered:  true),

        new("iso20-dc-pnc", "iso15118-20", "dc",
            "-20 DC, Plug & Charge: the AuthorizationReq echoes the station's GenChallenge and " +
            "carries the contract chain, signed over the PnC_AReqAuthorizationMode fragment.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                using var contractKey = PncMaterial.Key();
                // -20's own suite, ecdsa-sha512, which the C#, Kotlin and Swift cars all sign by default.
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

        // The meter's signature and the car's, in one message, and the shape this corpus said it could
        // not compare until it was recorded (2026-11-27). The station asks with
        // EVSENotification = MeteringConfirmation ([V2G20-1083]) from its second charge-loop response
        // on; the car signs the reading it was just shown with its contract key ([V2G20-1808]) and sends
        // it on the side stream, payload type 0x8102 ([V2G20-1084]), which is answered on the same type
        // ([V2G20-1085]). Two of the session's frames are therefore not on any of the four message-set
        // payload types every other recording uses.
        //
        // The station is strict here (RequireValidMeteringConfirmation): a confirmation that did not
        // bind the right session, reading, control mode, digest and key would be answered FAILED and end
        // the recording, so a trace that exists is one the station checked.
        new("iso20-dc-pnc-meterconfirm", "iso15118-20", "dc",
            "-20 DC, Plug & Charge, at a station with a signing meter that asks for metering " +
            "confirmations: from the second DC_ChargeLoopRes on, EVSENotification is " +
            "MeteringConfirmation, and after each the car sends a MeteringConfirmationReq on payload " +
            "type 0x8102 — the reading it was shown, meter signature included, signed with its " +
            "contract key. The only recorded message signed by two parties.",
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
                          { FixedSessionId                   = RecordedSessionId,
                            FixedGenChallenge                = RecordedGenChallenge,
                            InstalledMeter                   = Meter(clock),
                            MeteringConfirmationEvery        = 1,
                            RequireValidMeteringConfirmation = true,
                            Backend                          = backend }.RunAsync(stream, ct);
            },
            SignedBy: PncMaterial.Key,
            Metered:  true),

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

        // ── pause and resume ──────────────────────────────────────────────
        //
        // Five recordings for one feature, because "resume" is two protocols doing genuinely
        // different things under one name, and because the interesting half is what a resumed session
        // does NOT send.
        //
        // -2 ([V2G2-740]) rejoins by putting the paused id in the opening SessionSetupReq header and
        // then repeats everything: discovery, payment selection, authorization, charge parameters.
        // The only other difference is on the wire in one field — [V2G2-743] reduces EAmount by what
        // the paused session already charged.
        //
        // -20 rejoins the same way and then skips the whole opening block: no AuthorizationSetup, no
        // AuthorizationReq, no ServiceDiscovery/Detail/Selection. It opens at
        // ChargeParameterDiscovery. Our own two sides were wrong in the same direction here until
        // 2026-08-08 — the EVCC replayed authorization and the SECC accepted it, so they agreed with
        // each other and disagreed with EVerest, which answered FAILED_SequenceError.
        //
        // And the fifth: a -20 resume the station refuses, because the binding cannot be verified.
        // That is what this harness's own plain-TCP interop runs actually get, and it is where the
        // security property lives — a port that failed open there would pass all four others.

        new("iso2-ac-eim-pause", "iso15118-2", "ac",
            "AC, external payment, ended with SessionStopReq(Pause) rather than Terminate. The " +
            "station keeps the session id instead of forgetting it, and the car may come back to it " +
            "([V2G2-740]). Identical to iso2-ac-eim in every frame but the last request.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Evcc2(stream, PowerMode.Ac, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage)
                          { StopMode = I2ChargingSession.Pause }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Secc2(PowerMode.Ac, SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        new("iso2-ac-eim-resume", "iso15118-2", "ac",
            "The connection after that pause: the SessionSetupReq header carries the paused " +
            "session's id instead of the all-zero one, and the station answers OK_OldSessionJoined " +
            "([V2G2-740]). Everything else runs again — a -2 resume repeats discovery, payment " +
            "selection and authorization — so the second visible difference is the only other one: " +
            "EAmount asks for 22 kWh less the " + PausedSessionChargedWh + " Wh the paused session " +
            "already took ([V2G2-743]).",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Evcc2(stream, PowerMode.Ac, clock, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage)
                          {
                              ResumeSessionId  = RecordedSessionId,
                              AlreadyChargedWh = PausedSessionChargedWh,
                          }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_2, ct, PowerMode.Ac);
                await new Secc2(PowerMode.Ac, SeccSequenceTimeout, clock)
                          { FixedSessionId    = RecordedSessionId,
                            FixedGenChallenge = RecordedGenChallenge,
                            ResumeSessionId   = RecordedSessionId,
                            Backend           = backend }.RunAsync(stream, ct);
            }),

        new("iso20-dc-eim-pause", "iso15118-20", "dc",
            "-20 DC, ended with SessionStopReq(Pause). The station keeps both the session id and the " +
            "binding that says which vehicle owns it; the pair is what the next connection is offered.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
                          {
                              StopMode            = C20ChargingSession.Pause,
                              SeccLeafCertificate = ResumeMaterial.SeccLeaf,
                          }.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                await new Secc20Dc(SeccSequenceTimeout, clock)
                          { FixedSessionId         = RecordedSessionId,
                            FixedGenChallenge      = RecordedGenChallenge,
                            VehicleLeafCertificate = ResumeMaterial.VehicleLeaf,
                            Backend                = backend }.RunAsync(stream, ct);
            }),

        new("iso20-dc-eim-resume", "iso15118-20", "dc",
            "The connection after that pause, and the shortest complete -20 session in the corpus: " +
            "SessionSetup answers OK_OldSessionJoined and charging begins at " +
            "DC_ChargeParameterDiscovery. No AuthorizationSetup, no AuthorizationReq, no service " +
            "discovery, detail or selection — a resumed session repeats none of it, and the energy " +
            "service it settled on travels with the session rather than being negotiated again.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                var evcc = new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
                {
                    SeccLeafCertificate = ResumeMaterial.SeccLeaf,
                };
                // DC = service 2 (Table 204): what the paused session had settled on, carried over
                // rather than renegotiated.
                evcc.ResumeFrom(new ResumableSession(RecordedSessionId,
                                    SessionBinding20.Compute(RecordedSessionId, ResumeMaterial.SeccLeaf), 2));
                await evcc.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                var secc = new Secc20Dc(SeccSequenceTimeout, clock)
                {
                    FixedSessionId         = RecordedSessionId,
                    FixedGenChallenge      = RecordedGenChallenge,
                    VehicleLeafCertificate = ResumeMaterial.VehicleLeaf,
                    Backend                = backend,
                };
                secc.OfferResume(new ResumableSession(RecordedSessionId,
                                     SessionBinding20.Compute(RecordedSessionId, ResumeMaterial.VehicleLeaf), 2));
                await secc.RunAsync(stream, ct);
            }),

        new("iso20-dc-eim-resume-refused", "iso15118-20", "dc",
            "The same request, refused. The car offers the paused session's id and the station has " +
            "no binding to check it against — which is what plain TCP always gives, since the " +
            "binding is taken from the TLS handshake. The answer is a NEW session with a different " +
            "id and OK_NewSessionEstablished, indistinguishable from a first visit: a rejection that " +
            "said why would tell an attacker its guessed id was real. So the car drops everything " +
            "the pause carried and runs the full opening sequence.",
            async (stream, clock, ct) =>
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                var evcc = new Evcc20Dc(stream, clock, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
                // Offered without a binding on either side — exactly the shape of a plain-TCP resume.
                evcc.ResumeFrom(new ResumableSession(RecordedSessionId, Binding: null, EnergyServiceId: 2));
                await evcc.RunAsync(ct);
            },
            async (stream, clock, backend, ct) =>
            {
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, ct, PowerMode.Dc);
                var secc = new Secc20Dc(SeccSequenceTimeout, clock)
                {
                    // A *different* pinned id, and it has to be different: the station refuses by
                    // handing out an id that is not the one offered, so pinning it to that very id
                    // would ask for the one value the refusal may not take.
                    FixedSessionId         = RefusedSessionId,
                    FixedGenChallenge      = RecordedGenChallenge,
                    VehicleLeafCertificate = ResumeMaterial.VehicleLeaf,
                    Backend                = backend,
                };
                secc.OfferResume(new ResumableSession(RecordedSessionId, Binding: null, EnergyServiceId: 2));
                await secc.RunAsync(stream, ct);
            }),
    ];


    /// <summary>
    /// What <c>iso2-ac-eim-pause</c> metered, and therefore what its successor may no longer ask for.
    /// </summary>
    /// <remarks>
    /// A constant rather than a value threaded from one recording into the next, because the two are
    /// recorded independently and a scenario cannot see its predecessor. It is not left to drift:
    /// <see cref="TheResumedSessionAsksForWhatThePausedOneLeft"/> reads the paused session's own
    /// transaction record and fails here if the two ever disagree.
    /// </remarks>
    private const double PausedSessionChargedWh = 552;


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

    /// <summary>
    /// Writes the stand-in TLS leaves the recorded -20 resume is bound to. Explicit like its siblings,
    /// and for a sharper reason: these bytes are hashed into a session binding, so changing them
    /// invalidates the recorded resume without changing anything a reader would notice.
    /// </summary>
    [Test, Explicit("Regenerates Vectors/Session.resume-material.json — invalidates the -20 resume trace")]
    public void RegenerateTheResumeMaterial()
    {
        TestContext.Out.WriteLine($"wrote {ResumeMaterial.Regenerate()}");
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
                                     IPAddress.Loopback.ToString(), IPPort.Parse(listener.LocalEndpoint.Port), ct: cts.Token);

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

                // A frame signed over a reading somebody else signed: Restore takes the recorded
                // digest along with the recorded reading, so the digests are checked here instead,
                // each against its own body, and the fresh signature against the corpus key.
                if (old.IsSigned && old.CarriesMeterSignature)
                {
                    using var key = KeyOf(onDisk.SigningKey!);
                    Assert.Multiple(() =>
                    {
                        Assert.That(SignedFrame.DigestHolds(fresh.Bytes), Is.True,
                                    $"exchange {i} {direction}: the fresh digest is not over its own body");
                        Assert.That(SignedFrame.DigestHolds(old.Bytes), Is.True,
                                    $"exchange {i} {direction}: the recorded digest is not over its own body");
                        Assert.That(SignedFrame.VerifiesWith(fresh.Bytes, key), Is.True,
                                    $"exchange {i} {direction}: the fresh signature does not verify");
                    });
                }

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

        // Both at once: the meter's signature is inside what the car digested, so the recorded digest
        // belongs with the recorded reading and the whole Signature element is taken — see
        // SignedFrame.WithSignatureFrom. The caller holds each side's digest to its own body.
        if (old.IsSigned && old.CarriesMeterSignature)
            return SignedFrame.WithSignatureFrom(SignedFrame.WithMeterSignature(bytes, old.MeterSignatureBytes!),
                                                 old.Bytes);

        // Order does not matter: the two live in different fields — one in the message header, one
        // in the body's MeterInfo — and each substitution decodes and re-encodes the whole frame.
        if (old.SignatureBytes      is { } signature) bytes = SignedFrame.WithSignatureValue(bytes, signature);
        if (old.MeterSignatureBytes is { } reading)   bytes = SignedFrame.WithMeterSignature(bytes, reading);

        return bytes;
    }


    /// <summary>
    /// A resumed session names the session it is resuming — which is the one thing about a pause and
    /// its successor that lives <em>between</em> two recordings rather than inside either.
    /// </summary>
    /// <remarks>
    /// Read out of the paused trace rather than compared against the pinned constant, deliberately.
    /// The constant is what the recorder was told; what the pair has to agree on is what actually
    /// went on the wire, and a check against the constant would pass even if the two recordings had
    /// drifted apart from each other.
    /// </remarks>
    [Test]
    public void AResumedSessionCarriesThePausedSessionsId(
        [Values("iso2-ac-eim", "iso20-dc-eim")] string family)
    {

        var paused  = SessionTrace.ReadFrom(TracePath($"{family}-pause"));
        var resumed = SessionTrace.ReadFrom(TracePath($"{family}-resume"));

        // The id the paused session ran under: the station assigns it in SessionSetupRes, and every
        // later request carries it — so the request *after* SessionSetup is where to read it.
        var pausedId = SessionIdOfRequest(paused, index: 2);

        Assert.Multiple(() =>
        {
            Assert.That(paused.Exchanges[^1].Request.Message, Does.Contain("SessionStop"),
                        "a paused session must end with a SessionStopReq, or it did not pause");

            // The opening SessionSetupReq of a fresh session carries the all-zero id; a resume puts
            // the paused id there instead. That single field is the whole of [V2G2-740] on the wire.
            Assert.That(SessionIdOfRequest(resumed, index: 1), Is.EqualTo(pausedId),
                        "the resumed session's SessionSetupReq does not name the session it resumes");
            Assert.That(SessionIdOfRequest(paused, index: 1), Is.Not.EqualTo(pausedId),
                        "the paused session opened with the id it was later given, so this corpus " +
                        "cannot tell a resume from a first visit");
        });

    }

    /// <summary>The SessionID in a recorded request's header, whichever protocol it belongs to.</summary>
    private static string SessionIdOfRequest(SessionTrace trace, int index)
        => DecodedRequest(trace, index) switch
        {
            V2G_Message m
                => Convert.ToHexString(m.Header.SessionID).ToLowerInvariant(),
            cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.V2GRequestType r
                => Convert.ToHexString(r.Header.SessionID).ToLowerInvariant(),
            var other => throw new InvalidDataException(
                             $"{trace.Name} exchange {index}: a {other?.GetType().Name} carries no SessionID."),
        };

    private static object? DecodedRequest(SessionTrace trace, int index)
    {
        if (!V2GTPDispatcher.TryDecode(trace.Exchanges[index].Request.Bytes, out _, out var message, out var error))
            throw new InvalidDataException($"{trace.Name} exchange {index}: {error}");
        return message;
    }


    /// <summary>
    /// A resumed -2 session asks for what the paused one left, and not for the whole 22 kWh again
    /// ([V2G2-743]).
    /// </summary>
    /// <remarks>
    /// The number comes out of the paused session's <em>own</em> transaction record rather than out of
    /// <see cref="PausedSessionChargedWh"/>, so the constant the recorder was configured with is held
    /// to the recording it produced rather than being trusted. Two files that were regenerated apart
    /// fail here.
    /// </remarks>
    [Test]
    public void TheResumedSessionAsksForWhatThePausedOneLeft()
    {

        var charged = OcppEnergyOf("iso2-ac-eim-pause");

        Assert.That(charged, Is.EqualTo(PausedSessionChargedWh).Within(1.0),
                    "the paused recording metered a different amount than the resumed one was told " +
                    "to subtract — one of the two was regenerated without the other");

        var resumed = SessionTrace.ReadFrom(TracePath("iso2-ac-eim-resume"));
        // From 1: exchange 0 is the SupportedAppProtocol handshake, which belongs to a different
        // message set and is not a V2G_Message at all.
        var request = Enumerable.Range(1, resumed.Exchanges.Count - 1)
                                .Select(i => DecodedRequest(resumed, i))
                                .OfType<V2G_Message>()
                                .Select(m => m.Body.BodyElement)
                                .OfType<ChargeParameterDiscoveryReqType>()
                                .Single();

        var eAmount = ((AC_EVChargeParameterType) request.EVChargeParameter!).EAmount;

        Assert.That(eAmount.Value * Math.Pow(10, eAmount.Multiplier),
                    Is.EqualTo(22_000 - charged).Within(1.0),
                    "[V2G2-743]: a resumed session's EAmount must be the remainder, not the literal");

    }

    /// <summary>The energy a recorded session's station reported to its backend.</summary>
    private static double OcppEnergyOf(string name)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors", OcppFileName);
        var root = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement;

        var values = root.GetProperty("transactions").GetProperty(name).GetProperty("meterValues");
        Assert.That(values.GetArrayLength(), Is.GreaterThan(0), $"{name}: the station metered nothing");

        var last = values[values.GetArrayLength() - 1].GetProperty("sampledValue")[0].GetProperty("value");
        return double.Parse(last.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
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

            Assert.That(messages, Has.Some.Matches<string>(m => m is not null && m.StartsWith("SessionSetupReq")));
            Assert.That(messages, Has.Some.Matches<string>(m => m is not null && m.StartsWith("SessionStopReq")),
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
            // The scenario's flag rather than the name's suffix, for the reason `signs` above stopped
            // reading the name: the metering-confirmation session is metered and is not a "-meter".
            var isMetered = Scenarios.Single(s => s.Name == name).Metered;
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
    /// <remarks>
    /// Both directions: in the metering-confirmation session the car echoes each reading back inside
    /// its MeteringConfirmationReq, and that copy has to verify as well as the one it was shown.
    /// </remarks>
    [Test]
    public void EveryRecordedMeterReadingVerifies(
        [Values("iso2-ac-eim-meter", "iso2-ac-pnc-meter", "iso2-ac-pnc-meter-norecord", "iso20-dc-eim-meter", "iso20-dc-eim-meter-deviation", "iso20-dc-pnc-meterconfirm",
                "iso20-dc-eim-bpt-discharge", "iso20-ac-eim-bpt-discharge")] string name)
    {

        var trace   = SessionTrace.ReadFrom(TracePath(name));
        var metered = trace.Exchanges.SelectMany(e => new[] { (e.Index, Frame: e.Request), (e.Index, Frame: e.Response) })
                                     .Where(f => f.Frame.CarriesMeterSignature)
                                     .ToList();

        Assert.That(trace.Exchanges.Any(e => e.Response.CarriesMeterSignature), Is.True,
                    "no meter reading in a trace recorded with a meter fitted");

        using var key = KeyOf(trace.MeterKey!);

        Assert.Multiple(() =>
        {
            foreach (var (index, frame) in metered)
            {
                Assert.That(frame.MeterSignatureBytes, Has.Length.EqualTo(64),
                            $"exchange {index}: the field holds 64 bytes — one raw P-256 r‖s pair");
                Assert.That(SignedFrame.MeterReadingVerifiesWith(frame.Bytes, key), Is.True,
                            $"exchange {index} ({frame.Message}): the recorded " +
                             "reading does not verify against the trace's own meter key");
            }
        });

    }


    /// <summary>
    /// The metering-confirmation session confirms the readings it was shown: one MeteringConfirmationReq
    /// after every response that asked, on the side stream, carrying that response's reading unchanged,
    /// signed with the contract key over a digest of its own body.
    /// </summary>
    /// <remarks>
    /// Read out of the recording rather than out of the state machines' counters, because the claim is
    /// about what crossed the wire. The tampering half is the one worth having: a car that signed a
    /// reading other than the one it was shown must not produce a frame whose digest still holds.
    /// </remarks>
    [Test]
    public void TheCarConfirmsTheReadingsItWasShown()
    {

        var trace = SessionTrace.ReadFrom(TracePath("iso20-dc-pnc-meterconfirm"));

        var confirmations = trace.Exchanges.Where(e => e.Request.Message == "MeteringConfirmationReq").ToList();

        Assert.That(confirmations, Has.Count.GreaterThanOrEqualTo(2),
                    "the station asks from its second charge-loop response on, and the car charges for three");

        using var contractKey = KeyOf(trace.SigningKey!);
        using var meterKey    = KeyOf(trace.MeterKey!);

        Assert.Multiple(() =>
        {
            foreach (var confirmation in confirmations)
            {
                var asked = trace.Exchanges[confirmation.Index - 1].Response;

                Assert.That(confirmation.Request.PayloadType,  Is.EqualTo("0x8102"), $"exchange {confirmation.Index}: [V2G20-1084]");
                Assert.That(confirmation.Response.PayloadType, Is.EqualTo("0x8102"), $"exchange {confirmation.Index}: [V2G20-1085]");
                Assert.That(confirmation.Response.Message,     Is.EqualTo("MeteringConfirmationRes"));
                Assert.That(asked.Message,                     Is.EqualTo("DC_ChargeLoopRes"),
                            $"exchange {confirmation.Index}: a confirmation must follow the response that asked for it");

                Assert.That(confirmation.Request.MeterSignature, Is.EqualTo(asked.MeterSignature),
                            $"exchange {confirmation.Index}: the car confirmed a reading other than the one it was shown");

                Assert.That(SignedFrame.VerifiesWith(confirmation.Request.Bytes, contractKey), Is.True,
                            $"exchange {confirmation.Index}: the confirmation does not verify against the contract key");
                Assert.That(SignedFrame.MeterReadingVerifiesWith(confirmation.Request.Bytes, meterKey), Is.True,
                            $"exchange {confirmation.Index}: the reading inside the confirmation does not verify");
            }

            // A different reading under the same signature: the digest no longer describes the body.
            var first     = confirmations[0].Request;
            var otherRead = first.MeterSignatureBytes!;
            otherRead[0] ^= 0x01;
            var tampered  = SignedFrame.WithMeterSignature(first.Bytes, otherRead);

            Assert.That(SignedFrame.DigestHolds(tampered), Is.False,
                        "a confirmation whose reading was changed after signing still has a matching digest");
            Assert.That(SignedFrame.VerifiesWith(tampered, contractKey), Is.False,
                        "…and still verifies, so the verification is not looking at the body at all");

            using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Assert.That(SignedFrame.VerifiesWith(first.Bytes, otherKey), Is.False,
                        "any key must not do — that would make the verification decorative");
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
    /// Only the two metering messages a car signs over a reading, the -20 <c>MeteringConfirmationReq</c>
    /// and the -2 <c>MeteringReceiptReq</c>, carry a meter signature <i>and</i> a header signature.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A limit written down as a running check rather than as a comment. When the car signs a message
    /// that carries the station's <c>MeterInfo</c>, the meter's 64 random bytes land inside the fragment
    /// the car digests. Substituting the recorded meter signature then leaves the digest in
    /// <c>SignedInfo</c> describing a body that no longer exists.
    /// </para>
    /// <para>
    /// Solved for both since 2026-11-27: the comparison takes the recorded <c>Signature</c> element
    /// whole and holds each side's digest to its own body (<see cref="SignedFrame.WithSignatureFrom"/>,
    /// <see cref="SignedFrame.DigestHolds"/>). The -20 message came first. The -2 one followed when
    /// <c>iso2-ac-pnc-meter</c> was recorded. Any third message of this shape fails here, rather than
    /// letting the comparison quietly check nothing for it.
    /// </para>
    /// </remarks>
    [Test]
    public void OnlyTheMeteringMessagesAreSignedOverAMeterSignature(
        [ValueSource(nameof(ScenarioNames))] string name)
    {

        var trace = SessionTrace.ReadFrom(TracePath(name));

        foreach (var exchange in trace.Exchanges)
            foreach (var frame in new[] { exchange.Request, exchange.Response })
                if (frame.IsSigned && frame.CarriesMeterSignature)
                    Assert.That(frame.Message, Is.AnyOf("MeteringConfirmationReq", "MeteringReceiptReqType"),
                                $"exchange {exchange.Index} ({frame.Message}) carries both a header " +
                                 "signature and a meter signature, and only the -20 MeteringConfirmationReq " +
                                 "and the -2 MeteringReceiptReq have the digest-aware comparison. Model this " +
                                 "message in SignedFrame first.");

    }


    /// <summary>
    /// The -2 receipt signs the reading it was shown: the car's <c>MeteringReceiptReq</c> echoes the
    /// <c>MeterInfo</c> of the response that asked for it, the meter's <c>SigMeterReading</c> unchanged,
    /// and is signed with the contract key over a digest of its own body.
    /// </summary>
    /// <remarks>
    /// The -2 twin of <see cref="TheCarConfirmsTheReadingsItWasShown"/>, read out of the recording for
    /// the same reason. And with the same tampering half: a receipt whose reading was swapped after
    /// signing must not keep a digest that holds.
    /// </remarks>
    [Test]
    public void TheCarsReceiptSignsTheReadingItWasShown()
    {

        var trace    = SessionTrace.ReadFrom(TracePath("iso2-ac-pnc-meter"));
        var receipts = trace.Exchanges.Where(e => e.Request.Message == "MeteringReceiptReqType").ToList();

        Assert.That(receipts, Has.Count.GreaterThanOrEqualTo(1),
                    "a Contract session at our station is asked for a receipt once");

        using var contractKey = KeyOf(trace.SigningKey!);
        using var meterKey    = KeyOf(trace.MeterKey!);

        Assert.Multiple(() =>
        {
            foreach (var receipt in receipts)
            {
                var asked = trace.Exchanges[receipt.Index - 1].Response;

                Assert.That(asked.Message, Is.EqualTo("ChargingStatusResType"),
                            $"exchange {receipt.Index}: a receipt must follow the response that asked for it ([V2G2-408])");
                Assert.That(asked.CarriesMeterSignature, Is.True,
                            $"exchange {receipt.Index}: the response asking for the receipt carries no signed reading");
                Assert.That(receipt.Request.IsSigned, Is.True, $"exchange {receipt.Index}: [V2G2-903]");
                Assert.That(receipt.Request.MeterSignature, Is.EqualTo(asked.MeterSignature),
                            $"exchange {receipt.Index}: the car signed a reading other than the one it was shown");

                Assert.That(SignedFrame.VerifiesWith(receipt.Request.Bytes, contractKey), Is.True,
                            $"exchange {receipt.Index}: the receipt does not verify against the contract key");
                Assert.That(SignedFrame.MeterReadingVerifiesWith(receipt.Request.Bytes, meterKey), Is.True,
                            $"exchange {receipt.Index}: the reading inside the receipt does not verify");
            }

            var first     = receipts[0].Request;
            var otherRead = first.MeterSignatureBytes!;
            otherRead[0] ^= 0x01;
            var tampered  = SignedFrame.WithMeterSignature(first.Bytes, otherRead);

            Assert.That(SignedFrame.DigestHolds(first.Bytes), Is.True, "the recorded digest is over the recorded body");
            Assert.That(SignedFrame.DigestHolds(tampered), Is.False,
                        "a receipt whose reading was changed after signing still has a matching digest");
            Assert.That(SignedFrame.VerifiesWith(tampered, contractKey), Is.False,
                        "…and still verifies, so the verification is not looking at the body at all");
        });

    }

    /// <summary>
    /// A receipt asked for without a <c>MeterInfo</c> is still answered, over the last record the
    /// station sent.
    /// </summary>
    /// <remarks>
    /// `[V2G2-408]` makes the receipt unconditional after <c>ReceiptRequired = true</c>, and EVerest
    /// 2026.10.0 asks without a fresh record. Before stack <c>8e6fd3e</c> the C# car skipped that receipt,
    /// and the station answered the next <c>PowerDeliveryReq</c> <c>FAILED_SequenceError</c>. This is the
    /// first recording of the case, so it holds the C# car and both ports to it.
    /// </remarks>
    [Test]
    public void AReceiptAskedForWithoutARecordSignsTheLastOne()
    {

        var trace    = SessionTrace.ReadFrom(TracePath("iso2-ac-pnc-norecord"));
        var receipts = trace.Exchanges.Where(e => e.Request.Message == "MeteringReceiptReqType").ToList();

        Assert.That(receipts, Has.Count.EqualTo(2), "one receipt over a fresh record, one without");

        using var contractKey = KeyOf(trace.SigningKey!);

        static T Body<T>(byte[] frame) where T : BodyBaseType
            => (T) ((V2G_Message) SignedFrame.DecodeWithSet(frame).Message).Body.BodyElement!;

        var (withRecord, withoutRecord) = (receipts[0], receipts[1]);
        var askedFirst = Body<ChargingStatusResType>(trace.Exchanges[withRecord.Index    - 1].Response.Bytes);
        var askedAgain = Body<ChargingStatusResType>(trace.Exchanges[withoutRecord.Index - 1].Response.Bytes);
        var signedOver = Body<MeteringReceiptReqType>(withoutRecord.Request.Bytes).MeterInfo;

        Assert.Multiple(() =>
        {
            Assert.That(askedFirst.ReceiptRequired, Is.True);
            Assert.That(askedFirst.MeterInfo,       Is.Not.Null, "the first receipt is asked for with a record");
            Assert.That(askedAgain.ReceiptRequired, Is.True,     "[V2G2-408]: the second request is a receipt request");
            Assert.That(askedAgain.MeterInfo,       Is.Null,
                        "the second receipt must be asked for without a record, or this trace shows nothing new");

            // The last record the station sent is the one it asked the first receipt over.
            Assert.That(signedOver.MeterID,      Is.EqualTo(askedFirst.MeterInfo!.MeterID));
            Assert.That(signedOver.MeterReading, Is.EqualTo(askedFirst.MeterInfo.MeterReading),
                        "the second receipt does not sign the last record the station sent");
            Assert.That(signedOver.TMeter,       Is.EqualTo(askedFirst.MeterInfo.TMeter));

            foreach (var receipt in receipts)
                Assert.That(SignedFrame.VerifiesWith(receipt.Request.Bytes, contractKey), Is.True,
                            $"exchange {receipt.Index}: the receipt does not verify against the contract key");

            Assert.That(trace.Exchanges[^1].Request.Message, Is.EqualTo("SessionStopReqType"),
                        "the session ran to SessionStop rather than ending at a refused PowerDeliveryReq");
        });

    }

    /// <summary>
    /// At a metered station, a receipt asked for without a <c>MeterInfo</c> signs the last reading the
    /// station reported, the meter's signature included.
    /// </summary>
    /// <remarks>
    /// The EVerest 2026.10.0 shape, which <see cref="AReceiptAskedForWithoutARecordSignsTheLastOne"/>
    /// covers only without a meter: here the station reports a signed reading in every status
    /// response, and the bare request is the exception. So the second receipt carries a meter
    /// signature of its own, and two parties must still verify over it.
    /// </remarks>
    [Test]
    public void AMeteredStationsBareReceiptRequestSignsTheLastReading()
    {

        var trace    = SessionTrace.ReadFrom(TracePath("iso2-ac-pnc-meter-norecord"));
        var receipts = trace.Exchanges.Where(e => e.Request.Message == "MeteringReceiptReqType").ToList();

        Assert.That(receipts, Has.Count.EqualTo(2), "one receipt over a fresh reading, one without");

        using var contractKey = KeyOf(trace.SigningKey!);
        using var meterKey    = KeyOf(trace.MeterKey!);

        static T Body<T>(byte[] frame) where T : BodyBaseType
            => (T) ((V2G_Message) SignedFrame.DecodeWithSet(frame).Message).Body.BodyElement!;

        var (withRecord, withoutRecord) = (receipts[0], receipts[1]);
        var askedFirst  = Body<ChargingStatusResType>(trace.Exchanges[withRecord.Index    - 1].Response.Bytes);
        var askedAgain  = Body<ChargingStatusResType>(trace.Exchanges[withoutRecord.Index - 1].Response.Bytes);
        var lastReading = trace.Exchanges.Take(withoutRecord.Index).Last(e => e.Response.CarriesMeterSignature).Response;

        var unasked = trace.Exchanges.Where(e => e.Response.Message == "ChargingStatusResType"
                                              && Body<ChargingStatusResType>(e.Response.Bytes).ReceiptRequired != true)
                                     .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(askedFirst.MeterInfo?.SigMeterReading, Is.Not.Null,
                        "the first receipt is asked for with a signed reading");
            Assert.That(askedAgain.ReceiptRequired, Is.True, "[V2G2-408]: the second request is a receipt request");
            Assert.That(askedAgain.MeterInfo,       Is.Null,
                        "the second receipt must be asked for without a reading, or this trace shows nothing new");
            Assert.That(unasked, Is.Not.Empty.And.All.Matches<TraceExchange>(e => e.Response.CarriesMeterSignature),
                        "a metered station reports its reading in every status response that asks for no receipt");

            Assert.That(withoutRecord.Request.MeterSignature, Is.EqualTo(lastReading.MeterSignature),
                        "the second receipt does not sign the last reading the station reported");

            foreach (var receipt in receipts)
            {
                Assert.That(SignedFrame.VerifiesWith(receipt.Request.Bytes, contractKey), Is.True,
                            $"exchange {receipt.Index}: the receipt does not verify against the contract key");
                Assert.That(SignedFrame.MeterReadingVerifiesWith(receipt.Request.Bytes, meterKey), Is.True,
                            $"exchange {receipt.Index}: the reading inside the receipt does not verify");
            }

            Assert.That(trace.Exchanges[^1].Request.Message, Is.EqualTo("SessionStopReqType"),
                        "the session ran to SessionStop rather than ending at a refused PowerDeliveryReq");
        });

    }

    /// <summary>
    /// The station discharges the vehicle, and the meter signs what went back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first recordings of this project in which energy flows from the vehicle to the station.
    /// Negative is that direction (`[V2G20-1034]`, `[V2G20-1035]`). DC: the station serves the current,
    /// so every <c>EVSEPresentCurrent</c> is negative. AC: the station asks with a negative
    /// <c>EVSETargetActivePower</c>, and the vehicle reports that figure from the next charge loop on
    /// (`[V2G20-1825]`).
    /// </para>
    /// <para>
    /// The meter's discharge register rises, rides in <c>BPT_DischargedEnergyReadingWh</c>, and is signed
    /// with the charged one (layout 2 of <see cref="MeterSigningPayload"/>). The half worth having is the
    /// negative: the recorded signature must not verify with the register stripped, or the discharged
    /// figure would be an unsigned number that looks signed.
    /// </para>
    /// </remarks>
    [Test]
    public void TheStationDischargesAndTheMeterSignsWhatWentBack(
        [Values("iso20-dc-eim-bpt-discharge", "iso20-ac-eim-bpt-discharge")] string name)
    {

        var trace = SessionTrace.ReadFrom(TracePath(name));
        using var meterKey = KeyOf(trace.MeterKey!);

        static double Amount(short value, sbyte exponent) => value * Math.Pow(10, exponent);

        var loops = trace.Exchanges.Where(e => e.Response.Message is "DC_ChargeLoopRes" or "AC_ChargeLoopRes").ToList();
        Assert.That(loops, Has.Count.EqualTo(3), "three charge loops, as in every -20 session of the corpus");

        var discharged = new List<ulong>();

        Assert.Multiple(() =>
        {
            for (var i = 0; i < loops.Count; i++)
            {
                var (request, response) = (SignedFrame.DecodeWithSet(loops[i].Request.Bytes).Message,
                                           SignedFrame.DecodeWithSet(loops[i].Response.Bytes).Message);

                (string MeterID, ulong Charged, ulong? Discharged, byte[]? Signature, ulong? Timestamp, byte[] Session) reading;

                if (response is Dc20Msg.DC_ChargeLoopRes dc)
                {
                    Assert.That(Amount(dc.EVSEPresentCurrent.Value, dc.EVSEPresentCurrent.Exponent), Is.Negative,
                                $"loop {i + 1}: the station serves a positive current, so nothing went back");
                    var m = dc.MeterInfo!;
                    reading = (m.MeterID, m.ChargedEnergyReadingWh, m.BPT_DischargedEnergyReadingWh, m.MeterSignature,
                               m.MeterTimestamp, dc.Header.SessionID);
                }
                else
                {
                    var ac  = (Ac20Msg.AC_ChargeLoopRes) response;
                    var ask = (Ac20Msg.Dynamic_AC_CLResControlModeType) ac.CLResControlMode;
                    Assert.That(Amount(ask.EVSETargetActivePower.Value, ask.EVSETargetActivePower.Exponent), Is.Negative,
                                $"loop {i + 1}: the station does not ask for energy back");

                    // The vehicle follows from the loop after the first ask on.
                    var said = ((Ac20Msg.Dynamic_AC_CLReqControlModeType) ((Ac20Msg.AC_ChargeLoopReq) request).CLReqControlMode)
                                   .EVPresentActivePower;
                    if (i > 0)
                        Assert.That(Amount(said.Value, said.Exponent), Is.EqualTo(Amount(ask.EVSETargetActivePower.Value,
                                                                                          ask.EVSETargetActivePower.Exponent)),
                                    $"loop {i + 1}: the vehicle did not follow the station's ask ([V2G20-1825])");

                    var m = ac.MeterInfo!;
                    reading = (m.MeterID, m.ChargedEnergyReadingWh, m.BPT_DischargedEnergyReadingWh, m.MeterSignature,
                               m.MeterTimestamp, ac.Header.SessionID);
                }

                Assert.That(reading.Discharged, Is.Not.Null,
                            $"loop {i + 1}: a bidirectional session's reading carries no discharge register");
                discharged.Add(reading.Discharged ?? 0);

                Assert.That(SigningMeter.Verify(meterKey, 20, reading.Session, reading.MeterID, reading.Charged,
                                                reading.Discharged, (long?) reading.Timestamp, reading.Signature!),
                            Is.True, $"loop {i + 1}: the reading does not verify over both registers");
                Assert.That(SigningMeter.Verify(meterKey, 20, reading.Session, reading.MeterID, reading.Charged,
                                                (long?) reading.Timestamp, reading.Signature!),
                            Is.False, $"loop {i + 1}: the reading verifies with the discharge register stripped");
            }

            Assert.That(discharged, Is.Ordered.Ascending, "the discharge register went backwards");
            Assert.That(discharged[^1], Is.GreaterThan(0), "nothing was discharged");
        });

    }

    /// <summary>
    /// The recorded vehicle that found the meter wrong ends the charging after that reading, and its
    /// <c>SessionStopReq</c> says why. No other recorded -20 session gives a reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The explanation is pinned to the byte. A recorded session holds the vehicle's bytes and the Kotlin
    /// and Swift cars replay them, so this text is the cross-language contract, rounding included
    /// (<see cref="MeteringDeviation.Explain"/>). Its first figure is checked against the reading the
    /// station actually sent; the vehicle's own count is not on the wire, so the 800 Wh is what one
    /// 48 kW iteration is worth (<see cref="ChargeLoopSample"/>).
    /// </para>
    /// <para>
    /// Still confirmed and still conformant: nothing in the session but its length and its last request
    /// differs from <c>iso20-dc-eim-meter</c>. Both end with <c>Terminate</c>; only one says why.
    /// </para>
    /// </remarks>
    [Test]
    public void TheVehicleThatStoppedOverTheMeterSaysSoInItsSessionStopReq()
    {

        var trace = SessionTrace.ReadFrom(TracePath("iso20-dc-eim-meter-deviation"));
        var loops = trace.Exchanges.Where(e => e.Request.Message == "DC_ChargeLoopReq").ToList();
        var stop  = (Common20Msg.SessionStopReq) SignedFrame.DecodeWithSet(
                        trace.Exchanges.Single(e => e.Request.Message == "SessionStopReq").Request.Bytes).Message;
        var shown = ((Dc20Msg.DC_ChargeLoopRes) SignedFrame.DecodeWithSet(loops[^1].Response.Bytes).Message).MeterInfo!;

        Assert.Multiple(() => {

            Assert.That(loops,                         Has.Count.EqualTo(1), "stopped after the first reading");
            Assert.That(shown.ChargedEnergyReadingWh,  Is.EqualTo(880), "800 Wh delivered, read 10 % high");
            Assert.That(stop.ChargingSession,          Is.EqualTo(C20ChargingSession.Terminate));
            Assert.That(stop.EVTerminationCode,        Is.EqualTo(MeteringDeviation.TerminationCode));
            Assert.That(stop.EVTerminationExplanation, Is.EqualTo("meter 880 Wh, vehicle 800 Wh, difference 80 Wh, beyond 44 Wh"));
            Assert.That(stop.EVTerminationExplanation, Does.StartWith($"meter {shown.ChargedEnergyReadingWh} Wh, "));

            foreach (var other in Scenarios.Where(s => s.Protocol == "iso15118-20" && s.Name != "iso20-dc-eim-meter-deviation"))
            {
                var otherTrace = SessionTrace.ReadFrom(TracePath(other.Name));
                foreach (var exchange in otherTrace.Exchanges.Where(e => e.Request.Message == "SessionStopReq"))
                {
                    var req = (Common20Msg.SessionStopReq) SignedFrame.DecodeWithSet(exchange.Request.Bytes).Message;
                    Assert.That(req.EVTerminationCode is null && req.EVTerminationExplanation is null, Is.True,
                                $"{other.Name}: a session that did not stop over the meter gave a reason");
                }
            }

        });

    }

    /// <summary>
    /// Every discharge limit on the wire is negative, the vehicle's and the station's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `[V2G20-1213]` sets every parameter used for discharging negative, limits included, and negative
    /// is the direction from the vehicle (`[V2G20-1035]`). Until 2026-11-28 our cars and stations sent
    /// these limits as positive magnitudes. EVerest 2026.10.0 sends them negative on both sides
    /// (<c>docs/interop-runs/2026-11-28-everest-2026.10.0-discharge-sign/</c>).
    /// </para>
    /// <para>
    /// Walked by reflection over every decoded frame, so a message type added later is covered without
    /// being named here: any <c>…Maximum…Discharge…</c> power or current must be below zero, and any
    /// <c>…Minimum…Discharge…</c> one at or below. The register <c>BPT_DischargedEnergyReadingWh</c> is the
    /// one value that stays positive (`[V2G20-1415]`), and it is no limit, so the walk leaves it alone.
    /// </para>
    /// </remarks>
    [Test]
    public void EveryDischargeLimitOnTheWireIsNegative(
        [Values("iso20-dc-eim-bpt", "iso20-dc-eim-bpt-dynamic", "iso20-ac-eim-bpt", "iso20-ac-eim-bpt-dynamic",
                "iso20-dc-eim-bpt-discharge", "iso20-ac-eim-bpt-discharge")] string name)
    {

        var trace    = SessionTrace.ReadFrom(TracePath(name));
        var limits   = new List<(int Exchange, string Path, double Value)>();

        static double Amount(object rational)
        {
            var type = rational.GetType();
            var v    = Convert.ToDouble(type.GetProperty("Value")!.GetValue(rational));
            var e    = Convert.ToDouble(type.GetProperty("Exponent")!.GetValue(rational));
            return v * Math.Pow(10, e);
        }

        void Walk(object? node, string path, int exchange, int depth)
        {
            if (node is null || depth > 8) return;
            var type = node.GetType();
            if (type.IsPrimitive || node is string || node is byte[] || type.IsEnum) return;

            foreach (var property in type.GetProperties())
            {
                if (property.GetIndexParameters().Length > 0) continue;
                var value = property.GetValue(node);
                if (value is null) continue;

                var isRational = value.GetType().Name == "RationalNumberType";
                if (isRational && property.Name.Contains("Discharge") &&
                    (property.Name.Contains("Power") || property.Name.Contains("Current")))
                    limits.Add((exchange, $"{path}.{property.Name}", Amount(value)));
                else if (!isRational && value is not System.Collections.IEnumerable)
                    Walk(value, $"{path}.{property.Name}", exchange, depth + 1);
            }
        }

        foreach (var exchange in trace.Exchanges)
            foreach (var frame in new[] { exchange.Request, exchange.Response })
                if (frame.Message.Contains("ChargeParameterDiscovery") || frame.Message.Contains("ChargeLoop"))
                    Walk(SignedFrame.DecodeWithSet(frame.Bytes).Message, frame.Message, exchange.Index, 0);

        Assert.Multiple(() =>
        {
            Assert.That(limits.Select(l => l.Path), Has.Some.Contains("Req").And.Some.Contains("Res"),
                        "the walk found discharge limits on one side only");

            foreach (var (exchange, path, value) in limits)
                if (path.Contains("Maximum"))
                    Assert.That(value, Is.Negative, $"exchange {exchange}: {path} = {value}");
                else
                    Assert.That(value, Is.LessThanOrEqualTo(0), $"exchange {exchange}: {path} = {value}");
        });

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
        [Values("iso2-ac-eim-meter", "iso2-ac-pnc-meter", "iso20-dc-eim-meter", "iso20-dc-eim-meter-deviation", "iso20-dc-pnc-meterconfirm",
                "iso20-dc-eim-bpt-discharge", "iso20-ac-eim-bpt-discharge")] string name)
    {

        var (trace, ocpp) = await RecordWithBackendAsync(Scenarios.Single(s => s.Name == name));

        var onTheWire = trace.Exchanges
                             .Where(e => e.Response.CarriesMeterSignature)
                             .Select(e => e.Response.MeterSignature!)
                             .ToList();

        // One signature per reading, on the import register; a bidirectional session's export register
        // rides beside it unsigned, because the one signature covers both (V2G-METER-2).
        var reported = ocpp.MeterValues
                           .SelectMany(v => v.SampledValue)
                           .Where(v => v.Measurand == "Energy.Active.Import.Register")
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
    /// The bare receipt request withholds one reading from the car, and only from the car.
    /// </summary>
    /// <remarks>
    /// The one metered scenario <see cref="TheBackendRecordCarriesTheSameSignedValuesTheCarSaw"/> cannot
    /// hold to equality. The station books every iteration with its backend, the one whose response
    /// asks for a receipt without a <c>MeterInfo</c> included, so the backend holds one signed reading
    /// more than the car was shown. That is what EVerest's shape means in the field: a reading that is
    /// billed, but that the car never saw and so never signed a receipt over. Every other reading must
    /// still be the same 64 bytes on both sides.
    /// </remarks>
    [Test]
    public async Task TheBareReceiptRequestWithholdsOneReadingFromTheCarOnly()
    {

        var (trace, ocpp) = await RecordWithBackendAsync(Scenarios.Single(s => s.Name == "iso2-ac-pnc-meter-norecord"));

        var loop      = trace.Exchanges.Where(e => e.Response.Message == "ChargingStatusResType").ToList();
        var bare      = loop.FindIndex(e => !e.Response.CarriesMeterSignature);
        var onTheWire = loop.Where(e => e.Response.CarriesMeterSignature).Select(e => e.Response.MeterSignature!).ToList();

        var reported  = ocpp.MeterValues
                            .SelectMany(v => v.SampledValue)
                            .Select(v => v.SignedMeterValue?.SignedMeterData)
                            .ToList();

        Assert.That(bare, Is.Not.Negative, "no status response went without a reading");

        var withheld  = reported[bare];
        var shown     = reported.Where((_, i) => i != bare).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(loop.Count(e => !e.Response.CarriesMeterSignature), Is.EqualTo(1),
                        "exactly one status response carries no reading");
            Assert.That(reported, Has.Count.EqualTo(onTheWire.Count + 1),
                        "the backend holds the shown readings and the withheld one, nothing else");
            Assert.That(shown,    Is.EqualTo(onTheWire),
                        "apart from the withheld one, the backend got different signed values than the car saw");
            Assert.That(withheld,  Is.Not.Null, "the withheld reading is signed like the others");
            Assert.That(onTheWire, Does.Not.Contain(withheld), "the car was shown the withheld reading after all");
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
        [Values("iso2-ac-pnc", "iso20-dc-pnc", "iso20-dc-pnc-meterconfirm",
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
