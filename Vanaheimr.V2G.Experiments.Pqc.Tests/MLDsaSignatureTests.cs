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

using NUnit.Framework;

using Vanaheimr.V2G.Experiments.Pqc;
using cloud.charging.open.protocols.ISO15118_20.CommonMessages;
using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;

namespace Vanaheimr.V2G.Experiments.Pqc.Tests
{
    /// <summary>
    /// EXPERIMENT (wire-non-conformant, self-consistent only — no external implementation signs or
    /// verifies ML-DSA 15118 messages): the ML-DSA-87 signature suite over the -20 SignedInfo, driven
    /// through the REAL generated EXI codec — a full PnC AuthorizationReq with a 4 627-byte signature
    /// encodes, decodes and verifies without any codec change (byte-array values are unbounded).
    /// </summary>
    [TestFixture]
    public class MLDsaSignatureTests
    {
        private static (AuthorizationReq Message, Org.BouncyCastle.Crypto.Parameters.MLDsaPublicKeyParameters PublicKey) BuildSigned()
        {
            using var contractKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var contract = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                    "CN=MLDSA-EXPERIMENT", contractKey, HashAlgorithmName.SHA256)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

            var pncMode = new PnC_AReqAuthorizationModeType("id1", RandomNumberGenerator.GetBytes(16),
                new ContractCertificateChainType(contract.RawData, new SubCertificatesType(new[] { contract.RawData })));

            var fragment = new byte[8192];
            Assert.That(CommonMessagesCodec.EncodeFragment_PnC_AReqAuthorizationMode(pncMode, fragment, out int n), Is.True);

            var (priv, pub) = MLDsaV2GSignature.GenerateKeyPair();
            var signedInfo  = V2GSignature.BuildSignedInfo("id1",
                                  V2GSignature.Digest(fragment.AsSpan(0, n)),
                                  MLDsaV2GSignature.MlDsa87Experimental);
            var signature   = V2GSignature.BuildSignature(signedInfo, MLDsaV2GSignature.Sign(signedInfo, priv));

            var header = new MessageHeaderType(RandomNumberGenerator.GetBytes(8),
                                               (ulong) DateTimeOffset.UtcNow.ToUnixTimeSeconds(), signature);
            return (new AuthorizationReq(header, Authorization.PnC, null, pncMode), pub);
        }

        [Test]
        public void SignedAuthorizationReq_WithMlDsa87_RoundtripsThroughTheRealCodec_AndVerifies()
        {
            var (message, publicKey) = BuildSigned();

            Assert.That(message.Header.Signature!.SignatureValue.Value,
                Has.Length.EqualTo(MLDsaV2GSignature.SignatureSize), "FIPS 204 fixes ML-DSA-87 at 4 627 bytes");

            // The generated codec carries the PQC-sized signature unchanged: encode → decode byte-exact.
            var buf = new byte[65536];
            Assert.That(message.TryEncode(buf, out int length), Is.True, "EXI encode with a 4.6-KB signature");
            var decoded = CommonMessagesCodec.DecodeAny(buf.AsSpan(0, length), out int consumed);
            Assert.That(consumed, Is.EqualTo(length));

            var reply = (AuthorizationReq) decoded;
            var sig   = reply.Header.Signature!;
            Assert.Multiple(() =>
            {
                Assert.That(sig.SignedInfo.SignatureMethod.Algorithm, Is.EqualTo(MLDsaV2GSignature.MlDsa87Experimental));
                // Reference digest over the re-encoded fragment (unchanged SHA-512 path) …
                var fragment = new byte[8192];
                Assert.That(CommonMessagesCodec.EncodeFragment_PnC_AReqAuthorizationMode(
                    reply.PnC_AReqAuthorizationMode!, fragment, out int n), Is.True);
                Assert.That(V2GSignature.VerifyReference(sig.SignedInfo.Reference[0], fragment.AsSpan(0, n)), Is.True);
                // … and the ML-DSA-87 signature over the decoded SignedInfo.
                Assert.That(MLDsaV2GSignature.Verify(sig.SignedInfo, sig.SignatureValue.Value, publicKey), Is.True);
            });
        }

        [Test]
        public void TamperedSignatureValue_FailsVerification()
        {
            var (message, publicKey) = BuildSigned();
            var sig = message.Header.Signature!;

            var tampered = (byte[]) sig.SignatureValue.Value.Clone();
            tampered[100] ^= 0x01;

            Assert.Multiple(() =>
            {
                Assert.That(MLDsaV2GSignature.Verify(sig.SignedInfo, sig.SignatureValue.Value, publicKey), Is.True);
                Assert.That(MLDsaV2GSignature.Verify(sig.SignedInfo, tampered, publicKey), Is.False);
                Assert.That(MLDsaV2GSignature.Verify(sig.SignedInfo, sig.SignatureValue.Value,
                    MLDsaV2GSignature.GenerateKeyPair().Public), Is.False, "wrong key must fail");
            });
        }
    }
}
