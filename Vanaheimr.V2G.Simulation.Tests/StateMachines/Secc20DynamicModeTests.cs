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
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

using Ac20 = cloud.charging.open.protocols.ISO15118_20.AC.Generated;
using Dc20 = cloud.charging.open.protocols.ISO15118_20.DC.Generated;

namespace Vanaheimr.V2G.Simulation.Tests.StateMachines
{
    /// <summary>
    /// Dynamic control-mode coverage ([V2G20-1600]: the res control mode must be the same variant as the
    /// req's). Before the fix, a Dynamic-mode EV got a *Scheduled* ScheduleExchangeRes/ChargeLoopRes — a
    /// wire-type mismatch a live Josev EVCC crashes on. The SECC now (a) offers both ControlMode parameter
    /// sets (Scheduled=1, Dynamic=2; order flipped by <see cref="Secc20Base.PreferDynamicControlMode"/>) and
    /// (b) answers ScheduleExchange and the charge loop strictly in kind for all four control-mode variants.
    /// </summary>
    [TestFixture]
    public class Secc20DynamicModeTests
    {
        private readonly SessionContext _ctx = new(TimeProvider.System);
        private MessageHeaderType Common => _ctx.ToCommonHeader();
        private Dc20.MessageHeaderType Dc => _ctx.ToDcHeader();
        private Ac20.MessageHeaderType Ac => _ctx.ToAcHeader();

        private static Dc20.RationalNumberType DcRat(short value, sbyte exponent = 0) => new(exponent, value);
        private static Ac20.RationalNumberType AcRat(short value, sbyte exponent = 0) => new(exponent, value);
        private static RationalNumberType Rat(short value, sbyte exponent = 0) => new(exponent, value);

        private static Dynamic_SEReqControlModeType DynamicSeReq() =>
            new(DepartureTime: 7200, MinimumSOC: 30, TargetSOC: 80,
                EVTargetEnergyRequest: Rat(40, 3), EVMaximumEnergyRequest: Rat(6_000, 1), EVMinimumEnergyRequest: Rat(-20_000),
                EVMaximumV2XEnergyRequest: null, EVMinimumV2XEnergyRequest: null);

        private static int? ControlModeOf(ParameterSetType set) =>
            set.Parameter.First(p => p.Name == "ControlMode").IntValue;

        private void RunDcSetup(Secc20Dc secc, ushort serviceId)
        {
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionSetupReq(Common, "EVCC01"));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Common));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationReq(Common, Authorization.EIM, new EIM_AReqAuthorizationModeType(), null));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDiscoveryReq(Common, null));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, serviceId));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceSelectionReq(Common, new SelectedServiceType(serviceId, 2), null));
        }

        /// <summary>
        /// Service renegotiation ([V2G20-1477]) at the Handle level: the SECC (RequestRenegotiation) puts
        /// <c>EvseNotification.ServiceRenegotiation</c> into the first charge-loop response; the EV stops
        /// power delivery and sends <c>SessionStopReq(ServiceRenegotiation)</c> — the session does NOT end
        /// but re-enters ServiceDiscovery, re-runs the whole middle, and terminates normally afterwards.
        /// This mirrors exactly what a live Josev EVCC does on that notification.
        /// </summary>
        [Test]
        public void ServiceRenegotiation_ReentersServiceDiscovery_AndCompletes()
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System) { RequestRenegotiation = true };
            RunDcSetup(secc, serviceId: 2);
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeParameterDiscoveryReq(Dc,
                new Dc20.DC_CPDReqEnergyTransferModeType(DcRat(5_000, 1), DcRat(0), DcRat(200), DcRat(0), DcRat(500), DcRat(50), TargetSOC: 80)));
            secc.Handle(MessageSet.Iso20CommonMessages, new ScheduleExchangeReq(Common, 12, null,
                new Scheduled_SEReqControlModeType(null, null, null, null, null)));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_CableCheckReq(Dc));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_PreChargeReq(Dc, Dc20.Processing.Finished, DcRat(0), DcRat(400)));
            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Start, null, null));

            // First charge-loop response must carry the one-shot ServiceRenegotiation notification.
            var loop = (Dc20.DC_ChargeLoopRes)secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeLoopReq(Dc, null, false, DcRat(400),
                new Dc20.Scheduled_DC_CLReqControlModeType(null, null, null, DcRat(120), DcRat(400), null, null, null, null, null))).Response;
            Assert.That(loop.EVSEStatus?.EVSENotification, Is.EqualTo(Dc20.EvseNotification.ServiceRenegotiation));

            // The EV reacts: PowerDelivery(Stop) + SessionStopReq(ServiceRenegotiation) → session continues.
            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Stop, null, null));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_WeldingDetectionReq(Dc, Dc20.Processing.Finished));
            var stop = (SessionStopRes)secc.Handle(MessageSet.Iso20CommonMessages,
                new SessionStopReq(Common, ChargingSession.ServiceRenegotiation, null, null)).Response;
            Assert.That(stop.ResponseCode, Is.EqualTo(ResponseCode.OK));
            Assert.That(secc.IsDone, Is.False, "ServiceRenegotiation must NOT end the session");
            Assert.That(secc.Renegotiations, Is.EqualTo(1));

            // Round 2: the full middle replays from ServiceDiscovery — no second notification this time.
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDiscoveryReq(Common, null));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, 2));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceSelectionReq(Common, new SelectedServiceType(2, 1), null));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeParameterDiscoveryReq(Dc,
                new Dc20.DC_CPDReqEnergyTransferModeType(DcRat(5_000, 1), DcRat(0), DcRat(200), DcRat(0), DcRat(500), DcRat(50), TargetSOC: 80)));
            secc.Handle(MessageSet.Iso20CommonMessages, new ScheduleExchangeReq(Common, 12, null,
                new Scheduled_SEReqControlModeType(null, null, null, null, null)));
            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Start, null, null));
            var loop2 = (Dc20.DC_ChargeLoopRes)secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeLoopReq(Dc, null, false, DcRat(400),
                new Dc20.Scheduled_DC_CLReqControlModeType(null, null, null, DcRat(120), DcRat(400), null, null, null, null, null))).Response;
            Assert.That(loop2.EVSEStatus, Is.Null, "the renegotiation notification fires exactly once");

            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Stop, null, null));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_WeldingDetectionReq(Dc, Dc20.Processing.Finished));
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionStopReq(Common, ChargingSession.Terminate, null, null));
            Assert.That(secc.IsDone, Is.True);
        }

        [Test]
        public void ServiceDetail_OffersBothControlModes_ScheduledFirstByDefault()
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionSetupReq(Common, "EVCC01"));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Common));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationReq(Common, Authorization.EIM, new EIM_AReqAuthorizationModeType(), null));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDiscoveryReq(Common, null));

            var detail = (ServiceDetailRes)secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, 2)).Response;
            var modes = detail.ServiceParameterList.ParameterSet.Select(ControlModeOf).ToArray();
            Assert.That(modes, Is.EqualTo(new int?[] { 1, 2 }), "offers Scheduled then Dynamic by default");
        }

        [Test]
        public void ServiceDetail_WithPreferDynamic_OffersDynamicFirst()
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System) { PreferDynamicControlMode = true };
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionSetupReq(Common, "EVCC01"));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Common));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationReq(Common, Authorization.EIM, new EIM_AReqAuthorizationModeType(), null));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDiscoveryReq(Common, null));

            var detail = (ServiceDetailRes)secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, 2)).Response;
            var modes = detail.ServiceParameterList.ParameterSet.Select(ControlModeOf).ToArray();
            Assert.That(modes, Is.EqualTo(new int?[] { 2, 1 }),
                "a first-set-wins EV (Josev) must see Dynamic first when preferred");
        }

        [Test]
        public void DynamicScheduleExchange_GetsDynamicRes()
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            RunDcSetup(secc, serviceId: 2);
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeParameterDiscoveryReq(Dc,
                new Dc20.DC_CPDReqEnergyTransferModeType(DcRat(5_000, 1), DcRat(0), DcRat(200), DcRat(0), DcRat(500), DcRat(50), TargetSOC: 80)));

            var se = (ScheduleExchangeRes)secc.Handle(MessageSet.Iso20CommonMessages,
                new ScheduleExchangeReq(Common, 12, DynamicSeReq(), null)).Response;

            Assert.Multiple(() =>
            {
                Assert.That(se.Dynamic_SEResControlMode, Is.Not.Null, "a Dynamic SE req must get a Dynamic SE res");
                Assert.That(se.Scheduled_SEResControlMode, Is.Null);
                Assert.That(se.Dynamic_SEResControlMode!.DepartureTime, Is.EqualTo(7200), "departure time echoed");
            });
        }

        [Test]
        public void DcDynamicChargeLoop_GetsDynamicResWithEvseLimits()
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            RunDcSetup(secc, serviceId: 2);
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeParameterDiscoveryReq(Dc,
                new Dc20.DC_CPDReqEnergyTransferModeType(DcRat(5_000, 1), DcRat(0), DcRat(200), DcRat(0), DcRat(500), DcRat(50), TargetSOC: 80)));
            secc.Handle(MessageSet.Iso20CommonMessages, new ScheduleExchangeReq(Common, 12, DynamicSeReq(), null));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_CableCheckReq(Dc));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_PreChargeReq(Dc, Dc20.Processing.Finished, DcRat(0), DcRat(400)));
            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Start, null, null));

            var loop = (Dc20.DC_ChargeLoopRes)secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeLoopReq(Dc, null, false, DcRat(400),
                new Dc20.Dynamic_DC_CLReqControlModeType(DepartureTime: 7200,
                    EVTargetEnergyRequest: DcRat(40, 3), EVMaximumEnergyRequest: DcRat(6_000, 1), EVMinimumEnergyRequest: DcRat(-20_000),
                    EVMaximumChargePower: DcRat(300, 3), EVMinimumChargePower: DcRat(100),
                    EVMaximumChargeCurrent: DcRat(300), EVMaximumVoltage: DcRat(1_000), EVMinimumVoltage: DcRat(10)))).Response;

            Assert.That(loop.CLResControlMode, Is.TypeOf<Dc20.Dynamic_DC_CLResControlModeType>(),
                "a Dynamic charge-loop req must get a Dynamic res (not Scheduled, not BPT)");
            var dyn = (Dc20.Dynamic_DC_CLResControlModeType)loop.CLResControlMode;
            Assert.That(dyn.EVSEMaximumChargePower, Is.Not.Null, "dynamic mode: the SECC dictates the operating point");
        }

        [Test]
        public void DcBptDynamicChargeLoop_GetsBptDynamicRes()
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            RunDcSetup(secc, serviceId: 6);
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeParameterDiscoveryReq(Dc,
                new Dc20.BPT_DC_CPDReqEnergyTransferModeType(DcRat(5_000, 1), DcRat(0), DcRat(200), DcRat(0), DcRat(500), DcRat(50), TargetSOC: 80,
                    EVMaximumDischargePower: DcRat(5_000, 1), EVMinimumDischargePower: DcRat(0),
                    EVMaximumDischargeCurrent: DcRat(200), EVMinimumDischargeCurrent: DcRat(0))));
            secc.Handle(MessageSet.Iso20CommonMessages, new ScheduleExchangeReq(Common, 12, DynamicSeReq(), null));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_CableCheckReq(Dc));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_PreChargeReq(Dc, Dc20.Processing.Finished, DcRat(0), DcRat(400)));
            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Start, null, null));

            var loop = (Dc20.DC_ChargeLoopRes)secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeLoopReq(Dc, null, false, DcRat(400),
                new Dc20.BPT_Dynamic_DC_CLReqControlModeType(DepartureTime: 7200,
                    EVTargetEnergyRequest: DcRat(40, 3), EVMaximumEnergyRequest: DcRat(6_000, 1), EVMinimumEnergyRequest: DcRat(-20_000),
                    EVMaximumChargePower: DcRat(300, 3), EVMinimumChargePower: DcRat(100),
                    EVMaximumChargeCurrent: DcRat(300), EVMaximumVoltage: DcRat(1_000), EVMinimumVoltage: DcRat(10),
                    EVMaximumDischargePower: DcRat(11, 3), EVMinimumDischargePower: DcRat(1, -3),
                    EVMaximumDischargeCurrent: DcRat(11), EVMaximumV2XEnergyRequest: null, EVMinimumV2XEnergyRequest: null))).Response;

            Assert.That(loop.CLResControlMode, Is.TypeOf<Dc20.BPT_Dynamic_DC_CLResControlModeType>(),
                "a BPT_Dynamic charge-loop req must get a BPT_Dynamic res (with discharge limits)");
        }

        [Test]
        public void AcDynamicChargeLoops_GetDynamicResInKind()
        {
            var secc = new Secc20Ac(TimeSpan.FromSeconds(60), TimeProvider.System);
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionSetupReq(Common, "EVCC01"));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Common));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationReq(Common, Authorization.EIM, new EIM_AReqAuthorizationModeType(), null));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDiscoveryReq(Common, null));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, 5));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceSelectionReq(Common, new SelectedServiceType(5, 2), null));
            secc.Handle(MessageSet.Iso20AC, new Ac20.AC_ChargeParameterDiscoveryReq(Ac,
                new Ac20.BPT_AC_CPDReqEnergyTransferModeType(
                    EVMaximumChargePower: AcRat(2_200, 1), null, null, EVMinimumChargePower: AcRat(0), null, null,
                    EVMaximumDischargePower: AcRat(2_200, 1), null, null, EVMinimumDischargePower: AcRat(0), null, null)));
            secc.Handle(MessageSet.Iso20CommonMessages, new ScheduleExchangeReq(Common, 12, DynamicSeReq(), null));
            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Start, null, null));

            var loop = (Ac20.AC_ChargeLoopRes)secc.Handle(MessageSet.Iso20AC, new Ac20.AC_ChargeLoopReq(Ac, null, false,
                new Ac20.BPT_Dynamic_AC_CLReqControlModeType(DepartureTime: 2000,
                    EVTargetEnergyRequest: AcRat(40, 3), EVMaximumEnergyRequest: AcRat(60, 3), EVMinimumEnergyRequest: AcRat(-20, 3),
                    EVMaximumChargePower: AcRat(300, 3), EVMaximumChargePower_L2: null, EVMaximumChargePower_L3: null,
                    EVMinimumChargePower: AcRat(100), EVMinimumChargePower_L2: null, EVMinimumChargePower_L3: null,
                    EVPresentActivePower: AcRat(200, 3), EVPresentActivePower_L2: null, EVPresentActivePower_L3: null,
                    EVPresentReactivePower: AcRat(20, 3), EVPresentReactivePower_L2: null, EVPresentReactivePower_L3: null,
                    EVMaximumDischargePower: AcRat(11, 3), EVMaximumDischargePower_L2: null, EVMaximumDischargePower_L3: null,
                    EVMinimumDischargePower: AcRat(1, -3), EVMinimumDischargePower_L2: null, EVMinimumDischargePower_L3: null,
                    EVMaximumV2XEnergyRequest: null, EVMinimumV2XEnergyRequest: null))).Response;

            Assert.That(loop.CLResControlMode, Is.TypeOf<Ac20.BPT_Dynamic_AC_CLResControlModeType>(),
                "a BPT_Dynamic AC charge-loop req must get a BPT_Dynamic res");

            var next = (Ac20.AC_ChargeLoopRes)secc.Handle(MessageSet.Iso20AC, new Ac20.AC_ChargeLoopReq(Ac, null, false,
                new Ac20.Dynamic_AC_CLReqControlModeType(DepartureTime: 2000,
                    EVTargetEnergyRequest: AcRat(40, 3), EVMaximumEnergyRequest: AcRat(60, 3), EVMinimumEnergyRequest: AcRat(-20, 3),
                    EVMaximumChargePower: AcRat(300, 3), null, null, EVMinimumChargePower: AcRat(100), null, null,
                    EVPresentActivePower: AcRat(200, 3), null, null, EVPresentReactivePower: AcRat(20, 3), null, null))).Response;

            Assert.That(next.CLResControlMode, Is.TypeOf<Ac20.Dynamic_AC_CLResControlModeType>(),
                "a plain Dynamic AC charge-loop req must get a plain Dynamic res");
        }
    }
}
