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
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{
    /// <summary>
    /// The SECC's live contract-provisioning path, exercised with the REAL Josev
    /// <c>CertificateInstallationReq</c> (byte-exact reproduction of the 2026-07-22 live probe frame — a
    /// Josev EVCC's signed OEM provisioning chain; see <c>JosevCertificateInstallationReqTests</c> in
    /// cloud.charging.open.protocols.ISO15118.EXI.Tests for the frame's provenance and byte-exactness proofs). The SECC must verify
    /// Josev's OEM signature (standalone-xmldsig grammar, ecdsa-sha256) and answer with a schema-valid
    /// signed CertificateInstallationRes — staying in the Authorization phase so the AuthorizationReq can
    /// follow. Josev's OEM cert is P-256, so the issued key cannot be encrypted *for* it
    /// (<c>EncryptedForOem == false</c>); the P-521 happy path is covered by the loopback E2E.
    /// </summary>
    [TestFixture]
    public class Secc20CertInstallTests
    {
        /// <summary>The pinned live Josev frame (same octets as JosevCertificateInstallationReqTests).</summary>
        private const string JosevCertificateInstallationReqHex =
            "801c0459db319f8b14f3f7087c983d3060a25687474703a2f2f7777772e77332e6f72672f54522f63616e6f6e6963616c2d6578692f435687474703a2f2f7777772e77332e6f72672f323030312f30342f786d6c647369672d6d6f72652365636473612d736861323536440c46d2c86204ad0e8e8e0745e5eeeeeee5cee665cdee4ce5ea8a45ec6c2dcdedcd2c6c2d85acaf0d25e90a5a1d1d1c0e8bcbddddddcb9dcccb9bdc99cbcc8c0c0c4bcc0d0bde1b5b195b98c8dcda184c8d4d90802b410d8fcefcc88a958571255ed49501f954ea3531884272e9124da2715bb1084a05551f956941d9ff0db1f2154399c40a2d67f234aa342c9afd27241912a3ceb017e675c661fd54f1d9927d9e2a52c75ca85e469f90792ad577db4ee43818a4686200ad2c8627201984100f0184100c350018100810101182018050304154324671e8201811823988918080301aa82018604a7a2a6a9bab121a099188798068301aa8205060329bbb4ba31b4188598048301aa820309812aa598899808830504c91344c9f91632008c8b01a7a2a6980f0b86991b181b991898989a999918ad0b869998181b991818989a999918ad1824988a18090301aa82018605a7a2a6a83937bb21b2b93a188798068301aa8205060329bbb4ba31b4188598048301aa820309812aa598899808830504c91344c9f91632008c8b01a7a2a6982c98098303954324671e81008304154324671e81808381a1000211f8d5cfa8b3023cf845b21a9a6936717100aceed35377518fcbab3edd8cac3dc07f8a4992ba4211904037c7e8b0a37eebd07a8f31d09202c2456467a7be40fad1b0182f18060301aa8e898080ff8201180018070301aa8e878080ff8202018101c4180e8301aa8e87020b020a73f4c1c98258c0b75c633817aa6064ed8ddf0100180f8301aa8e91820c180b400a63dab9984ac047c177782586b9b8326181ed58fe98050304154324671e82018101a400182281108068269fe0ad8b947d9cb06ef9ac9d09a0c04877bc40318a9858f0f7093c47369a81103c744338df81dc6b75e56b0cb432df2b4e8ff9ac6c3c5c3acc9384a1c939dac503a00cc2080790c208062a800c0804080808c0fcc0281820aa192338f4100c08c11cc448c040180d54100c30253d15354dd5890d04c4c43cc034180d54102830194ddda5d18da0c42cc024180d5410184c09552cc44cc044182826489a264fc8b1900464580d3d1534c0785c34c8d8c0dcc8c4c4c4d4ccc8c1685c34ccc0c0dcc8c0c4c4d4ccc8c168c11cc448c040180d54100c30253d15354dd5890d04c8c43cc034180d54102830194ddda5d18da0c42cc024180d5410184c09552cc44cc044182826489a264fc8b1900464580d3d1534c164c04c181caa192338f408041820aa192338f40c041c0d0800130912f7d3acfe99ace410efeddce98af308fcb05a5ed43c6464f765f2b36bc151333b0c76a3452189e97596449fe86d6ce0d684412281ea1f267fe24bde15dce68d98c190c048180d54744c0407fc1020c0180407fc080400c038180d54743c0407fc10100c080418c074180d547438105810531ed5ccc256023e0bbbc12c35cdc1930c0f6ac7f4c07c180d54748c1060c05a0053a7c42dc1a0c101778dfc7726cb8878048e703dccc0281820aa192338f4100c080d2000c1140884024a9cd7b20a8ebb3252ca2f2f3bfbf9ef004e1e93cc509bdafaa7a99d5fb260d808804ee97f2be73f2d6c08e1e0bde016b5e6e2410a5d01d4c07f6c759cebf214d2a03a00cc2080790c208062a800c0804080808c0f8c0281820aa192338f4100c08c11cc448c040180d54100c30253d153549bdbdd10d04c43cc034180d54102830194ddda5d18da0c42cc024180d5410184c09552cc44cc044182826489a264fc8b1900464580d3d1534c0785c34c8d8c0dcc8c4c4c4d4ccc8c1685c34ccc0c0dcc8c0c4c4d4ccc8c168c11cc448c040180d54100c30253d15354dd5890d04c4c43cc034180d54102830194ddda5d18da0c42cc024180d5410184c09552cc44cc044182826489a264fc8b1900464580d3d1534c164c04c181caa192338f408041820aa192338f40c041c0d080012bb5b4867e4caecc8112bf361dc9fe164168fedc7dc92aeb593be033795d9abe11522a8e15a148b8863f3af0fcd49f06fbd05631761d5f4d700a4614c0beee9128d98c190c048180d54744c0407fc1020c0180407fc080404c038180d54743c0407fc10100c080418c074180d54743810581053a7c42dc1a0c101778dfc7726cb8878048e703dccc07c180d54748c1060c05a00504b26dd72c60a17d015bfe7abf8cbe61f0f031ce8c0281820aa192338f4100c080d2000c1140884027e5d18a6a89ccfe365a28ef8f8c85665146bfb9d6d646553bcce886e80b1e2e40880211e5ec89e6a40eadb792b8b25ad17b716d869548fba198b91f0bcac5fba6f0080acf13985b594a10d38f558c91d49bdbdd10d04b13cf54ddda5d18da0b10cf5552cb1110cf558c91ca4f82e58040320";

        private static MessageHeaderType Hdr() => new(new byte[8], 1_700_000_000UL, null);

        [Test]
        public void JosevCertificateInstallationReq_VerifiesAndGetsSignedRes()
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionSetupReq(Hdr(), "EVCC01"));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Hdr()));

            var frame = Convert.FromHexString(JosevCertificateInstallationReqHex);
            var req = (CertificateInstallationReq)CommonMessagesCodec.DecodeAny(frame, out _);

            var res = (CertificateInstallationRes)secc.Handle(MessageSet.Iso20CommonMessages, req).Response;

            Assert.Multiple(() =>
            {
                Assert.That(secc.CertInstall, Is.Not.Null);
                Assert.That(secc.CertInstall!.DigestOk, Is.True, "OEM-chain fragment digest must match");
                Assert.That(secc.CertInstall.SignatureOk, Is.True, "Josev's OEM signature must verify");
                Assert.That(secc.CertInstall.SignatureGrammar, Is.EqualTo("xmldsig-standalone"),
                    "Josev signs over the standalone xmldsig grammar");
                Assert.That(secc.CertInstall.OemSubject, Does.Contain("OEMProvCert"));
                Assert.That(secc.CertInstall.EncryptedForOem, Is.False,
                    "a P-256 OEM cert cannot take part in the -20 secp521r1 ECDH");

                Assert.That(res.ResponseCode, Is.EqualTo(ResponseCode.OK));
                Assert.That(res.SignedInstallationData.SECP521_EncryptedPrivateKey, Has.Length.EqualTo(94));
                Assert.That(res.SignedInstallationData.DHPublicKey, Has.Length.EqualTo(133));
                Assert.That(res.Header.Signature, Is.Not.Null, "the res must be signed (CPS leaf)");
            });

            // The res must round-trip through the wire codec (it goes out via EncodeAny → TryEncode).
            var buf = new byte[8192];
            Assert.That(res.TryEncode(buf, out int n), Is.True);
            var back = (CertificateInstallationRes)CommonMessagesCodec.DecodeAny(buf.AsSpan(0, n).ToArray(), out _);
            Assert.That(back.SignedInstallationData.ContractCertificateChain.Certificate,
                Is.EqualTo(res.SignedInstallationData.ContractCertificateChain.Certificate));

            // The session continues: the AuthorizationReq is still legal after the cert-install exchange.
            var auth = secc.Handle(MessageSet.Iso20CommonMessages,
                new AuthorizationReq(Hdr(), Authorization.EIM, new EIM_AReqAuthorizationModeType(), null));
            Assert.That(auth.Response, Is.InstanceOf<AuthorizationRes>());
        }

        [Test]
        public void ContractProvisioning_EncryptRecover_RoundTrips()
        {
            using var oemKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP521);
            using var contractKey = ECDsa.Create(ECCurve.NamedCurves.nistP521);

            var (dhPub, wrapped) = ContractProvisioning.EncryptContractKey(oemKey.PublicKey, contractKey);
            Assert.That(dhPub, Has.Length.EqualTo(133));
            Assert.That(wrapped, Has.Length.EqualTo(94));

            using var recovered = ContractProvisioning.RecoverContractKey(oemKey, dhPub, wrapped);

            // The recovered key must be the same signing identity: a signature made with the original key
            // verifies under the recovered key's public parameters and vice versa.
            var message = new byte[] { 1, 2, 3, 4, 5 };
            var sig = contractKey.SignData(message, HashAlgorithmName.SHA512);
            Assert.That(recovered.VerifyData(message, sig, HashAlgorithmName.SHA512), Is.True);
            var sig2 = recovered.SignData(message, HashAlgorithmName.SHA512);
            Assert.That(contractKey.VerifyData(message, sig2, HashAlgorithmName.SHA512), Is.True);
        }

        [Test]
        public void ContractProvisioning_TamperedCiphertext_FailsAuthentication()
        {
            using var oemKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP521);
            using var contractKey = ECDsa.Create(ECCurve.NamedCurves.nistP521);
            var (dhPub, wrapped) = ContractProvisioning.EncryptContractKey(oemKey.PublicKey, contractKey);

            wrapped[20] ^= 0xFF;   // flip a ciphertext bit → GCM tag must reject
            Assert.Throws<AuthenticationTagMismatchException>(
                () => ContractProvisioning.RecoverContractKey(oemKey, dhPub, wrapped));
        }
    }
}
