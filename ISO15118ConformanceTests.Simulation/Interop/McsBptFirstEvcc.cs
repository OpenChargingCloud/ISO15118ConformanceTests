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
/// the envelope, the state machine, the whole session — is the app's and is used unmodified. Only the
/// ranking is the probe's, and one chosen to interrogate a single counterparty's catalogue is a test
/// input, not a vehicle the simulator should ship. It derives from <c>Evcc20Mcs</c>, so it <b>is</b> a
/// megawatt truck and differs from one in exactly the one line below.
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
/// <b>The envelope used to be repeated here, and that went wrong.</b> While <c>Evcc20Mcs</c> was
/// <c>sealed</c> this class derived from <c>Evcc20Dc</c> and copied the megawatt limits by hand — until
/// they drifted, and the first complete MCS_BPT run caught their <c>EvseManager</c> reading back
/// <c>dc_ev_maximum_power_limit: 50000.0</c> under service 9: a DC envelope beneath a megawatt service,
/// the exact defect <c>Evcc20Mcs</c> had just been fixed to prevent. The class is open now, the copy is
/// gone, and the envelope can only be wrong here if it is wrong for every megawatt truck.
/// </para>
/// </remarks>
internal sealed class McsBptFirstEvcc(Stream stream, TimeProvider clock, IAsyncDelay pollDelay,
                                      TimeSpan perMessageTimeout)
    : Evcc20Mcs(stream, clock, pollDelay, perMessageTimeout)
{

    /// <summary>MCS_BPT (9) first, MCS (8) behind it — <c>Evcc20Mcs</c>'s list reversed, and the whole of
    /// what this class changes. The envelope, the drivable set and the state machine are inherited.</summary>
    protected override IReadOnlyList<UInt16> PreferredEnergyServiceIds => new UInt16[] { 9, 8 };

    // 8 stays in the list, and DrivableEnergyServiceIds stays at the base's { 2, 6, 8, 9 }: a station that
    // carries no MCS_BPT should still complete a session on whatever it does offer, so the run comes back
    // with a negotiated service id to report rather than a refusal to diagnose. The fixture's assertion,
    // not the state machine, is what decides whether that counts as an MCS_BPT result.

}
