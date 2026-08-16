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

using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Simulation;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.Timing;

namespace ISO15118ConformanceTests.Simulation.Interop;

/// <summary>
/// Our two state machines, selected by protocol and power mode, on a stream somebody else is at the other
/// end of.
/// </summary>
/// <remarks>
/// Shared by every counterparty fixture. A per-counterparty copy would be four copies of the same switch,
/// and the interesting difference between the fixtures is who is on the wire, not how we drive our own
/// side of it.
/// <para>
/// Real delays throughout (<see cref="TaskAsyncDelay"/>), unlike the loopback tests: a peer's timeouts are
/// real, and a poll loop that runs as fast as the CPU allows is a different session from the one the
/// specification describes.
/// </para>
/// </remarks>
internal static class InteropSession
{

    /// <summary>Generous on purpose: a live peer under a debugger, or a container's first message after a
    /// cold start, is slower than anything a loopback ever sees.</summary>
    public static readonly TimeSpan PerMessageTimeout = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan SequenceTimeout   = TimeSpan.FromSeconds(60);


    /// <summary>How a session actually ran: the exchange count, and the authorization mode it really
    /// used. The second field exists because a Plug &amp; Charge run that quietly falls back to EIM —
    /// because the station did not offer Contract, or sent no challenge — completes just as happily and
    /// would otherwise be reported as a PnC result.
    /// <para>
    /// <see cref="SelectedEnergyServiceId"/> is there for the same reason, one negotiation earlier: an
    /// <c>Evcc20Mcs</c> that finds no MCS service in the catalogue takes the DC one <i>by design</i>, so a
    /// run reported only as "completed" cannot tell an MCS result from a megawatt truck charging at an
    /// ordinary DC post. <c>null</c> for -2, which has no service catalogue to select from.
    /// </para></summary>
    /// <param name="MeterInfoResponses">-20 only: how many charge-loop responses carried a
    /// <c>MeterInfo</c> element. Reported separately from <paramref name="MeteringReceiptsSent"/>, which
    /// is the -2 mechanism: a run that asked under `[V2G20-1081]` and got nothing back is a finding about
    /// the station, and it is invisible in an exchange count.</param>
    /// <param name="Provisioning">-2 only, and null unless the run asked for a contract: what the
    /// station's <c>CertificateInstallationRes</c>/<c>CertificateUpdateRes</c> actually delivered.
    /// Separate from <paramref name="AuthorizationMode"/> for the reason that field exists — a
    /// provisioning run whose station never offered the certificate service completes as an ordinary
    /// EIM or PnC session and would otherwise be filed as a provisioning result.</param>
    public sealed record EvccOutcome(Int32 Exchanges, String AuthorizationMode, Int32 MeteringReceiptsSent,
                                     UInt16? SelectedEnergyServiceId = null, Int32 MeterInfoResponses = 0,
                                     TimeSpan? SilenceEndedAfter = null,
                                     ProvisioningOutcome? Provisioning = null,
                                     Int32 Renegotiations = 0,
                                     // Why the charge loop ended, when a battery decided it rather than
                                     // the fixed three iterations. Null on every run that configured none,
                                     // which is every recorded one.
                                     ChargeStop? BatteryStop = null,
                                     Double? DeliveredWh = null);


    /// <summary>
    /// What an ISO 15118-2 contract-provisioning exchange produced.
    /// </summary>
    /// <remarks>
    /// Every field is <b>reported and none asserted</b>, deliberately: this run exists to find out what a
    /// station does, and a station that answers with a refusal, an unverifiable signature or an
    /// undecryptable key is the result rather than a broken run. <paramref name="Offered"/> is the one
    /// that decides whether the rest means anything — a station that never advertised the service in its
    /// <c>ServiceList</c> was never asked.
    /// </remarks>
    public sealed record ProvisioningOutcome(String Action, Boolean Offered, Boolean SignatureOk,
                                             String? Emaid, String? ContractSubject, Boolean KeyRecovered);


    /// <summary>
    /// The same, from the station's side of the wire — and needed for a sharper reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A reverse run used to come back as one <see cref="Boolean"/>, and a run that can only report
    /// <i>whether</i> it finished cannot report <b>what</b> finished. Two facts of the 2026-08-06 MCS
    /// reverse session were on our station and nowhere else: which entry of our catalogue their EV picked
    /// — the whole point of that direction, and invisible on the wire because MCS is DC's message set —
    /// and whether the signed <c>AuthorizationReq</c> their car sent actually verified. Neither is in the
    /// counterparty's logs, because the session was not their station's. Both were read off the CLI's
    /// console because the fixture had nowhere to put them; see that run's finding 2.
    /// </para>
    /// <para>
    /// <see cref="SelectedEnergyServiceId"/> is <c>null</c> for -2, which has no service catalogue, and
    /// <see cref="PlugAndCharge"/> is <c>null</c> whenever the EV authorized by EIM — which is itself
    /// worth distinguishing from a contract that failed to verify.
    /// </para>
    /// <para>
    /// <see cref="SequenceErrorAt"/> exists because <see cref="IsDone"/> stopped being able to tell the two
    /// endings apart. Our -2 station used to <i>throw</i> at an out-of-sequence request, so a refused session
    /// arrived here as a failed test; it now answers <c>FAILED_SequenceError</c> and ends the session the way
    /// the standard asks — which is correct on the wire and, left unreported, would turn the loudest possible
    /// finding into a green tick. It names the refused message, or is <c>null</c> for a session that ended
    /// the normal way. -20 has no equivalent yet: its guard still throws.
    /// </para>
    /// </remarks>
    /// <param name="EvControlModeIsDynamic">-20 only: which control mode the peer's car actually ran, read
    /// off its <c>ScheduleExchangeReq</c>. Null for -2, and for a session that ended before that message.
    /// <see cref="InteropEnvironment.PreferDynamic"/> only decides what our station offers <i>first</i>;
    /// both modes are always advertised, so this is the half that says what the car did with the offer.</param>
    /// <param name="PlugAndChargeIso2">The `-2` twin of <paramref name="PlugAndCharge"/>: what
    /// <c>Secc2</c> made of a signed <c>AuthorizationReq</c> — challenge echo, reference digest, ECDSA
    /// signature, the <c>SignedInfo</c> grammar it verified under, and how the contract chain fared.</param>
    /// <param name="MeteringReceipts">One verdict per signed `-2` <c>MeteringReceiptReq</c>. A PnC car
    /// owes these (<c>[V2G2-903]</c>) and our station verifies each; an empty list in a Contract session
    /// means none arrived, which is a different statement from "they did not verify".</param>
    public sealed record SeccOutcome(Boolean IsDone, UInt16? SelectedEnergyServiceId = null,
                                     PnCAuthResult? PlugAndCharge = null, String? SequenceErrorAt = null,
                                     Boolean? EvControlModeIsDynamic = null,
                                     Iso2PnCResult? PlugAndChargeIso2 = null,
                                     IReadOnlyList<Iso2ReceiptResult>? MeteringReceipts = null);


    /// <param name="preferDynamic">-20 only: drive the session in Dynamic control mode (ControlMode = 2)
    /// rather than Scheduled — the EV states energy needs and a departure time and lets the station steer.
    /// Ignored for -2, which has no control modes. Set by <c>V2G_INTEROP_DYNAMIC=1</c>.</param>
    /// <param name="pnc">Contract credentials; when set <i>and</i> the station offers Contract/PnC, the
    /// session authorizes with a signed AuthorizationReq instead of EIM. Set by
    /// <c>V2G_INTEROP_CONTRACT_CERT</c>.</param>
    /// <param name="mcs">Drive the -20 DC session as <b>MCS</b>: ask for energy-transfer services 8 / 9
    /// instead of 2 / 6. Set by <c>V2G_INTEROP_MODE=mcs</c>; see <see cref="InteropEnvironment.Mcs"/>.</param>
    /// <param name="bptFirst">Rank the bidirectional entry of this run's catalogue ahead of the
    /// unidirectional one — AC_BPT (5), DC_BPT (6) or MCS_BPT (9) — which is the only way to reach it at a
    /// station advertising both. Set by <c>V2G_INTEROP_BPT_FIRST=1</c>.
    /// <para>
    /// It applies to all three catalogues, which it did not use to: while this was <c>mcsBptFirst</c> it
    /// was refused without <paramref name="mcs"/>, so services 5 and 6 were unreachable from here and
    /// EVerest's BPT column stayed empty while their SIL advertised it. The generalisation is the app's
    /// <c>Evcc20Base.PreferBidirectionalService</c>.
    /// </para></param>
    /// <param name="requestMeterInfo">-20 only: set <c>MeterInfoRequested</c> in every charge-loop
    /// request, which `[V2G20-1081]` makes the EV's way of asking and `[V2G20-1082]` makes the station's
    /// duty to answer. Set by <c>V2G_INTEROP_METER=1</c>. Off by default, so every earlier run and every
    /// vector keeps the field <c>false</c> it was recorded with.</param>
    /// <param name="silentInChargeLoop">-20 only: after one charge-loop iteration, stop sending and hold
    /// the connection open for this long, to measure when the station ends the session by itself. Set by
    /// <c>V2G_INTEROP_SILENT=&lt;seconds&gt;</c>. A run that sets it does not charge — it measures
    /// <c>V2G_SECC_Sequence_Timeout</c>, which nothing here could do before.
    /// <para><b>-2 refuses it</b> rather than ignoring it, as of 2026-08-15: our `-2` car has no such
    /// instrument, so a run that asked for one used to charge normally and measure nobody's timer.</para></param>
    /// <param name="sendSessionId">Both protocols: the SessionID our car puts in every request after
    /// <c>SessionSetup</c>, so a station's `[V2G2-460]` / `[V2G20-460]` duty to answer
    /// <c>FAILED_UnknownSession</c> is reachable at all. Set by
    /// <c>V2G_INTEROP_SESSIONID=&lt;hex|zero&gt;</c>; <c>null</c> sends the id the station issued.
    /// <para>
    /// <b>The `-2` half was dropped here until 2026-08-15.</b> <c>Evcc2.SendSessionId</c> and its `-20`
    /// twin were both built on 2026-08-11, and this method wired only the `-20` one — so every `-2`
    /// caller's value went nowhere, which is why the EVerest `-2` measurement of that same day had to be
    /// taken with a raw Python probe instead of this fixture. A silently ignored value here produces a
    /// <i>complete, successful session</i>, which is exactly what a station that ignores the rule also
    /// produces.
    /// </para></param>
    /// <param name="supportedServiceIds">-20 only: the <c>SupportedServiceIDs</c> filter our EV puts in
    /// <c>ServiceDiscoveryReq</c>. Null omits the element, which asks the station for everything and is
    /// what every recorded session does. Set by <c>V2G_INTEROP_SERVICE_IDS=2,6</c>; see
    /// <c>Evcc20Base.SupportedServiceIds</c> for why it exists.</param>
    /// <param name="ongoingTimeout">Both protocols: how long to keep polling a phase that answers
    /// <c>EVSEProcessing = Ongoing</c>, overriding the 60 s default. Set by
    /// <c>V2G_INTEROP_ONGOING=&lt;seconds&gt;</c>, and needed to reach a station's own timer at all —
    /// see <see cref="InteropEnvironment.OngoingTimeout"/> for the three that are longer than 60 s.</param>
    public static async Task<EvccOutcome> RunEvccAsync(Stream stream, ProtocolVariant protocol, PowerMode mode,
                                                       CancellationToken ct, Boolean preferDynamic = false,
                                                       PncEvccOptions? pnc = null, Boolean mcs = false,
                                                       Boolean bptFirst = false, Boolean requestMeterInfo = false,
                                                       TimeSpan? silentInChargeLoop = null,
                                                       Byte[]? sendSessionId = null,
                                                       IReadOnlyList<UInt16>? supportedServiceIds = null,
                                                       Iso2CertInstallOptions? certificateProvisioning = null,
                                                       Boolean renegotiate = false,
                                                       TimeSpan? ongoingTimeout = null)
    {

        if (mcs)
            RefuseImpossibleMcs(protocol, mode);

        if (protocol == ProtocolVariant.Iso15118_2)
        {
            // -2 has no service catalogue: its energy transfer mode is chosen in ChargeParameterDiscovery
            // and there is no bidirectional variant to rank. Refused rather than dropped, for the reason
            // the MCS guard below has — a run configured for BPT that quietly ran unidirectionally is a
            // result that will be written up as something it is not.
            if (bptFirst)
                throw new ArgumentException(
                    "BPT was ranked first for an ISO 15118-2 session; the bidirectional services are -20 "
                  + "catalogue entries and -2 has no catalogue.", nameof(bptFirst));

            // The same rule as bptFirst above, applied to the two knobs that reach `Evcc20Base` and have no
            // -2 counterpart at all: `MeterInfoRequested` is a -20 charge-loop field, and going silent needs
            // `GoSilentInChargeLoop`, which only the -20 car has. Both were *dropped* here until 2026-08-15 —
            // so a -2 run that set V2G_INTEROP_SILENT charged normally and measured nobody's timer, which is
            // the eighth instance this month of a value read from the environment and lost one hop short.
            if (requestMeterInfo)
                throw new ArgumentException(
                    "MeterInfo was requested for an ISO 15118-2 session; `[V2G20-1081]`'s MeterInfoRequested "
                  + "is a -20 charge-loop field and -2 has no way to ask.", nameof(requestMeterInfo));

            if (silentInChargeLoop is not null)
                throw new ArgumentException(
                    "A silent charge loop was asked of an ISO 15118-2 session; the instrument is "
                  + "`Evcc20Base.GoSilentInChargeLoop` and our -2 car has no twin of it.", nameof(silentInChargeLoop));

            if (supportedServiceIds is not null)
                throw new ArgumentException(
                    "A service-id filter was given for an ISO 15118-2 session; SupportedServiceIDs is a -20 "
                  + "ServiceDiscoveryReq element and -2 has no service catalogue at all.", nameof(supportedServiceIds));

            var evcc = new Evcc2(stream, mode, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout)
                           {
                               Pnc                = pnc,
                               CertInstallRequest = certificateProvisioning,
                               Renegotiate        = renegotiate,

                               // The pre-fix car on demand, so a run can show the difference is the
                               // sequence and not the station. Never set outside a control arm.
                               RenegotiationSkipsIsolationSequence
                                                  = InteropEnvironment.RenegotiationSkipsIsolation(),

                               // The open arm of the 2026-08-15 re-run: does their supply ramp down once
                               // the car stops claiming it is ready? Off unless a run asks for it.
                               IsolationDeclaresNotReady
                                                  = InteropEnvironment.IsolationDeclaresNotReady(),


                               // `Evcc2.SendSessionId` has existed since 2026-08-11 and this line had not:
                               // every -2 caller passing sendSessionId had it discarded here, so the one
                               // stack whose `[V2G2-460]` behaviour was measured that day had to be probed
                               // with raw Python. Wired 2026-08-15.
                               SendSessionId      = sendSessionId,

                               // A charge loop that ends when the car is done rather than after three
                               // iterations, and a real interval between them. Both null unless a run asks:
                               // every recorded interop session was taken at three iterations, 50 ms apart,
                               // and this must not change that by existing.
                               Battery            = InteropEnvironment.Battery(),
                               ChargeLoopInterval = InteropEnvironment.ChargeLoopInterval()
                           };

            if (ongoingTimeout is { } iso2Ongoing)
                evcc.OngoingTimeout = iso2Ongoing;

            await evcc.RunAsync(ct);

            // Where the pack ended up and why the loop stopped. Printed rather than merely returned,
            // because a battery-driven arm's whole result is this line — a session that ran to the
            // iteration ceiling and one that charged to its target look identical in the exchange count.
            if (evcc.Battery is { } pack)
                TestContext.Out.WriteLine(pack.Describe(evcc.BatteryStop ?? ChargeStop.Running));

            ProvisioningOutcome? provisioning = null;
            if (certificateProvisioning is { } request)
            {
                // Offered is read from what came back rather than from the ServiceList: Evcc2 only runs the
                // exchange when it found the service, so an installed certificate is the proof it was there.
                // A station that advertised nothing leaves all of this null and the run note says so.
                String? subject = null;
                if (evcc.InstalledContractCertificate is { } der)
                {
                    using var contract = X509CertificateLoader.LoadCertificate(der);
                    subject = contract.Subject;
                }

                provisioning = new ProvisioningOutcome(
                                   request.Action.ToString(),
                                   Offered:         evcc.InstalledContractCertificate is not null,
                                   SignatureOk:     evcc.InstalledContractSignatureOk,
                                   Emaid:           evcc.InstalledEmaid,
                                   ContractSubject: subject,
                                   KeyRecovered:    evcc.InstalledContractKey is not null);
            }

            return new EvccOutcome(evcc.Exchanges, evcc.AuthorizationMode, evcc.MeteringReceiptsSent,
                                   Provisioning:   provisioning,
                                   Renegotiations: evcc.Renegotiations,
                                   BatteryStop:    evcc.BatteryStop,
                                   DeliveredWh:    evcc.Battery?.DeliveredWh);
        }

        Evcc20Base evcc20 = (mode, mcs) switch
        {
            (PowerMode.Dc, true ) => new Evcc20Mcs(stream, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout),
            (PowerMode.Dc, false) => new Evcc20Dc (stream, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout),
            _                     => new Evcc20Ac (stream, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout),
        };

        evcc20.PreferDynamicControlMode  = preferDynamic;
        evcc20.PreferBidirectionalService = bptFirst;
        evcc20.Pnc                       = pnc;
        evcc20.RequestMeterInfo          = requestMeterInfo;
        evcc20.GoSilentInChargeLoop      = silentInChargeLoop;
        evcc20.SendSessionId             = sendSessionId;
        evcc20.SupportedServiceIds       = supportedServiceIds;

        if (ongoingTimeout is { } iso20Ongoing)
            evcc20.OngoingTimeout = iso20Ongoing;

        await evcc20.RunAsync(ct);
        return new EvccOutcome(evcc20.Exchanges, evcc20.AuthorizationMode, MeteringReceiptsSent: 0,
                               evcc20.SelectedEnergyServiceId, evcc20.MeterInfoResponses,
                               evcc20.SilenceEndedAfter);

    }


    /// <summary>The ISO 15118-20 energy-transfer service ids (Table 204), so a run's own output names the
    /// catalogue entry that was negotiated instead of leaving a bare number to be looked up.</summary>
    /// <remarks>Here rather than in a fixture because more than one counterparty's EV selects from our
    /// catalogue, and the table is the protocol's, not any one peer's.</remarks>
    public static String ServiceName(UInt16 serviceId)
        => serviceId switch
           {
               1 => "AC",   2 => "DC",  3 => "WPT", 4 => "DC_ACDP", 5 => "AC_BPT",
               6 => "DC_BPT", 7 => "DC_ACDP_BPT",  8 => "MCS",      9 => "MCS_BPT",
               _ => "unknown to Table 204 as we read it",
           };


    /// <param name="offerPlugAndCharge">-20 only: advertise Plug &amp; Charge alongside EIM. False narrows the
    /// offer to EIM, for an EV that cannot ignore a service it does not support.</param>
    /// <param name="preferDynamic">-20 only: offer the Dynamic (ControlMode 2) parameter set first. An EV
    /// that takes the first offered set then runs a Dynamic session — which is the mode eVDriveFlow works
    /// in, and the one that drives schedule renegotiation. Ignored for -2, which has no control modes.</param>
    /// <param name="mcs">Advertise the <b>MCS</b> catalogue — services 8 / 9 and a megawatt envelope —
    /// rather than DC's 2 / 6. -20 DC only; set by <c>V2G_INTEROP_MODE=mcs</c>.</param>
    /// <param name="observed">Called with the outcome as it stands when this method leaves, <b>including
    /// when it leaves by throwing</b> — so a caller can report what our station learned from a session
    /// that did not finish.</param>
    /// <remarks>
    /// The counterpart of recording in a <c>finally</c>, and it arrived for the same reason: the run that
    /// breaks is the one worth reading. A peer that dies mid-charge-loop used to take the whole verdict
    /// with it — which service its EV had selected out of our catalogue, whether its contract signature
    /// verified — because those live on the state machine and the exception unwound past them. That cost
    /// the 2026-08-07 eVDriveFlow runs their headline: their EV picks <b>DC_BPT</b>, and the only way to
    /// see it was to grep the counterparty's own log.
    /// </remarks>
    /// <returns>Whether our station reached the terminal session state, and what it saw on the way —
    /// see <see cref="SeccOutcome"/>.</returns>
    /// <param name="requestRenegotiation">Put an `[V2G20-1477]` <c>ServiceRenegotiation</c> notification
    /// into the first charge-loop response (-20) or an `[V2G2-841]` <c>ReNegotiation</c> into the first
    /// charging-status response (-2), once. Set by <c>V2G_INTEROP_RENEG=1</c>.
    /// <para>
    /// The station half has existed since 2026-07-22 and was reachable only from the CLI, which writes no
    /// artifacts — so the one live renegotiation session this project has against a foreign EV is a pair
    /// of console logs. This is the knob that makes such a run recordable.
    /// </para></param>
    public static async Task<SeccOutcome> RunSeccAsync(Stream stream, ProtocolVariant protocol, PowerMode mode,
                                                       CancellationToken ct, Boolean preferDynamic = false,
                                                       Boolean offerPlugAndCharge = true, Boolean mcs = false,
                                                       Action<SeccOutcome>? observed = null,
                                                       Boolean requestRenegotiation = false)
    {

        if (mcs)
            RefuseImpossibleMcs(protocol, mode);

        if (protocol == ProtocolVariant.Iso15118_2)
        {
            var secc = new Secc2(mode, SequenceTimeout, TimeProvider.System)
                           {
                               RequestRenegotiation   = requestRenegotiation,
                               ContractChainValidator = InteropEnvironment.ContractRootsOrNull(),
                           };

            // `Secc2` verifies a Contract session's signed AuthorizationReq and every signed
            // MeteringReceiptReq, and until 2026-08-15 this branch reported neither — so a reverse `-2`
            // Plug & Charge run was judged on IsDone, which a session with an unverifiable signature
            // reaches just as well. Their EV switches to Contract the moment the transport is TLS, so the
            // first run that met it was also the first that needed this.
            SeccOutcome Iso2Outcome() => new(secc.IsDone,
                                             SequenceErrorAt:   secc.SequenceErrorAt,
                                             PlugAndChargeIso2: secc.PnCAuth,
                                             MeteringReceipts:  secc.MeteringReceipts.Count > 0
                                                                    ? secc.MeteringReceipts
                                                                    : null);
            try
            {
                await secc.RunAsync(stream, ct);
            }
            finally
            {
                observed?.Invoke(Iso2Outcome());
            }
            return Iso2Outcome();
        }

        // ChargeLoopSequenceTimeout is init-only, so the knob has to be applied here rather than after
        // construction. Unset, every arm keeps Secc20Base's own 500 ms — the Tables 216/217 value, and
        // the one a conformance claim rests on. See InteropEnvironment.ChargeLoopTimeout.
        var chargeLoop = InteropEnvironment.ChargeLoopTimeout();

        Secc20Base secc20 = (mode, mcs) switch
        {
            (PowerMode.Dc, true ) => chargeLoop is { } m ? new Secc20Mcs(SequenceTimeout, TimeProvider.System) { ChargeLoopSequenceTimeout = m }
                                                        : new Secc20Mcs(SequenceTimeout, TimeProvider.System),
            (PowerMode.Dc, false) => chargeLoop is { } d ? new Secc20Dc (SequenceTimeout, TimeProvider.System) { ChargeLoopSequenceTimeout = d }
                                                        : new Secc20Dc (SequenceTimeout, TimeProvider.System),
            _                     => chargeLoop is { } a ? new Secc20Ac (SequenceTimeout, TimeProvider.System) { ChargeLoopSequenceTimeout = a }
                                                        : new Secc20Ac (SequenceTimeout, TimeProvider.System),
        };

        secc20.PreferDynamicControlMode = preferDynamic;
        secc20.OfferPlugAndCharge       = offerPlugAndCharge;
        secc20.RequestRenegotiation     = requestRenegotiation;
        // Same knob as the `-2` branch above, for the same reason: without it an inbound contract
        // signature is checked against the leaf the car presented and nobody asks who issued it.
        secc20.ContractChainValidator   = InteropEnvironment.ContractRootsOrNull();

        // Zero is "no ServiceSelectionReq ever arrived", not service 0 — the state machine's own sentinel,
        // and the CLI reads it the same way. Reported as null so a session that stopped before selection is
        // not filed as one that selected something.
        SeccOutcome SoFar() => new(secc20.IsDone,
                                   secc20.SelectedEnergyServiceId != 0 ? secc20.SelectedEnergyServiceId : null,
                                   secc20.PnCAuth,
                                   EvControlModeIsDynamic: secc20.EvControlModeIsDynamic);

        try
        {
            await secc20.RunAsync(stream, ct);
        }
        finally
        {
            observed?.Invoke(SoFar());
        }

        return SoFar();

    }


    /// <summary>
    /// MCS rides the -20 DC message set and nothing else, so the two combinations that cannot mean anything
    /// are refused here rather than silently dropped.
    /// </summary>
    /// <remarks>
    /// <see cref="InteropEnvironment.ProtocolAndMode"/> already rules both out for a run configured from the
    /// environment. This guard is for the direct caller, and for the same reason the environment has one: a
    /// request for MCS that quietly becomes an ordinary AC or -2 session is a run that proves something
    /// other than what it will be written up as.
    /// </remarks>
    private static void RefuseImpossibleMcs(ProtocolVariant protocol, PowerMode mode)
    {

        if (protocol != ProtocolVariant.Iso15118_20)
            throw new ArgumentException(
                "MCS was requested for an ISO 15118-2 session, but service ids 8 / 9 exist only in the "
              + "-20 catalogue.", nameof(protocol));

        if (mode != PowerMode.Dc)
            throw new ArgumentException(
                "MCS was requested for an AC session; MCS is the DC message set under services 8 / 9.",
                nameof(mode));

    }

}
