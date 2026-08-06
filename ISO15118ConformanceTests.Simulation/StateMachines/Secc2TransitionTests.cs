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

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118_2;
using cloud.charging.open.protocols.ISO15118_2.Generated;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{
    /// <summary>
    /// Direct (no-socket) tests for <see cref="Secc2.Handle"/> — the phase-guarded dispatch that decides
    /// what's a legal next request. Exercises the full AC and DC happy paths plus the sequence guard's
    /// rejection of an out-of-order request.
    /// </summary>
    [TestFixture]
    public class Secc2TransitionTests
    {
        private static V2G_Message Wrap(byte[] sid, BodyBaseType body) =>
            new(new MessageHeaderType(sid, Notification: null, Signature: null), new BodyType(body));

        [Test]
        public void AcHappyPath_ReachesDone()
        {
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
            var sid = new byte[8];

            var setupRes = (SessionSetupResType)secc.Handle(Wrap(sid, new SessionSetupReqType(new byte[] { 1 }))).Body.BodyElement!;
            Assert.That(setupRes.ResponseCode, Is.EqualTo(ResponseCode.OK_NewSessionEstablished));

            secc.Handle(Wrap(sid, new ServiceDiscoveryReqType(null, null)));
            secc.Handle(Wrap(sid, new PaymentServiceSelectionReqType(PaymentOption.ExternalPayment,
                new SelectedServiceListType(new[] { new SelectedServiceType(1, null) }))));
            secc.Handle(Wrap(sid, new AuthorizationReqType(null, null)));
            secc.Handle(Wrap(sid, new ChargeParameterDiscoveryReqType(null, EnergyTransferMode.AC_three_phase_core,
                new AC_EVChargeParameterType(null, PhysicalValue.Of(22_000, UnitSymbol.Wh),
                    new PhysicalValueType(0, UnitSymbol.V, 400), new PhysicalValueType(0, UnitSymbol.A, 32),
                    new PhysicalValueType(0, UnitSymbol.A, 6)))));
            secc.Handle(Wrap(sid, new PowerDeliveryReqType(ChargeProgress.Start, 1, null, null)));
            secc.Handle(Wrap(sid, new ChargingStatusReqType()));
            secc.Handle(Wrap(sid, new PowerDeliveryReqType(ChargeProgress.Stop, 1, null, null)));
            secc.Handle(Wrap(sid, new SessionStopReqType(ChargingSession.Terminate)));

            Assert.That(secc.IsDone, Is.True);
        }

        [Test]
        public void DcHappyPath_ReachesDone()
        {
            var secc = new Secc2(PowerMode.Dc, TimeSpan.FromSeconds(60), TimeProvider.System);
            var sid = new byte[8];
            var evStatus = new DC_EVStatusType(EVReady: true, DC_EVErrorCode.NO_ERROR, EVRESSSOC: 50);

            secc.Handle(Wrap(sid, new SessionSetupReqType(new byte[] { 1 })));
            secc.Handle(Wrap(sid, new ServiceDiscoveryReqType(null, null)));
            secc.Handle(Wrap(sid, new PaymentServiceSelectionReqType(PaymentOption.ExternalPayment,
                new SelectedServiceListType(new[] { new SelectedServiceType(1, null) }))));
            secc.Handle(Wrap(sid, new AuthorizationReqType(null, null)));
            secc.Handle(Wrap(sid, new ChargeParameterDiscoveryReqType(null, EnergyTransferMode.DC_extended,
                new DC_EVChargeParameterType(null, evStatus, new PhysicalValueType(0, UnitSymbol.A, 200), null,
                    new PhysicalValueType(0, UnitSymbol.V, 500), null, null, 100, 80))));
            secc.Handle(Wrap(sid, new CableCheckReqType(evStatus)));
            secc.Handle(Wrap(sid, new PreChargeReqType(evStatus, new PhysicalValueType(0, UnitSymbol.V, 400), new PhysicalValueType(0, UnitSymbol.A, 2))));
            secc.Handle(Wrap(sid, new PowerDeliveryReqType(ChargeProgress.Start, 1, null, null)));
            secc.Handle(Wrap(sid, new CurrentDemandReqType(evStatus, new PhysicalValueType(0, UnitSymbol.A, 120),
                null, null, null, null, false, null, null, new PhysicalValueType(0, UnitSymbol.V, 400))));
            secc.Handle(Wrap(sid, new PowerDeliveryReqType(ChargeProgress.Stop, 1, null, null)));
            secc.Handle(Wrap(sid, new WeldingDetectionReqType(evStatus)));
            secc.Handle(Wrap(sid, new SessionStopReqType(ChargingSession.Terminate)));

            Assert.That(secc.IsDone, Is.True);
        }

        [Test]
        public void SessionStopMidSession_IsAcceptedAndEndsGracefully()
        {
            // The EV may abort at any time: a SessionStopReq must be answered (not sequence-guarded) in any
            // phase. Drive to mid-session, then abort right after Authorization (well before SessionStop).
            var secc = new Secc2(PowerMode.Dc, TimeSpan.FromSeconds(60), TimeProvider.System);
            var sid = new byte[8];

            secc.Handle(Wrap(sid, new SessionSetupReqType(new byte[] { 1 })));
            secc.Handle(Wrap(sid, new ServiceDiscoveryReqType(null, null)));
            secc.Handle(Wrap(sid, new PaymentServiceSelectionReqType(PaymentOption.ExternalPayment,
                new SelectedServiceListType(new[] { new SelectedServiceType(1, null) }))));
            secc.Handle(Wrap(sid, new AuthorizationReqType(null, null)));

            var stopRes = (SessionStopResType)secc.Handle(Wrap(sid, new SessionStopReqType(ChargingSession.Terminate))).Body.BodyElement!;
            Assert.That(stopRes.ResponseCode, Is.EqualTo(ResponseCode.OK));
            Assert.That(secc.IsDone, Is.True);
        }

        [Test]
        public void OutOfOrderRequest_IsAnsweredWithSequenceErrorAndEndsTheSession()
        {
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
            var sid = new byte[8];

            // Skip straight to PowerDelivery without SessionSetup first.
            var res = (PowerDeliveryResType)secc.Handle(
                          Wrap(sid, new PowerDeliveryReqType(ChargeProgress.Start, 1, null, null))).Body.BodyElement!;

            // On the wire, in the request's own response type — not in an exception nobody outside this
            // process can read. ISO 15118-2 answers the out-of-sequence request and *then* terminates.
            Assert.Multiple(() =>
            {
                Assert.That(res.ResponseCode,      Is.EqualTo(ResponseCode.FAILED_SequenceError));
                Assert.That(secc.IsDone,           Is.True, "the session ends with the refusal");
                Assert.That(secc.SequenceErrorAt,  Is.EqualTo("PowerDeliveryReq"),
                            "which message was refused — the fact IsDone alone cannot carry");
            });
        }


        /// <summary>
        /// The case that found this: a real car polls a phase our station has already finished.
        /// </summary>
        /// <remarks>
        /// A tux-evse injector replayed a VW whose charger had answered the first AuthorizationReq with
        /// <c>Ongoing_WaitingForCustomerInteraction</c>, so the car sent a second one. Ours answers
        /// <c>Finished</c> at once and is in ChargeParams by then — and used to close the connection without
        /// a word, which a car cannot distinguish from a dead station
        /// (<c>docs/interop-runs/2026-08-06-tux-head-reverse/</c>).
        /// </remarks>
        [Test]
        public void AuthorizationPolledTwice_GetsAnAnswerRatherThanSilence()
        {
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
            var sid = new byte[8];

            secc.Handle(Wrap(sid, new SessionSetupReqType(new byte[] { 1 })));
            secc.Handle(Wrap(sid, new ServiceDiscoveryReqType(null, null)));
            secc.Handle(Wrap(sid, new PaymentServiceSelectionReqType(PaymentOption.ExternalPayment,
                new SelectedServiceListType(new[] { new SelectedServiceType(1, null) }))));

            var first  = (AuthorizationResType)secc.Handle(Wrap(sid, new AuthorizationReqType(null, null))).Body.BodyElement!;
            var second = (AuthorizationResType)secc.Handle(Wrap(sid, new AuthorizationReqType(null, null))).Body.BodyElement!;

            Assert.Multiple(() =>
            {
                Assert.That(first.ResponseCode,   Is.EqualTo(ResponseCode.OK));
                Assert.That(second.ResponseCode,  Is.EqualTo(ResponseCode.FAILED_SequenceError));
                Assert.That(secc.SequenceErrorAt, Is.EqualTo("AuthorizationReq"));
            });
        }


        /// <summary>
        /// The refusal has to survive the wire, which is a stronger claim than "the object was built": these
        /// responses carry mandatory fields the success path fills from session state that a refused session
        /// never gathered, and an EXI encode is where an unfillable one would show up.
        /// </summary>
        [Test]
        public void EverySequenceErrorResponse_Encodes([Values(PowerMode.Ac, PowerMode.Dc)] PowerMode mode)
        {
            var evStatus = new DC_EVStatusType(EVReady: true, DC_EVErrorCode.NO_ERROR, EVRESSSOC: 50);

            // Each of these is sent in the SessionSetup phase, where none of them is legal.
            BodyBaseType[] outOfOrder =
            [
                new ServiceDiscoveryReqType(null, null),
                new ServiceDetailReqType(1),
                new PaymentServiceSelectionReqType(PaymentOption.ExternalPayment,
                    new SelectedServiceListType(new[] { new SelectedServiceType(1, null) })),
                new AuthorizationReqType(null, null),
                new ChargeParameterDiscoveryReqType(null, EnergyTransferMode.AC_three_phase_core,
                    new AC_EVChargeParameterType(null, PhysicalValue.Of(22_000, UnitSymbol.Wh),
                        new PhysicalValueType(0, UnitSymbol.V, 400), new PhysicalValueType(0, UnitSymbol.A, 32),
                        new PhysicalValueType(0, UnitSymbol.A, 6))),
                new CableCheckReqType(evStatus),
                new PreChargeReqType(evStatus, new PhysicalValueType(0, UnitSymbol.V, 400),
                                     new PhysicalValueType(0, UnitSymbol.A, 2)),
                new PowerDeliveryReqType(ChargeProgress.Start, 1, null, null),
                new CurrentDemandReqType(evStatus, new PhysicalValueType(0, UnitSymbol.A, 120),
                    null, null, null, null, false, null, null, new PhysicalValueType(0, UnitSymbol.V, 400)),
                new ChargingStatusReqType(),
                new WeldingDetectionReqType(evStatus),
            ];

            var buf = new byte[4096];

            foreach (var request in outOfOrder)
            {
                var secc  = new Secc2(mode, TimeSpan.FromSeconds(60), TimeProvider.System);
                var reply = secc.Handle(Wrap(new byte[8], request));
                var body  = reply.Body.BodyElement!;

                Assert.Multiple(() =>
                {
                    Assert.That(reply.TryEncode(buf, out var written), Is.True,
                                $"{request.GetType().Name}: its refusal must encode");
                    Assert.That(written, Is.GreaterThan(0));
                    Assert.That(body.GetType().Name,
                                Is.EqualTo(request.GetType().Name.Replace("ReqType", "ResType")),
                                "the refusal is the response that pairs with the request");
                    Assert.That(secc.IsDone, Is.True);
                });
            }
        }


        /// <summary>
        /// A PaymentDetailsReq out of order is refused like the rest — and the refusal must not hand out a
        /// usable GenChallenge, because a challenge is an invitation to sign and this message is the opposite.
        /// </summary>
        [Test]
        public void ASequenceErrorHandsOutNoChallenge()
        {
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);

            var res = (PaymentDetailsResType)secc.Handle(Wrap(new byte[8],
                          new PaymentDetailsReqType("DE-ABC-C1234",
                              new CertificateChainType(Id: null, Certificate: [1, 2, 3], SubCertificates: null)))).Body.BodyElement!;

            Assert.Multiple(() =>
            {
                Assert.That(res.ResponseCode, Is.EqualTo(ResponseCode.FAILED_SequenceError));
                Assert.That(res.GenChallenge, Is.EqualTo(new byte[16]), "no challenge in a refusal");
            });
        }


        /// <summary>
        /// Not everything can be refused in kind: a CertificateInstallationRes is a contract chain, an
        /// encrypted private key, a DH public key and an eMAID, and none of that can be fabricated to carry
        /// a response code. This station advertises no certificate service either, so the abort stands —
        /// pinned here so the exception stays a deliberate exception and not a forgotten arm.
        /// </summary>
        [Test]
        public void ACertificateRequestStillAborts()
        {
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);

            Assert.That(() => secc.Handle(Wrap(new byte[8],
                            new CertificateInstallationReqType("id1", OEMProvisioningCert: [1, 2, 3],
                                new ListOfRootCertificateIDsType([new X509IssuerSerialType("CN=V2G Root", 1)])))),
                Throws.InstanceOf<SessionAborted>().With.Message.Contain("sequence guard"));
        }
    }
}
