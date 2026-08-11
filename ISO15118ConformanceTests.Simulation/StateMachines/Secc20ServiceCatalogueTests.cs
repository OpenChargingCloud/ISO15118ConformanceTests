/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of ISO15118ConformanceTests
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{

    /// <summary>
    /// The station holds the EV to its own catalogue: a service it never advertised is refused at
    /// <c>ServiceDetail</c> and at <c>ServiceSelection</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both used to be echoes. <c>ServiceDetailRes</c> answered <c>OK</c> with whatever id arrived, and
    /// <c>ServiceSelection</c> assigned it to <c>SelectedEnergyServiceId</c> and answered <c>OK</c> — so an
    /// EV could select service 99 against a DC station, or DC against an AC one, and the session carried on
    /// with a number the station never stood behind.
    /// </para>
    /// <para>
    /// It matters because that value is read again: <c>BidirectionalServiceSelected</c> decides from it
    /// whether the charge-parameter and control-mode types the EV sends next must be the <c>BPT_*</c> ones.
    /// So an unadvertised id does not sit inert, it decides a conformance check.
    /// </para>
    /// <para>
    /// And it is the mirror of what EVerest found in us on 2026-08-03: our EVCC named an energy transfer
    /// mode instead of reading <c>ServiceDiscoveryRes</c>, and their station refused it. We started reading
    /// their catalogue that day and went on not checking our own for six days
    /// (<c>docs/interop-runs/2026-08-03-everest-ac/</c>).
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Secc20ServiceCatalogueTests
    {

        private readonly SessionContext _ctx = new(TimeProvider.System);
        private MessageHeaderType Common => _ctx.ToCommonHeader();

        /// <summary>Up to ServiceDiscovery, and back with what the station actually offered.</summary>
        /// <remarks>The SessionID is adopted from the station's own answer, as a real car does — see
        /// <see cref="Iso20SessionDriver.AdoptSessionId"/> for what this harness was sending before
        /// 2026-08-11 and why nothing noticed.</remarks>
        private ServiceDiscoveryRes RunToDiscovery(Secc20Base secc)
        {
            var setup = (SessionSetupRes) secc.Handle(MessageSet.Iso20CommonMessages,
                                                      new SessionSetupReq(Common, "EVCC01")).Response;
            _ctx.SessionId = setup.Header.SessionID;
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Common));
            secc.Handle(MessageSet.Iso20CommonMessages,
                        new AuthorizationReq(Common, Authorization.EIM, new EIM_AReqAuthorizationModeType(), null));
            return (ServiceDiscoveryRes) secc.Handle(MessageSet.Iso20CommonMessages,
                                                     new ServiceDiscoveryReq(Common, null)).Response;
        }

        private static Secc20Dc Dc() => new(TimeSpan.FromSeconds(60), TimeProvider.System);

        /// <summary>A DC station offers 2 and 6 — never 99, and never AC's 1.</summary>
        [TestCase((ushort) 99, TestName = "ServiceDetail_refuses_anIdFromNoCatalogue")]
        [TestCase((ushort)  1, TestName = "ServiceDetail_refuses_theOtherPowerModesService")]
        public void ServiceDetail_RefusesWhatWasNeverAdvertised(ushort serviceId)
        {
            var secc = Dc();
            RunToDiscovery(secc);

            var res = (ServiceDetailRes) secc.Handle(MessageSet.Iso20CommonMessages,
                                                     new ServiceDetailReq(Common, serviceId)).Response;

            Assert.That(res.ResponseCode, Is.EqualTo(ResponseCode.FAILED_ServiceIDInvalid),
                        "the station answered OK for a service it never put in its own catalogue");
        }

        [TestCase((ushort) 99, TestName = "ServiceSelection_refuses_anIdFromNoCatalogue")]
        [TestCase((ushort)  1, TestName = "ServiceSelection_refuses_theOtherPowerModesService")]
        public void ServiceSelection_RefusesWhatWasNeverAdvertised(ushort serviceId)
        {
            var secc = Dc();
            RunToDiscovery(secc);
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, 2));

            var res = (ServiceSelectionRes) secc.Handle(
                          MessageSet.Iso20CommonMessages,
                          new ServiceSelectionReq(Common, new SelectedServiceType(serviceId, 1), null)).Response;

            Assert.Multiple(() =>
            {
                Assert.That(res.ResponseCode, Is.EqualTo(ResponseCode.FAILED_ServiceSelectionInvalid));
                Assert.That(secc.SelectedEnergyServiceId, Is.EqualTo((ushort) 0),
                            "a refused selection must not leave the id standing — it is what "
                          + "BidirectionalServiceSelected reads");
            });
        }

        /// <summary>
        /// <c>[V2G20-433]</c> is about the <i>pair</i>: an advertised service carrying a parameter set this
        /// station never offered for it is refused too. Service 2 offers sets 1 and 2 and nothing else.
        /// </summary>
        /// <remarks>
        /// This is the half the check missed until 2026-08-10 — <c>Advertised</c> compared the id alone, so
        /// a car could select service 2 with parameter set 7 and be told <c>OK</c> for a control mode the
        /// station never described. Reading the requirement to correct a stale comment is what turned it up;
        /// nothing on the wire ever produced it, and this test is the reason it stays fixed.
        /// </remarks>
        [TestCase((ushort) 7,  TestName = "ServiceSelection_refuses_aParameterSetFromNoCatalogue")]
        [TestCase((ushort) 0,  TestName = "ServiceSelection_refuses_theZeroParameterSet")]
        public void ServiceSelection_RefusesAParameterSetNeverOffered(ushort parameterSetId)
        {
            var secc = Dc();
            RunToDiscovery(secc);
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, 2));

            var res = (ServiceSelectionRes) secc.Handle(
                          MessageSet.Iso20CommonMessages,
                          new ServiceSelectionReq(Common, new SelectedServiceType(2, parameterSetId), null)).Response;

            Assert.Multiple(() =>
            {
                Assert.That(res.ResponseCode, Is.EqualTo(ResponseCode.FAILED_ServiceSelectionInvalid));
                Assert.That(secc.SelectedEnergyServiceId, Is.EqualTo((ushort) 0));
            });
        }

        /// <summary>
        /// And a service whose detail was never asked for cannot be selected either — even when the service
        /// itself is advertised and the parameter set exists for its sibling. The ParameterSetIDs live only
        /// in a <c>ServiceDetailRes</c>, so a car naming a pair it was never sent is naming a value it
        /// invented, which is what <c>[V2G20-1216]</c> forbids it to do.
        /// </summary>
        /// <remarks>
        /// Written first as "straight from discovery to selection" and that is <b>not</b> this check: the
        /// sequence guard refuses a <c>ServiceSelectionReq</c> in phase <c>ServiceDetail</c> with
        /// <c>FAILED_SequenceError</c> before the catalogue is consulted at all. Asking detail for the
        /// <i>other</i> advertised service is what reaches this code — same phase, same station, one pair
        /// offered and a different one named.
        /// </remarks>
        [Test]
        public void ServiceSelection_RefusesAPairFromAServiceWhoseDetailWasNeverAsked()
        {
            var secc    = Dc();
            var offered = RunToDiscovery(secc).EnergyTransferServiceList.Service.Select(s => s.ServiceID).ToArray();

            Assert.That(offered, Is.SupersetOf(new ushort[] { 2, 6 }), "test assumes DC and DC_BPT are advertised");

            // Detail for 6 (DC_BPT), selection of 2 (DC). Both are advertised, and set 1 is not 6's property —
            // SvcDetail offers sets 1 and 2 for whichever service is asked about, so 2 would have carried it
            // too. What makes the pair (2, 1) refusable is that 2's detail was never requested, so that pair
            // was never put on the wire this session. The check is about what was provided, not about what
            // could have been.
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, 6));

            var res = (ServiceSelectionRes) secc.Handle(
                          MessageSet.Iso20CommonMessages,
                          new ServiceSelectionReq(Common, new SelectedServiceType(2, 1), null)).Response;

            Assert.That(res.ResponseCode, Is.EqualTo(ResponseCode.FAILED_ServiceSelectionInvalid));
        }

        /// <summary>
        /// The refusal ends the session, and that is this station's blanket rule for every failure rather
        /// than anything chosen here (<c>Secc20Base.Handle</c>: <c>Phase = IsFailure(response) ? Done</c>).
        /// </summary>
        /// <remarks>
        /// Written the other way round first — asserting that a corrected selection still works, as it does
        /// on the <c>-2</c> side where <c>Secc2.PowerOn</c> deliberately stays put. It does not, and the
        /// difference between the two state machines was worth finding: this test now pins the behaviour
        /// that exists rather than the one the new code asked for.
        /// </remarks>
        [Test]
        public void ARefusedSelection_EndsTheSession()
        {
            var secc = Dc();
            RunToDiscovery(secc);
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, 2));

            secc.Handle(MessageSet.Iso20CommonMessages,
                        new ServiceSelectionReq(Common, new SelectedServiceType(99, 1), null));

            Assert.That(secc.IsDone, Is.True,
                        "a failure response ends a -20 session; the EV is expected to stop, and ours does");
        }

        /// <summary>
        /// Every id the station advertised is accepted — the positive half, so the check cannot be satisfied
        /// by a station that refuses everything. A DC station offers DC and DC_BPT, and the second is what
        /// makes <c>BidirectionalServiceSelected</c> true.
        /// </summary>
        [Test]
        public void EveryAdvertisedService_IsAccepted()
        {
            var secc    = Dc();
            var offered = RunToDiscovery(secc).EnergyTransferServiceList.Service;

            Assert.That(offered, Is.Not.Empty);

            foreach (var service in offered)
            {
                var fresh = Dc();
                RunToDiscovery(fresh);
                fresh.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, service.ServiceID));

                var res = (ServiceSelectionRes) fresh.Handle(
                              MessageSet.Iso20CommonMessages,
                              new ServiceSelectionReq(Common, new SelectedServiceType(service.ServiceID, 1), null)).Response;

                Assert.That(res.ResponseCode, Is.EqualTo(ResponseCode.OK),
                            $"the station refused service {service.ServiceID}, which it advertised itself");
                Assert.That(fresh.SelectedEnergyServiceId, Is.EqualTo(service.ServiceID));
            }
        }

    }

}
