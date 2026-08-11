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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118_20.CommonMessages;
using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

using X = cloud.charging.open.protocols.ISO15118_20.XMLDSig.Generated;

namespace ISO15118ConformanceTests.Simulation.StateMachines
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
        /// <summary>
        /// The SessionID this fixture's station is pinned to, and that every request here carries.
        /// </summary>
        /// <remarks>
        /// It used to be <c>new byte[8]</c> — the all-zero id ISO reserves for *"I have no session"*, which
        /// this fixture was therefore sending in every request for its whole existence, and which the
        /// station happily served because it had no <c>[V2G20-460]</c> check. Pinned rather than read back
        /// from the station because the signatures below are made under a fixed timestamp and want a fixed
        /// header; the point is only that it is a real id, not that it is this one.
        /// </remarks>
        private static readonly byte[] Sid = [0x0a, 0x1b, 0x2c, 0x3d, 0x4e, 0x5f, 0x60, 0x71];

        private static Secc20Dc Station() =>
            new(TimeSpan.FromSeconds(60), TimeProvider.System) { FixedSessionId = Sid };

        private static MessageHeaderType Hdr(SignatureType? sig = null) => new(Sid, 1_700_000_000UL, sig);

        [Test]
        public void AuthorizationSetup_OffersPnCWithGenChallenge()
        {
            var secc = Station();
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionSetupReq(Hdr(), "EVCC01"));

            var res = (AuthorizationSetupRes)secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Hdr())).Response;

            Assert.That(res.AuthorizationServices, Does.Contain(Authorization.PnC));
            Assert.That(res.PnC_ASResAuthorizationMode, Is.Not.Null);
            Assert.That(res.PnC_ASResAuthorizationMode!.GenChallenge.Length, Is.EqualTo(16));
        }

        [Test]
        public void SignedPnCAuthorizationReq_WithP256Contract_VerifiesEndToEnd()
        {
            var secc = Station();
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
            Assert.That(secc.PnCAuth.SignatureGrammar, Is.EqualTo("iso20-commonmessages"),
                "our own EVCC signs the SignedInfo over the combined CommonMessages grammar");
        }

        /// <summary>
        /// Interop: a PnC <c>AuthorizationReq</c> whose <c>SignedInfo</c> was signed over the <b>standalone
        /// xmldsig grammar</b> (as Josev's stack does) still verifies — the SECC falls back to that grammar and
        /// reports <c>SignatureGrammar == "xmldsig-standalone"</c>. This is the offline analogue of the live
        /// Josev PnC run (docs/interop-runs/2026-07-21-iso20-dc-pnc-tls/): our combined-grammar encoding does
        /// not match, so the first verify fails and the standalone-xmldsig fallback succeeds.
        /// </summary>
        [Test]
        public void SignedPnCAuthorizationReq_OverStandaloneXmldsigGrammar_VerifiesViaFallback()
        {
            var secc = Station();
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionSetupReq(Hdr(), "EVCC01"));
            var setup = (AuthorizationSetupRes)secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Hdr())).Response;
            byte[] challenge = setup.PnC_ASResAuthorizationMode!.GenChallenge;

            using var contractKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var certReq = new CertificateRequest("CN=JosevStyleContract", contractKey, HashAlgorithmName.SHA256);
            using var contract = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

            var pncMode = new PnC_AReqAuthorizationModeType("authId", challenge,
                new ContractCertificateChainType(contract.RawData, new SubCertificatesType(new[] { contract.RawData })));

            var buf = new byte[8192];
            Assert.That(CommonMessagesCodec.EncodeFragment_PnC_AReqAuthorizationMode(pncMode, buf, out int n), Is.True);
            var digest = V2GSignature.Digest(buf.AsSpan(0, n));
            var signedInfo = V2GSignature.BuildSignedInfo("authId", digest); // SHA-512 / ecdsa-sha512

            // Sign the SignedInfo as Josev would: over the STANDALONE xmldsig grammar, not our combined one.
            var xsi = new X.SignedInfoType(
                signedInfo.Id,
                new X.CanonicalizationMethodType(signedInfo.CanonicalizationMethod.Algorithm, signedInfo.CanonicalizationMethod.ANY),
                new X.SignatureMethodType(signedInfo.SignatureMethod.Algorithm, signedInfo.SignatureMethod.HMACOutputLength, signedInfo.SignatureMethod.ANY),
                new[] { new X.ReferenceType(null, null, "#authId", null, new X.DigestMethodType(signedInfo.Reference[0].DigestMethod.Algorithm, null), digest) });
            var xbuf = new byte[512];
            Assert.That(X.XmlDsigCodec.EncodeFragment_SignedInfo(xsi, xbuf, out int xn), Is.True);
            var rawSig = contractKey.SignData(xbuf.AsSpan(0, xn), HashAlgorithmName.SHA512, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            var signature = V2GSignature.BuildSignature(signedInfo, rawSig);

            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationReq(Hdr(signature), Authorization.PnC, null, pncMode));

            Assert.That(secc.PnCAuth!.DigestOk, Is.True);
            Assert.That(secc.PnCAuth.SignatureOk, Is.True, "signature over the standalone xmldsig grammar must verify via fallback");
            Assert.That(secc.PnCAuth.SignatureGrammar, Is.EqualTo("xmldsig-standalone"));
        }

        [Test]
        public void PnCAuthorizationReq_WithWrongChallenge_FlaggedButSessionContinues()
        {
            var secc = Station();
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
