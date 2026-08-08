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

using cloud.charging.open.protocols.ISO15118_20.CommonMessages;
using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;

namespace ISO15118ConformanceTests.Simulation.Interop
{

    /// <summary>
    /// <b>Where the 94-byte <c>SECP521_EncryptedPrivateKey</c> facet is enforced — and where it is not.</b>
    ///
    /// <para>
    /// <c>V2G_CI_CommonMessages.xsd</c> declares <c>secp521_EncryptedPrivateKeyType</c> as
    /// <c>xs:base64Binary</c> with <c>xs:length value="94"</c> — <i>exact</i>, not a maximum. 94 is not an
    /// arbitrary number: it is the AES-GCM shape of the -20 contract-key transport, IV 12 ‖ ciphertext 66
    /// (the raw secp521r1 scalar) ‖ tag 16. <see cref="ContractProvisioning"/> builds precisely that.
    /// </para>
    ///
    /// <para>
    /// <b>Why this file exists.</b> ChargePoint's <c>wireshark-v2g</c> dissector carries a patch against
    /// <c>EVerest/libcbv2g</c> — <c>libcbv2g-fix-iso20-secp521-buffer-size.patch</c>, 2025-06-25 — that
    /// raises <c>iso20_secp521_EncryptedPrivateKeyType_BYTES_SIZE</c> from 94 to 128, because "the secp521
    /// encrypted private key can be up to 100 bytes when encoded […] causing decode failures during
    /// CertificateInstallationRes processing". It applies to libcbv2g at
    /// <c>03350be048b35b179905129005a97144a4bdcf93</c> — the exact commit our own reference harness pins
    /// (<c>WWCP_ISO15118/tools/cbv2g-ref/CMakeLists.txt</c>), so it is a statement about our oracle.
    /// </para>
    ///
    /// <para>
    /// Read against the schema, the patch points the other way: 94 is right, and whatever peer emitted 100
    /// bytes was non-conformant. (12 + 72 + 16 = 100 would be a scalar padded from 66 to 72 — a guess, and
    /// only that.) So we do <b>not</b> follow it. What matters for us is that the disagreement is now
    /// pinned on both sides of the layer boundary:
    /// </para>
    ///
    /// <list type="bullet">
    ///   <item><b>The codec stays lenient.</b> The source generator recognises <c>xs:length</c> and
    ///         deliberately ignores it (<c>Xsd/XsdReader.cs</c>: length facets constrain the value space
    ///         but not the EXI encoding — binary is length-prefixed either way). An overlong key therefore
    ///         decodes intact rather than throwing somewhere deep in the bit reader. For a harness whose
    ///         job is to <i>observe</i> other stacks that is the behaviour we want.</item>
    ///   <item><b>The crypto layer is strict.</b> <see cref="ContractProvisioning.RecoverContractKey"/>
    ///         rejects anything but 94 bytes, by name and with the expected length in the message. That is
    ///         the boundary where a wrong length actually means something.</item>
    /// </list>
    ///
    /// <para>
    /// The interop consequence, and the reason this sits under <c>Interop/</c>: a cbV2G-based peer
    /// (EVerest, tux-evse) <i>fails to decode the frame at all</i> where we hand the caller a value and let
    /// the crypto layer refuse it. Neither is wrong, but a divergence in where a violation surfaces is
    /// exactly the kind of thing that reads as "your message is corrupt" in someone else's log.
    /// </para>
    /// </summary>
    [TestFixture]
    public class Secp521PrivateKeyFacetTests
    {

        /// <summary><c>xs:length value="94"</c> — exact, per V2G_CI_CommonMessages.xsd.</summary>
        private const int SchemaLength    = 94;

        /// <summary>The length ChargePoint's patch was written to accommodate.</summary>
        private const int OverlongLength  = 100;


        /// <summary>
        /// 94 is the AES-GCM shape, not a magic number — and our SECC emits exactly it.
        /// </summary>
        [Test]
        public void TheSchemaLengthIsTheAesGcmShape()
        {
            const int iv = 12, scalar = 66, tag = 16;
            Assert.That(iv + scalar + tag, Is.EqualTo(SchemaLength),
                "IV 12 ‖ ciphertext 66 (secp521r1 scalar) ‖ GCM tag 16");

            using var oemKey      = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP521);
            using var contractKey = ECDsa.Create(ECCurve.NamedCurves.nistP521);

            var (_, wrapped) = ContractProvisioning.EncryptContractKey(oemKey.PublicKey, contractKey);
            Assert.That(wrapped, Has.Length.EqualTo(SchemaLength),
                "we must never be the peer that emits an overlong key");
        }


        /// <summary>
        /// The codec carries an overlong key through unchanged, in both directions. This pins the
        /// deliberate leniency: if the generator ever grows facet validation, this test is the place
        /// where that becomes a decision rather than a surprise.
        /// </summary>
        [Test]
        public void TheCodecCarriesAnOverlongKeyUnchanged()
        {
            var conformant = Fill(SchemaLength);
            var overlong   = Fill(OverlongLength);

            var (conformantBytes, conformantBack) = RoundTrip(conformant);
            var (overlongBytes,   overlongBack)   = RoundTrip(overlong);

            Assert.Multiple(() =>
            {
                Assert.That(conformantBack, Is.EqualTo(conformant), "the conformant key must survive");
                Assert.That(overlongBack,   Is.EqualTo(overlong),
                    "an overlong key decodes intact — no truncation, no throw");

                // EXI length-prefixes binary, so the extra six octets are simply on the wire. That is why
                // the value is representable at all, and why a fixed 94-byte destination buffer — cbV2G's,
                // before ChargePoint's patch — is what turns it into a decode failure over there.
                Assert.That(overlongBytes.Length - conformantBytes.Length,
                    Is.EqualTo(OverlongLength - SchemaLength),
                    "the six extra octets are carried literally, not re-encoded");
            });
        }


        /// <summary>
        /// The crypto layer is where the facet is actually enforced — and it names the expected length,
        /// so an operator reading the exception learns what was wrong rather than that "decoding failed".
        /// </summary>
        [Test]
        public void TheCryptoLayerRejectsAnOverlongKey()
        {
            using var oemKey      = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP521);
            using var contractKey = ECDsa.Create(ECCurve.NamedCurves.nistP521);

            var (dhPublicKey, wrapped) = ContractProvisioning.EncryptContractKey(oemKey.PublicKey, contractKey);

            // A genuine 94-byte wrapping, padded out to the length ChargePoint's peer produced.
            var overlong = new byte[OverlongLength];
            wrapped.CopyTo(overlong, 0);

            var ex = Assert.Throws<CryptographicException>(
                () => ContractProvisioning.RecoverContractKey(oemKey, dhPublicKey, overlong));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Message, Does.Contain("SECP521_EncryptedPrivateKey"));
                Assert.That(ex.Message, Does.Contain(SchemaLength.ToString()),
                    "the message must say what length was expected");
            });

            // And the unpadded original is still accepted, so the rejection is about the length alone.
            Assert.DoesNotThrow(() => ContractProvisioning.RecoverContractKey(oemKey, dhPublicKey, wrapped).Dispose());
        }


        #region (private) Fill(length), RoundTrip(key)

        private static byte[] Fill(int length)
        {
            var bytes = new byte[length];
            for (var i = 0; i < length; i++)
                bytes[i] = (byte)(i + 1);
            return bytes;
        }

        /// <summary>
        /// Encodes a minimal CertificateInstallationRes carrying <paramref name="encryptedPrivateKey"/>
        /// and decodes it back, returning the wire bytes and the key as it came off the wire.
        /// </summary>
        private static (byte[] Wire, byte[]? Key) RoundTrip(byte[] encryptedPrivateKey)
        {
            var res = new CertificateInstallationRes(
                          new MessageHeaderType(new byte[8], 1_700_000_000UL, null),
                          ResponseCode.OK,
                          Processing.Finished,
                          new CertificateChainType(Fill(64), null),
                          new SignedInstallationDataType(
                              "id1",
                              new ContractCertificateChainType(Fill(64), new SubCertificatesType([Fill(48)])),
                              EcdhCurve.SECP521,
                              Fill(133),
                              encryptedPrivateKey,
                              null,
                              null
                          ),
                          0
                      );

            var buffer = new byte[8192];
            Assert.That(res.TryEncode(buffer, out var length), Is.True, "the res must encode");

            var wire = buffer.AsSpan(0, length).ToArray();
            var back = (CertificateInstallationRes)CommonMessagesCodec.DecodeAny(wire, out _);

            return (wire, back.SignedInstallationData.SECP521_EncryptedPrivateKey);
        }

        #endregion

    }

}
