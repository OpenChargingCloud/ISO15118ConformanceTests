using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using Vanaheimr.V2G.Iso15118_20.CommonMessages;
using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.Tests.StateMachines
{
    /// <summary>
    /// Direct (no-socket) tests for the SECC's live Plug &amp; Charge path: it offers PnC + a GenChallenge in
    /// AuthorizationSetupRes and validates a signed PnC AuthorizationReq (challenge echo, reference digest, and
    /// the ECDSA signature over the contract leaf). The contract key here is <b>P-256</b> — matching a real
    /// Josev PKI, not the -20-nominal secp521r1 — with the signature/digest hashes read from the message URIs.
    /// </summary>
    [TestFixture]
    public class Secc20PnCTests
    {
        private static MessageHeaderType Hdr(SignatureType? sig = null) => new(new byte[8], 1_700_000_000UL, sig);

        [Test]
        public void AuthorizationSetup_OffersPnCWithGenChallenge()
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionSetupReq(Hdr(), "EVCC01"));

            var res = (AuthorizationSetupRes)secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Hdr())).Response;

            Assert.That(res.AuthorizationServices, Does.Contain(Authorization.PnC));
            Assert.That(res.PnC_ASResAuthorizationMode, Is.Not.Null);
            Assert.That(res.PnC_ASResAuthorizationMode!.GenChallenge.Length, Is.EqualTo(16));
        }

        [Test]
        public void SignedPnCAuthorizationReq_WithP256Contract_VerifiesEndToEnd()
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionSetupReq(Hdr(), "EVCC01"));
            var setup = (AuthorizationSetupRes)secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Hdr())).Response;
            byte[] challenge = setup.PnC_ASResAuthorizationMode!.GenChallenge;

            // A P-256 contract certificate + key (as a real Josev PKI issues), and a PnC mode echoing the challenge.
            using var contractKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var certReq = new CertificateRequest("CN=TestContract", contractKey, HashAlgorithmName.SHA256);
            using var contract = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

            var pncMode = new PnC_AReqAuthorizationModeType("authId", challenge,
                new ContractCertificateChainType(contract.RawData, new SubCertificatesType(new[] { contract.RawData })));

            // Sign it exactly as an EV would: SHA-512 over the PnC_AReqAuthorizationMode fragment → SignedInfo → ECDSA.
            var buf = new byte[8192];
            Assert.That(CommonMessagesCodec.EncodeFragment_PnC_AReqAuthorizationMode(pncMode, buf, out int n), Is.True);
            var signedInfo = V2GSignature.BuildSignedInfo("authId", V2GSignature.Digest(buf.AsSpan(0, n)));
            var signature = V2GSignature.BuildSignature(signedInfo, V2GSignature.Sign(signedInfo, contractKey));

            var authReq = new AuthorizationReq(Hdr(signature), Authorization.PnC, null, pncMode);
            secc.Handle(MessageSet.Iso20CommonMessages, authReq);

            Assert.That(secc.PnCAuth, Is.Not.Null);
            Assert.That(secc.PnCAuth!.ChallengeOk, Is.True, "GenChallenge must echo");
            Assert.That(secc.PnCAuth.DigestOk, Is.True, "reference digest must match the signed element");
            Assert.That(secc.PnCAuth.SignatureOk, Is.True, "ECDSA signature over the contract leaf must verify");
        }

        [Test]
        public void PnCAuthorizationReq_WithWrongChallenge_FlaggedButSessionContinues()
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionSetupReq(Hdr(), "EVCC01"));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Hdr()));

            using var contractKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var certReq = new CertificateRequest("CN=TestContract", contractKey, HashAlgorithmName.SHA256);
            using var contract = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

            var pncMode = new PnC_AReqAuthorizationModeType("authId", new byte[16], // all-zero: not our challenge
                new ContractCertificateChainType(contract.RawData, new SubCertificatesType(new[] { contract.RawData })));
            var buf = new byte[8192];
            CommonMessagesCodec.EncodeFragment_PnC_AReqAuthorizationMode(pncMode, buf, out int n);
            var signedInfo = V2GSignature.BuildSignedInfo("authId", V2GSignature.Digest(buf.AsSpan(0, n)));
            var signature = V2GSignature.BuildSignature(signedInfo, V2GSignature.Sign(signedInfo, contractKey));

            var res = (AuthorizationRes)secc.Handle(MessageSet.Iso20CommonMessages,
                new AuthorizationReq(Hdr(signature), Authorization.PnC, null, pncMode)).Response;

            // The signature itself is valid (self-consistent), but the challenge does not match ours.
            Assert.That(secc.PnCAuth!.SignatureOk, Is.True);
            Assert.That(secc.PnCAuth.ChallengeOk, Is.False);
            Assert.That(res.ResponseCode, Is.EqualTo(ResponseCode.OK)); // recorded, not aborted
        }
    }
}
