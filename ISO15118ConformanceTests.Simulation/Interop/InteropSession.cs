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

using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Timing;

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
    public sealed record EvccOutcome(Int32 Exchanges, String AuthorizationMode, Int32 MeteringReceiptsSent,
                                     UInt16? SelectedEnergyServiceId = null);


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
    /// </remarks>
    public sealed record SeccOutcome(Boolean IsDone, UInt16? SelectedEnergyServiceId = null,
                                     PnCAuthResult? PlugAndCharge = null);


    /// <param name="preferDynamic">-20 only: drive the session in Dynamic control mode (ControlMode = 2)
    /// rather than Scheduled — the EV states energy needs and a departure time and lets the station steer.
    /// Ignored for -2, which has no control modes. Set by <c>V2G_INTEROP_DYNAMIC=1</c>.</param>
    /// <param name="pnc">Contract credentials; when set <i>and</i> the station offers Contract/PnC, the
    /// session authorizes with a signed AuthorizationReq instead of EIM. Set by
    /// <c>V2G_INTEROP_CONTRACT_CERT</c>.</param>
    /// <param name="mcs">Drive the -20 DC session as <b>MCS</b>: ask for energy-transfer services 8 / 9
    /// instead of 2 / 6. Set by <c>V2G_INTEROP_MODE=mcs</c>; see <see cref="InteropEnvironment.Mcs"/>.</param>
    /// <param name="mcsBptFirst">With <paramref name="mcs"/>: rank MCS_BPT (9) ahead of MCS (8), which is
    /// the only way to reach the bidirectional entry of a catalogue that carries both. Set by
    /// <c>V2G_INTEROP_MCS_FIRST=9</c>; see <see cref="McsBptFirstEvcc"/> for what it does <i>not</i>
    /// make bidirectional.</param>
    public static async Task<EvccOutcome> RunEvccAsync(Stream stream, ProtocolVariant protocol, PowerMode mode,
                                                       CancellationToken ct, Boolean preferDynamic = false,
                                                       PncEvccOptions? pnc = null, Boolean mcs = false,
                                                       Boolean mcsBptFirst = false)
    {

        if (mcs)
            RefuseImpossibleMcs(protocol, mode);

        // Stated rather than implied: ranking 9 first is meaningless outside an MCS catalogue, and silently
        // ignoring it would leave a run that was asked for MCS_BPT looking like one that got it.
        else if (mcsBptFirst)
            throw new ArgumentException(
                "MCS_BPT was ranked first without an MCS session; services 8 / 9 are only asked for when "
              + "mcs is set.", nameof(mcsBptFirst));

        if (protocol == ProtocolVariant.Iso15118_2)
        {
            var evcc = new Evcc2(stream, mode, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout)
                           { Pnc = pnc };
            await evcc.RunAsync(ct);
            return new EvccOutcome(evcc.Exchanges, evcc.AuthorizationMode, evcc.MeteringReceiptsSent);
        }

        Evcc20Base evcc20 = (mode, mcs, mcsBptFirst) switch
        {
            (PowerMode.Dc, true, true ) => new McsBptFirstEvcc(stream, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout),
            (PowerMode.Dc, true, false) => new Evcc20Mcs      (stream, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout),
            (PowerMode.Dc, false,    _) => new Evcc20Dc       (stream, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout),
            _                           => new Evcc20Ac       (stream, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout),
        };

        evcc20.PreferDynamicControlMode = preferDynamic;
        evcc20.Pnc                      = pnc;

        await evcc20.RunAsync(ct);
        return new EvccOutcome(evcc20.Exchanges, evcc20.AuthorizationMode, MeteringReceiptsSent: 0,
                               evcc20.SelectedEnergyServiceId);

    }


    /// <param name="offerPlugAndCharge">-20 only: advertise Plug &amp; Charge alongside EIM. False narrows the
    /// offer to EIM, for an EV that cannot ignore a service it does not support.</param>
    /// <param name="preferDynamic">-20 only: offer the Dynamic (ControlMode 2) parameter set first. An EV
    /// that takes the first offered set then runs a Dynamic session — which is the mode eVDriveFlow works
    /// in, and the one that drives schedule renegotiation. Ignored for -2, which has no control modes.</param>
    /// <param name="mcs">Advertise the <b>MCS</b> catalogue — services 8 / 9 and a megawatt envelope —
    /// rather than DC's 2 / 6. -20 DC only; set by <c>V2G_INTEROP_MODE=mcs</c>.</param>
    /// <returns>Whether our station reached the terminal session state, and what it saw on the way —
    /// see <see cref="SeccOutcome"/>.</returns>
    public static async Task<SeccOutcome> RunSeccAsync(Stream stream, ProtocolVariant protocol, PowerMode mode,
                                                       CancellationToken ct, Boolean preferDynamic = false,
                                                       Boolean offerPlugAndCharge = true, Boolean mcs = false)
    {

        if (mcs)
            RefuseImpossibleMcs(protocol, mode);

        if (protocol == ProtocolVariant.Iso15118_2)
        {
            var secc = new Secc2(mode, SequenceTimeout, TimeProvider.System);
            await secc.RunAsync(stream, ct);
            return new SeccOutcome(secc.IsDone);
        }

        Secc20Base secc20 = (mode, mcs) switch
        {
            (PowerMode.Dc, true ) => new Secc20Mcs(SequenceTimeout, TimeProvider.System),
            (PowerMode.Dc, false) => new Secc20Dc (SequenceTimeout, TimeProvider.System),
            _                     => new Secc20Ac (SequenceTimeout, TimeProvider.System),
        };

        secc20.PreferDynamicControlMode = preferDynamic;
        secc20.OfferPlugAndCharge       = offerPlugAndCharge;

        await secc20.RunAsync(stream, ct);

        // Zero is "no ServiceSelectionReq ever arrived", not service 0 — the state machine's own sentinel,
        // and the CLI reads it the same way. Reported as null so a session that stopped before selection is
        // not filed as one that selected something.
        return new SeccOutcome(secc20.IsDone,
                               secc20.SelectedEnergyServiceId != 0 ? secc20.SelectedEnergyServiceId : null,
                               secc20.PnCAuth);

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
