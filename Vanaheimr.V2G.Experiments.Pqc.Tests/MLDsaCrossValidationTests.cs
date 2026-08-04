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

using Org.BouncyCastle.Crypto.Parameters;

using Vanaheimr.V2G.Experiments.Pqc;
using cloud.charging.open.protocols.ISO15118_20.CommonMessages;
using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;

namespace Vanaheimr.V2G.Experiments.Pqc.Tests
{
    /// <summary>
    /// EXPERIMENT — the ML-DSA suite's <b>internal oracle</b>: BouncyCastle 2.6.2 and .NET 10's native
    /// <see cref="MLDsa"/> (SYSLIB5006-experimental, OS crypto backed) are two independent FIPS 204
    /// implementations. Signatures produced by one over the real SignedInfo EXI fragment must verify
    /// under the other, in BOTH directions, with raw FIPS-204 key exchange between them
    /// (2 592-byte public key). This is the same two-independent-implementations pattern the codec
    /// uses with cbV2G vs EXIficient — just for the PQC primitive instead of the EXI bytes.
    /// </summary>
    [TestFixture]
    public class MLDsaCrossValidationTests
    {
        private static readonly byte[] EmptyContext = [];

        /// <summary>A realistic SignedInfo (SHA-512 digest of a random "fragment") + its signing octets.</summary>
        private static (SignedInfoType SignedInfo, byte[] Message) BuildSignedInfo()
        {
            var digest = V2GSignature.Digest(RandomNumberGenerator.GetBytes(64));
            var signedInfo = V2GSignature.BuildSignedInfo("id1", digest, MLDsaV2GSignature.MlDsa87Experimental);
            return (signedInfo, V2GSignature.SignedInfoFragment(signedInfo));
        }

        [SetUp]
        public void RequireDotNetMlDsa()
        {
            if (!MLDsa.IsSupported)
                Assert.Ignore(".NET 10 MLDsa is not supported by this OS crypto backend — " +
                              "cross-validation needs both implementations.");
        }

        [Test]
        public void BouncyCastleSigns_DotNetVerifies()
        {
            var (signedInfo, message) = BuildSignedInfo();
            var (bcPrivate, bcPublic) = MLDsaV2GSignature.GenerateKeyPair();
            var signature = MLDsaV2GSignature.Sign(signedInfo, bcPrivate);

            using var dotnet = MLDsa.ImportMLDsaPublicKey(MLDsaAlgorithm.MLDsa87, bcPublic.GetEncoded());

            Assert.Multiple(() =>
            {
                Assert.That(dotnet.VerifyData(message, signature, EmptyContext), Is.True,
                    "a BouncyCastle ML-DSA-87 signature must verify under .NET's independent implementation");

                var tampered = (byte[]) signature.Clone();
                tampered[7] ^= 0x01;
                Assert.That(dotnet.VerifyData(message, tampered, EmptyContext), Is.False);
            });
        }

        [Test]
        public void DotNetSigns_BouncyCastleVerifies()
        {
            var (signedInfo, message) = BuildSignedInfo();
            using var dotnet = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa87);
            var signature = dotnet.SignData(message, EmptyContext);

            var bcPublic = MLDsaPublicKeyParameters.FromEncoding(MLDsaParameters.ml_dsa_87,
                                                                 dotnet.ExportMLDsaPublicKey());

            Assert.Multiple(() =>
            {
                Assert.That(signature, Has.Length.EqualTo(MLDsaV2GSignature.SignatureSize),
                    "both implementations must agree on the FIPS 204 signature size");
                Assert.That(MLDsaV2GSignature.Verify(signedInfo, signature, bcPublic), Is.True,
                    "a .NET ML-DSA-87 signature must verify under BouncyCastle's independent implementation");
            });
        }

        [Test]
        public void RawKeyMaterial_RoundTripsBetweenImplementations()
        {
            // The FIPS 204 raw encodings are the interchange format: BC-generated keys imported into
            // .NET must export byte-identically, closing the loop on key-format agreement.
            var (_, bcPublic) = MLDsaV2GSignature.GenerateKeyPair();
            var raw = bcPublic.GetEncoded();
            Assert.That(raw, Has.Length.EqualTo(MLDsaV2GSignature.PublicKeySize));

            using var dotnet = MLDsa.ImportMLDsaPublicKey(MLDsaAlgorithm.MLDsa87, raw);
            Assert.That(dotnet.ExportMLDsaPublicKey(), Is.EqualTo(raw));
        }
    }
}
