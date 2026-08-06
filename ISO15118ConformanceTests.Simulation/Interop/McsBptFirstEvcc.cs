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

using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Timing;

using Dc20 = cloud.charging.open.protocols.ISO15118_20.DC.Generated;

namespace ISO15118ConformanceTests.Simulation.Interop;

/// <summary>
/// An MCS vehicle that ranks <b>MCS_BPT (9)</b> above <b>MCS (8)</b> — the probe that reaches the
/// bidirectional half of a station's catalogue.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reversal is the experiment, not a preference</b> — the same shape as
/// <see cref="InteropEnvironment.BothOffers"/>, one negotiation further in. The app's <c>Evcc20Mcs</c>
/// lists <c>{ 8, 9 }</c>, so against a station advertising both it never selects 9 and the BPT entry stays
/// untested.
/// </para>
/// <para>
/// <b>This probe found a defect before it could run at all, which is worth remembering here.</b> On its
/// first outing the reordering changed nothing: against EVerest's catalogue of <c>8, 9</c> the session
/// still negotiated 8, because <c>Evcc20Base.SelectEnergyTransferService</c> walked the <i>station's</i>
/// list and took the first entry we accepted — <c>PreferredEnergyServiceIds</c>, documented "best first",
/// was a set and never a ranking. That is fixed in the app now and the list below means what it says; the
/// episode is in <c>docs/interop-runs/2026-08-05-everest-mcs-bpt/notes.md</c>, and it is the reason this
/// class states a full ranking rather than trusting a single entry to be picked.
/// </para>
/// <para>
/// <b>Why it lives here and not in the app.</b> Everything that acts on the list — the selection logic,
/// the state machine, the whole session — is the app's and is used unmodified. Only the acceptance set is
/// the probe's, and one chosen to interrogate a single counterparty's catalogue is a test input, not a
/// vehicle the simulator should ship. It derives from <c>Evcc20Dc</c> rather than <c>Evcc20Mcs</c> for the
/// mundane reason that the latter is <c>sealed</c>; the two differ in exactly this list.
/// </para>
/// <para>
/// <b>Selecting service 9 used not to make the session bidirectional</b>, and the first run with this class
/// is how we learned it: <c>Evcc20Dc</c> sent a plain <c>DC_CPDReqEnergyTransferModeType</c> under a BPT
/// service and EVerest refused it with <c>FAILED_WrongChargeParameter</c>. That is fixed in the app —
/// <c>Evcc20Base.BidirectionalService</c> derives the direction from the selected service and
/// <c>Evcc20Dc</c> builds the <c>BPT_*</c> types accordingly — so this class now drives a genuinely
/// bidirectional session.
/// </para>
/// <para>
/// <b>Why the envelope is repeated below.</b> Deriving from <c>Evcc20Dc</c> means inheriting the ordinary
/// DC envelope, which is exactly the defect the app fixed for <c>Evcc20Mcs</c> — and the first complete
/// MCS_BPT run caught the same thing here instead: their <c>EvseManager</c> read back
/// <c>dc_ev_maximum_power_limit: 50000.0</c> under service 9. The four properties mirror
/// <c>Evcc20Mcs</c>'s so a truck asking for MCS_BPT declares megawatts in <i>both</i> directions.
/// Duplication is the price of <c>sealed</c>; the better home is the app, either by unsealing
/// <c>Evcc20Mcs</c> or by letting it rank the bidirectional service first.
/// </para>
/// </remarks>
internal sealed class McsBptFirstEvcc(Stream stream, TimeProvider clock, IAsyncDelay pollDelay,
                                      TimeSpan perMessageTimeout)
    : Evcc20Dc(stream, clock, pollDelay, perMessageTimeout)
{

    /// <summary>MCS_BPT (9) first, MCS (8) behind it — <c>Evcc20Mcs</c>'s list reversed, and nothing
    /// else changed.</summary>
    protected override IReadOnlyList<UInt16> PreferredEnergyServiceIds => new UInt16[] { 9, 8 };

    // 8 stays in the list, and DrivableEnergyServiceIds stays at the base's { 2, 6, 8, 9 }: a station that
    // carries no MCS_BPT should still complete a session on whatever it does offer, so the run comes back
    // with a negotiated service id to report rather than a refusal to diagnose. The fixture's assertion,
    // not the state machine, is what decides whether that counts as an MCS_BPT result.

    // Evcc20Mcs's envelope, repeated because that class is sealed (see the remarks). 1250 V × 3000 A ≈
    // 3.75 MW; under a BPT service the base mirrors these onto the discharge half as well, so the truck
    // declares megawatts in both directions rather than only the one it charges through.
    protected override Dc20.RationalNumberType MaxPower   => new(3, 3750);   // 3.75 MW
    protected override Dc20.RationalNumberType MaxCurrent => new(0, 3000);   // 3000 A
    protected override Dc20.RationalNumberType MaxVoltage => new(0, 1250);   // 1250 V
    protected override Dc20.RationalNumberType MinVoltage => new(0,  150);   //  150 V

    protected override Dc20.RationalNumberType LoopMaxPower   => MaxPower;
    protected override Dc20.RationalNumberType LoopMaxCurrent => MaxCurrent;

}
