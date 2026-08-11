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

using System.Linq;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

using Dc20 = cloud.charging.open.protocols.ISO15118_20.DC.Generated;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{
    /// <summary>
    /// Direct (no-socket) tests for <see cref="Secc20Dc"/>'s phase machine, focused on the DC poll-loop
    /// self-looping: a real EV polls CableCheck / PreCharge / WeldingDetection repeatedly (each a separate
    /// request) before sending the next-phase message. The SECC must answer every poll in place and only
    /// advance when the next-phase message arrives — a live Josev EVCC → our SECC interop run surfaced the
    /// earlier one-shot behaviour ("DC_PreChargeReq not allowed in phase PowerOn" on the second poll).
    /// See docs/interop-runs/2026-07-21-iso20-dc-tcp-reverse/notes.md.
    /// </summary>
    [TestFixture]
    public class Secc20DcTransitionTests
    {
        private readonly SessionContext _ctx = new(TimeProvider.System);
        private MessageHeaderType Common => _ctx.ToCommonHeader();
        private Dc20.MessageHeaderType Dc => _ctx.ToDcHeader();

        /// <summary>Drives the SECC through the shared CommonMessages setup up to (not including) CableCheck.</summary>
        private void RunSetup(Secc20Dc secc)
        {
            _ctx.OpenSession(secc);
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Common));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationReq(Common, Authorization.EIM, new EIM_AReqAuthorizationModeType(), null));
            var disc = (ServiceDiscoveryRes)secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDiscoveryReq(Common, null)).Response;
            ushort svc = disc.EnergyTransferServiceList.Service[0].ServiceID;
            var detail = (ServiceDetailRes)secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, svc)).Response;
            ushort psid = detail.ServiceParameterList.ParameterSet[0].ParameterSetID;
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceSelectionReq(Common, new SelectedServiceType(svc, psid), null));

            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeParameterDiscoveryReq(Dc,
                new Dc20.DC_CPDReqEnergyTransferModeType(Rat(5_000, 1), Rat(0), Rat(200), Rat(0), Rat(500), Rat(50), TargetSOC: 80)));
            secc.Handle(MessageSet.Iso20CommonMessages, new ScheduleExchangeReq(Common, 12, null,
                new Scheduled_SEReqControlModeType(null, null, null, null, null)));
        }

        [Test]
        public void DcSession_WithPolledCableCheckPreChargeAndWelding_ReachesDone()
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            RunSetup(secc);

            // CableCheck: three polls in a row — each must answer with a CableCheckRes and stay in phase.
            for (int i = 0; i < 3; i++)
            {
                var (set, resp) = secc.Handle(MessageSet.Iso20DC, new Dc20.DC_CableCheckReq(Dc));
                Assert.That(set, Is.EqualTo(MessageSet.Iso20DC));
                Assert.That(resp, Is.InstanceOf<Dc20.DC_CableCheckRes>());
            }

            // PreCharge: the first DC_PreChargeReq ends the CableCheck loop; three polls total, all answered.
            for (int i = 0; i < 3; i++)
            {
                var (set, resp) = secc.Handle(MessageSet.Iso20DC,
                    new Dc20.DC_PreChargeReq(Dc, Dc20.Processing.Finished, EVPresentVoltage: Rat(0), EVTargetVoltage: Rat(400)));
                Assert.That(resp, Is.InstanceOf<Dc20.DC_PreChargeRes>());
            }

            // PowerDelivery(Start) ends the PreCharge loop and is itself a poll phase: a real EV repeats it
            // (EVProcessing=Ongoing) until it begins the charge loop — three polls here, all answered in place.
            for (int i = 0; i < 3; i++)
            {
                var (set, resp) = secc.Handle(MessageSet.Iso20CommonMessages,
                    new PowerDeliveryReq(Common, Processing.Ongoing, ChargeProgress.Start, BuildProfile(), null));
                Assert.That(resp, Is.InstanceOf<PowerDeliveryRes>());
            }

            for (int i = 0; i < 4; i++)
            {
                var (set, resp) = secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeLoopReq(Dc, null, false,
                    EVPresentVoltage: Rat(400), CLReqControlMode: new Dc20.Scheduled_DC_CLReqControlModeType(null, null, null, Rat(120), Rat(400), null, null, null, null, null)));
                Assert.That(resp, Is.InstanceOf<Dc20.DC_ChargeLoopRes>());
            }

            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Stop, null, null));

            // WeldingDetection: three polls, all answered in place.
            for (int i = 0; i < 3; i++)
            {
                var (set, resp) = secc.Handle(MessageSet.Iso20DC, new Dc20.DC_WeldingDetectionReq(Dc, Dc20.Processing.Finished));
                Assert.That(resp, Is.InstanceOf<Dc20.DC_WeldingDetectionRes>());
            }

            secc.Handle(MessageSet.Iso20CommonMessages, new SessionStopReq(Common, ChargingSession.Terminate, null, null));
            Assert.That(secc.IsDone, Is.True);
        }

        [Test]
        public void DcSession_SinglePollEach_StillReachesDone()
        {
            // The loopback EVCC sends exactly one CableCheck/PreCharge/Welding — the self-loop must not break that.
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            RunSetup(secc);

            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_CableCheckReq(Dc));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_PreChargeReq(Dc, Dc20.Processing.Finished, Rat(0), Rat(400)));
            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Start, BuildProfile(), null));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeLoopReq(Dc, null, false, Rat(400),
                new Dc20.Scheduled_DC_CLReqControlModeType(null, null, null, Rat(120), Rat(400), null, null, null, null, null)));
            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Stop, null, null));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_WeldingDetectionReq(Dc, Dc20.Processing.Finished));
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionStopReq(Common, ChargingSession.Terminate, null, null));

            Assert.That(secc.IsDone, Is.True);
        }

        [Test]
        public void SessionStopMidPreCharge_IsAcceptedAndEndsGracefully()
        {
            // The EV may abort at any time. Drive into the PreCharge poll phase, then send SessionStopReq
            // instead of continuing — it must be answered (not sequence-guarded, and not mis-cast to a DC
            // poll/charge-loop request by the wildcard phase arms). This is the live reverse-run abort case.
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            RunSetup(secc);
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_CableCheckReq(Dc));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_PreChargeReq(Dc, Dc20.Processing.Ongoing, Rat(0), Rat(400)));

            var (set, resp) = secc.Handle(MessageSet.Iso20CommonMessages, new SessionStopReq(Common, ChargingSession.Terminate, null, null));
            Assert.That(set, Is.EqualTo(MessageSet.Iso20CommonMessages));
            Assert.That(resp, Is.InstanceOf<SessionStopRes>());
            Assert.That(((SessionStopRes)resp).ResponseCode, Is.EqualTo(ResponseCode.OK));
            Assert.That(secc.IsDone, Is.True);
        }

        [Test]
        public void SessionStopRightAfterSetup_IsAcceptedAndEndsGracefully()
        {
            // Earliest realistic abort: right after ServiceSelection, before any DC exchange.
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            _ctx.OpenSession(secc);
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Common));

            var (_, resp) = secc.Handle(MessageSet.Iso20CommonMessages, new SessionStopReq(Common, ChargingSession.Terminate, null, null));
            Assert.That(resp, Is.InstanceOf<SessionStopRes>());
            Assert.That(secc.IsDone, Is.True);
        }

        [Test]
        public void DcBptSession_OffersBothServices_AndAnswersBptCpdAndChargeLoop()
        {
            // Bidirectional (DC_BPT) EV: our SECC must advertise the BPT service (id 6) alongside DC (id 2) —
            // else Josev aborts with WrongServiceID — and answer with the BPT energy-transfer-mode + control-mode
            // variants. Mirrors the live 2026-07-22 DC_BPT run.
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            _ctx.OpenSession(secc);
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Common));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationReq(Common, Authorization.EIM, new EIM_AReqAuthorizationModeType(), null));

            var disc = (ServiceDiscoveryRes)secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDiscoveryReq(Common, null)).Response;
            var ids = disc.EnergyTransferServiceList.Service.Select(s => s.ServiceID).ToArray();
            Assert.That(ids, Does.Contain((ushort)2).And.Contain((ushort)6), "offers both DC and DC_BPT");

            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, 6));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceSelectionReq(Common, new SelectedServiceType(6, 1), null));

            var cpd = (Dc20.DC_ChargeParameterDiscoveryRes)secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeParameterDiscoveryReq(Dc,
                new Dc20.BPT_DC_CPDReqEnergyTransferModeType(Rat(5_000, 1), Rat(0), Rat(200), Rat(0), Rat(500), Rat(50), TargetSOC: 80,
                    EVMaximumDischargePower: Rat(5_000, 1), EVMinimumDischargePower: Rat(0), EVMaximumDischargeCurrent: Rat(200), EVMinimumDischargeCurrent: Rat(0)))).Response;
            Assert.That(cpd.DC_CPDResEnergyTransferMode, Is.InstanceOf<Dc20.BPT_DC_CPDResEnergyTransferModeType>(),
                "a BPT charge-parameter request must get a BPT response with discharge limits");

            secc.Handle(MessageSet.Iso20CommonMessages, new ScheduleExchangeReq(Common, 12, null, new Scheduled_SEReqControlModeType(null, null, null, null, null)));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_CableCheckReq(Dc));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_PreChargeReq(Dc, Dc20.Processing.Finished, Rat(0), Rat(400)));
            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Start, BuildProfile(), null));

            var loop = (Dc20.DC_ChargeLoopRes)secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeLoopReq(Dc, null, false, Rat(400),
                new Dc20.BPT_Scheduled_DC_CLReqControlModeType(null, null, null, Rat(120), Rat(400), null, null, null, null, null, null, null, null))).Response;
            Assert.That(loop.CLResControlMode, Is.InstanceOf<Dc20.BPT_Scheduled_DC_CLResControlModeType>(),
                "a BPT charge-loop control mode must get a BPT response control mode");

            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Stop, null, null));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_WeldingDetectionReq(Dc, Dc20.Processing.Finished));
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionStopReq(Common, ChargingSession.Terminate, null, null));
            Assert.That(secc.IsDone, Is.True);
        }

        private EVPowerProfileType BuildProfile() => new(
            TimeAnchor: 0, Dynamic_EVPPTControlMode: null,
            Scheduled_EVPPTControlMode: new Scheduled_EVPPTControlModeType(1, PowerToleranceAcceptance.PowerToleranceConfirmed),
            EVPowerProfileEntries: new EVPowerProfileEntryListType(new[]
            {
                new PowerScheduleEntryType(Duration: 3600, Power: new RationalNumberType(3, 10), Power_L2: null, Power_L3: null),
            }));

        private static Dc20.RationalNumberType Rat(short value, sbyte exponent = 0) => new(exponent, value);
    }
}
