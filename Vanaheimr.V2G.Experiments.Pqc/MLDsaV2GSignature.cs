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

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

using cloud.charging.open.protocols.ISO15118_20.CommonMessages;
using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;

namespace Vanaheimr.V2G.Experiments.Pqc
{
    /// <summary>
    /// <b>EXPERIMENT — wire-NON-conformant.</b> An ML-DSA-87 (FIPS 204, "Dilithium") signature suite
    /// for the ISO 15118-20 <see cref="SignedInfoType"/>, mirroring <see cref="V2GSignature"/>'s
    /// Ed448 path: ML-DSA is a "pure" scheme, so it signs the SignedInfo EXI-fragment octets
    /// directly (no external pre-hash); the reference digests inside the SignedInfo stay SHA-512,
    /// so <see cref="V2GSignature.Digest"/>/<see cref="V2GSignature.VerifyReference"/> work
    /// unchanged. The <see cref="SignatureValue"/> is the raw 4 627-byte ML-DSA-87 signature — the
    /// generated EXI codec carries it without modification (base64Binary values are unbounded).
    ///
    /// <para>There is no standardized XMLDSig algorithm URI for ML-DSA yet (as of this experiment),
    /// hence the explicitly experimental URN in <see cref="MlDsa87Experimental"/> — no conformant
    /// peer will ever accept it, which is the point: this measures feasibility and size, nothing
    /// interops. See <c>docs/experiments/pqc.md</c> for the numbers.</para>
    /// </summary>
    public static class MLDsaV2GSignature
    {
        /// <summary>Deliberately non-standard signature-method URI — flags the suite as ours-only.</summary>
        public const string MlDsa87Experimental = "urn:vanaheimr:v2g:experimental:xmldsig:ml-dsa-87";

        /// <summary>ML-DSA-87 signature size (FIPS 204): fixed 4 627 bytes — 35× the 132-byte
        /// P-521 <c>r‖s</c> and 40× the 114-byte Ed448 signature.</summary>
        public const int SignatureSize = 4627;

        /// <summary>ML-DSA-87 public-key size (FIPS 204): 2 592 bytes (vs 133 for an uncompressed
        /// P-521 point) — the number that makes PQC <em>certificate chains</em> balloon.</summary>
        public const int PublicKeySize = 2592;

        public static (MLDsaPrivateKeyParameters Private, MLDsaPublicKeyParameters Public) GenerateKeyPair(SecureRandom? random = null)
        {
            var generator = new MLDsaKeyPairGenerator();
            generator.Init(new MLDsaKeyGenerationParameters(random ?? new SecureRandom(), MLDsaParameters.ml_dsa_87));
            var pair = generator.GenerateKeyPair();
            return ((MLDsaPrivateKeyParameters) pair.Private, (MLDsaPublicKeyParameters) pair.Public);
        }

        /// <summary>Signs a <see cref="SignedInfoType"/> (built via
        /// <c>V2GSignature.BuildSignedInfo(id, digest, MlDsa87Experimental)</c>) over its EXI fragment.</summary>
        public static byte[] Sign(SignedInfoType signedInfo, MLDsaPrivateKeyParameters privateKey)
        {
            var signer = new MLDsaSigner(MLDsaParameters.ml_dsa_87, deterministic: false);
            signer.Init(forSigning: true, privateKey);
            var message = V2GSignature.SignedInfoFragment(signedInfo);
            signer.BlockUpdate(message, 0, message.Length);
            return signer.GenerateSignature();
        }

        /// <summary>Verifies a raw ML-DSA-87 <c>SignatureValue</c> against a SignedInfo and public key.
        /// Reference digests are the caller's job, exactly like <see cref="V2GSignature.Verify"/>.</summary>
        public static bool Verify(SignedInfoType signedInfo, byte[] signatureValue, MLDsaPublicKeyParameters publicKey)
        {
            var signer = new MLDsaSigner(MLDsaParameters.ml_dsa_87, deterministic: false);
            signer.Init(forSigning: false, publicKey);
            var message = V2GSignature.SignedInfoFragment(signedInfo);
            signer.BlockUpdate(message, 0, message.Length);
            return signer.VerifySignature(signatureValue);
        }
    }
}
