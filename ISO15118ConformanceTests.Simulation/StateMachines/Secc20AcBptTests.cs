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

namespace ISO15118ConformanceTests.Simulation.StateMachines
{
    /// <summary>
    /// Bidirectional (AC_BPT) coverage for <see cref="Secc20Ac"/>: the SECC must advertise the BPT service
    /// (id 5) alongside AC (id 1) and answer a BPT charge-parameter request with a BPT energy-transfer-mode
    /// (discharge power included). Mirrors the live 2026-07-22 AC_BPT run
    /// (docs/interop-runs/2026-07-22-iso20-ac-bpt-sdp/).
    /// </summary>
    [TestFixture]
    public class Secc20AcBptTests
    {
        private readonly SessionContext _ctx = new(TimeProvider.System);
        private MessageHeaderType Common => _ctx.ToCommonHeader();
        private Ac20.MessageHeaderType Ac => _ctx.ToAcHeader();
        private static Ac20.RationalNumberType Rat(short value, sbyte exponent = 0) => new(exponent, value);

        [Test]
        public void AcBptSession_OffersBothServices_AndAnswersBptCpd()
        {
            var secc = new Secc20Ac(TimeSpan.FromSeconds(60), TimeProvider.System);
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionSetupReq(Common, "EVCC01"));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Common));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationReq(Common, Authorization.EIM, new EIM_AReqAuthorizationModeType(), null));

            var disc = (ServiceDiscoveryRes)secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDiscoveryReq(Common, null)).Response;
            var ids = disc.EnergyTransferServiceList.Service.Select(s => s.ServiceID).ToArray();
            Assert.That(ids, Does.Contain((ushort)1).And.Contain((ushort)5), "offers both AC and AC_BPT");

            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, 5));
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceSelectionReq(Common, new SelectedServiceType(5, 1), null));

            var cpd = (Ac20.AC_ChargeParameterDiscoveryRes)secc.Handle(MessageSet.Iso20AC, new Ac20.AC_ChargeParameterDiscoveryReq(Ac,
                new Ac20.BPT_AC_CPDReqEnergyTransferModeType(
                    EVMaximumChargePower: Rat(2_200, 1), null, null, EVMinimumChargePower: Rat(0), null, null,
                    EVMaximumDischargePower: Rat(2_200, 1), null, null, EVMinimumDischargePower: Rat(0), null, null))).Response;
            Assert.That(cpd.AC_CPDResEnergyTransferMode, Is.InstanceOf<Ac20.BPT_AC_CPDResEnergyTransferModeType>(),
                "a BPT charge-parameter request must get a BPT response with discharge limits");
        }
    }
}
