using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using Vanaheimr.V2G.Iso15118_2;
using Vanaheimr.V2G.Iso15118_2.Generated;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;

namespace Vanaheimr.V2G.Simulation.Tests.StateMachines
{
    /// <summary>
    /// The -2 Plug &amp; Charge SECC path, direct (no socket): Contract payment inserts PaymentDetails
    /// (contract chain in → GenChallenge out), the AuthorizationReq must be signed (challenge echo +
    /// body-fragment digest + ECDSA — verified under the combined -2 grammar or the Josev
    /// standalone-xmldsig form), and the charging loop demands + verifies signed MeteringReceiptReqs.
    /// </summary>
    [TestFixture]
    public class Secc2PnCTests
    {
        private static V2G_Message Msg(BodyBaseType body, SignatureType? sig = null) =>
            new(new MessageHeaderType(new byte[8], Notification: null, Signature: sig), new BodyType(body));

        private static (ECDsa Key, X509Certificate2 Cert) NewContract()
        {
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var cert = new CertificateRequest("CN=UKTEST000000001A", key, HashAlgorithmName.SHA256)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
            return (key, cert);
        }

        private static Secc2 RunToPaymentDetails(X509Certificate2 cert, out byte[] challenge)
        {
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
            secc.Handle(Msg(new SessionSetupReqType(new byte[] { 1, 2, 3, 4, 5, 6 })));
            var disc = (ServiceDiscoveryResType)secc.Handle(Msg(new ServiceDiscoveryReqType(null, null))).Body.BodyElement!;
            Assert.That(disc.PaymentOptionList.PaymentOption, Does.Contain(PaymentOption.Contract), "Contract must be offered");

            secc.Handle(Msg(new PaymentServiceSelectionReqType(PaymentOption.Contract,
                new SelectedServiceListType(new[] { new SelectedServiceType(1, null) }))));

            var details = (PaymentDetailsResType)secc.Handle(Msg(new PaymentDetailsReqType("UKTEST000000001A",
                new CertificateChainType(null, cert.RawData, new SubCertificatesType(new[] { cert.RawData }))))).Body.BodyElement!;
            Assert.That(details.GenChallenge, Has.Length.EqualTo(16));
            challenge = details.GenChallenge;
            return secc;
        }

        [Test]
        public void ContractSession_SignedAuthorizationReq_VerifiesJosevForm()
        {
            var (key, cert) = NewContract();
            using (key) using (cert)
            {
                var secc = RunToPaymentDetails(cert, out var challenge);

                var authReq = new AuthorizationReqType("id1", challenge);
                var frag = new byte[1024];
                Assert.That(Iso2Codec.EncodeFragment_AuthorizationReq(authReq, frag, out int n), Is.True);
                var signature = XmlDsigInterop2.Sign("id1", frag.AsSpan(0, n), key);

                secc.Handle(Msg(authReq, signature));

                Assert.Multiple(() =>
                {
                    Assert.That(secc.PnCAuth, Is.Not.Null);
                    Assert.That(secc.PnCAuth!.ChallengeOk, Is.True, "GenChallenge must echo");
                    Assert.That(secc.PnCAuth.DigestOk, Is.True, "body-fragment digest must match");
                    Assert.That(secc.PnCAuth.SignatureOk, Is.True, "ECDSA signature must verify");
                    Assert.That(secc.PnCAuth.SignatureGrammar, Is.EqualTo("xmldsig-standalone"));
                    Assert.That(secc.PnCAuth.ContractSubject, Does.Contain("UKTEST000000001A"));
                });
            }
        }

        [Test]
        public void ContractSession_ChargingLoop_DemandsAndVerifiesSignedMeteringReceipt()
        {
            var (key, cert) = NewContract();
            using (key) using (cert)
            {
                var secc = RunToPaymentDetails(cert, out var challenge);

                var authReq = new AuthorizationReqType("id1", challenge);
                var frag = new byte[1024];
                Iso2Codec.EncodeFragment_AuthorizationReq(authReq, frag, out int n);
                secc.Handle(Msg(authReq, XmlDsigInterop2.Sign("id1", frag.AsSpan(0, n), key)));

                secc.Handle(Msg(new ChargeParameterDiscoveryReqType(null, EnergyTransferMode.AC_three_phase_core,
                    new AC_EVChargeParameterType(null, PhysicalValue.Of(22_000, UnitSymbol.Wh),
                        new PhysicalValueType(0, UnitSymbol.V, 400), new PhysicalValueType(0, UnitSymbol.A, 32),
                        new PhysicalValueType(0, UnitSymbol.A, 6)))));
                secc.Handle(Msg(new PowerDeliveryReqType(ChargeProgress.Start, 1, null, null)));

                var status = (ChargingStatusResType)secc.Handle(Msg(new ChargingStatusReqType())).Body.BodyElement!;
                Assert.That(status.ReceiptRequired, Is.True, "a Contract session must demand a receipt");
                Assert.That(status.MeterInfo, Is.Not.Null, "the EV echoes this MeterInfo in its receipt");

                var receipt = new MeteringReceiptReqType("id2", new byte[8], status.SAScheduleTupleID, status.MeterInfo!);
                var rfrag = new byte[1024];
                Assert.That(Iso2Codec.EncodeFragment_MeteringReceiptReq(receipt, rfrag, out int rn), Is.True);
                var res = (MeteringReceiptResType)secc.Handle(Msg(receipt, XmlDsigInterop2.Sign("id2", rfrag.AsSpan(0, rn), key))).Body.BodyElement!;

                Assert.Multiple(() =>
                {
                    Assert.That(res.ResponseCode, Is.EqualTo(ResponseCode.OK));
                    Assert.That(secc.MeteringReceipts, Has.Count.EqualTo(1));
                    Assert.That(secc.MeteringReceipts[0].DigestOk, Is.True);
                    Assert.That(secc.MeteringReceipts[0].SignatureOk, Is.True);
                    Assert.That(secc.MeteringReceipts[0].SignatureGrammar, Is.EqualTo("xmldsig-standalone"));
                });

                // The loop continues normally after the receipt.
                var again = secc.Handle(Msg(new ChargingStatusReqType()));
                Assert.That(again.Body.BodyElement, Is.InstanceOf<ChargingStatusResType>());
            }
        }

        [Test]
        public void EimSession_IsUntouched_NoPaymentDetailsNoReceipts()
        {
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
            secc.Handle(Msg(new SessionSetupReqType(new byte[] { 1, 2, 3, 4, 5, 6 })));
            secc.Handle(Msg(new ServiceDiscoveryReqType(null, null)));
            secc.Handle(Msg(new PaymentServiceSelectionReqType(PaymentOption.ExternalPayment,
                new SelectedServiceListType(new[] { new SelectedServiceType(1, null) }))));
            secc.Handle(Msg(new AuthorizationReqType(null, null)));   // straight to Authorization, unsigned

            secc.Handle(Msg(new ChargeParameterDiscoveryReqType(null, EnergyTransferMode.AC_three_phase_core,
                new AC_EVChargeParameterType(null, PhysicalValue.Of(22_000, UnitSymbol.Wh),
                    new PhysicalValueType(0, UnitSymbol.V, 400), new PhysicalValueType(0, UnitSymbol.A, 32),
                    new PhysicalValueType(0, UnitSymbol.A, 6)))));
            secc.Handle(Msg(new PowerDeliveryReqType(ChargeProgress.Start, 1, null, null)));
            var status = (ChargingStatusResType)secc.Handle(Msg(new ChargingStatusReqType())).Body.BodyElement!;

            Assert.Multiple(() =>
            {
                Assert.That(secc.PnCAuth, Is.Null);
                Assert.That(status.ReceiptRequired, Is.Null, "EIM sessions must not demand receipts");
                Assert.That(status.MeterInfo, Is.Null);
            });
        }
    }
}
