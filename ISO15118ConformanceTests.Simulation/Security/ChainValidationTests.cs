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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Security;
using cloud.charging.open.protocols.ISO15118.SharedCC;

namespace ISO15118ConformanceTests.Simulation.Security
{

    /// <summary>
    /// The chain validator, and the one property whose absence cost a wrong conformance finding.
    /// </summary>
    /// <remarks>
    /// A V2G chain is three deep — root, two Sub-CAs, leaf — and a peer normally sends the two
    /// intermediates alongside its leaf. Whether this side <em>passes them on</em> to the validator is the
    /// difference between "the peer sent nothing" and "the peer sent everything", and on 2026-08-08 both
    /// TLS call sites dropped them: the <c>X509Chain</c> argument of the .NET validation callback was
    /// discarded, so every peer was judged on its bare leaf. A station that sent its full chain was
    /// rejected against its own root, which was written up as a property of that counterparty until
    /// <c>openssl s_client -showcerts</c> showed it sending all three certificates.
    ///
    /// These lock the semantics rather than the wiring, plus the helper the wiring now goes through.
    /// </remarks>
    [TestFixture]
    public class ChainValidationTests
    {

        private static X509Certificate2 _root = null!, _subCa1 = null!, _subCa2 = null!, _leaf = null!;

        /// <summary>A three-deep hierarchy, built here rather than borrowed, so the test says what it needs.</summary>
        [OneTimeSetUp]
        public void BuildHierarchy()
        {
            var now = DateTimeOffset.UtcNow;

            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _root = new CertificateRequest("CN=Test V2G Root", rootKey, HashAlgorithmName.SHA256)
                    {
                        CertificateExtensions = { new X509BasicConstraintsExtension(true, false, 0, true) }
                    }
                    .CreateSelfSigned(now.AddDays(-1), now.AddYears(5));

            using var sub1Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _subCa1 = Issue("CN=Test CPO Sub-CA 1", sub1Key, _root, rootKey, isCa: true, now);

            using var sub2Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _subCa2 = Issue("CN=Test CPO Sub-CA 2", sub2Key, _subCa1, sub1Key, isCa: true, now);

            using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _leaf = Issue("CN=Test SECC Leaf", leafKey, _subCa2, sub2Key, isCa: false, now);
        }

        [OneTimeTearDown]
        public void Dispose()
        {
            _root.Dispose(); _subCa1.Dispose(); _subCa2.Dispose(); _leaf.Dispose();
        }

        private static X509Certificate2 Issue(string subject, ECDsa key, X509Certificate2 issuer,
                                              ECDsa issuerKey, bool isCa, DateTimeOffset now)
        {
            var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
            if (isCa)
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

            var serial = new byte[8];
            RandomNumberGenerator.Fill(serial);
            return request.Create(issuer.SubjectName, X509SignatureGenerator.CreateForECDsa(issuerKey),
                                  now.AddDays(-1), now.AddYears(2), serial);
        }

        private static V2GChainValidator RootOnly() => new([.. new[] { _root }]);

        /// <summary>
        /// The regression. With only the root trusted — the shape of a real trust store, which holds
        /// anchors and not intermediates — a leaf validates <em>if and only if</em> the intermediates the
        /// peer sent are handed over. The failing half is what both TLS call sites produced for every peer.
        /// </summary>
        [Test]
        public void RootOnlyTrust_NeedsThePeersIntermediates()
        {
            var withThem    = RootOnly().Validate(_leaf, [_subCa2.RawData, _subCa1.RawData]);
            var withoutThem = RootOnly().Validate(_leaf);

            Assert.Multiple(() =>
            {
                Assert.That(withThem.Ok, Is.True,
                            $"the root is trusted and the peer sent the path to it: {withThem.Reason}");
                Assert.That(withThem.Anchor, Does.Contain("Test V2G Root"),
                            "the reported anchor is the root, not the Sub-CA the chain passed through");

                Assert.That(withoutThem.Ok, Is.False,
                            "a leaf two hops below the only trusted root cannot reach it alone");
            });
        }

        /// <summary>
        /// The other half of the same story, and the reason a bundle of root + Sub-CAs is safe: a
        /// non-self-signed certificate in the trust store can act as an intermediate and never as an
        /// anchor. Measured against EVerest and eVDriveFlow before it was tested here.
        /// </summary>
        [Test]
        public void SubCaInTheTrustStore_IsNeverAnAnchor()
        {
            var subCasOnly = new V2GChainValidator([.. new[] { _subCa1, _subCa2 }]);
            var bundle     = new V2GChainValidator([.. new[] { _root, _subCa1, _subCa2 }]);

            Assert.Multiple(() =>
            {
                Assert.That(subCasOnly.Validate(_leaf).Ok, Is.False,
                            "both Sub-CAs can build the whole path from the leaf and still must not anchor it");
                Assert.That(bundle.Validate(_leaf).Ok, Is.True,
                            "with the root added the same bundle validates");
                Assert.That(bundle.Validate(_leaf).Anchor, Does.Contain("Test V2G Root"),
                            "and it anchors at the root rather than at the nearest trusted certificate");
            });
        }

        /// <summary>A real root of the wrong branch is refused, which is what a negative control needs.</summary>
        [Test]
        public void AnUnrelatedRoot_IsRefused()
        {
            using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var other    = new CertificateRequest("CN=Test MO Root", otherKey, HashAlgorithmName.SHA256)
                                 {
                                     CertificateExtensions = { new X509BasicConstraintsExtension(true, false, 0, true) }
                                 }
                                 .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));

            var result = new V2GChainValidator([.. new[] { other }]).Validate(_leaf, [_subCa2.RawData, _subCa1.RawData]);
            Assert.That(result.Ok, Is.False, "a chain that reaches a root nobody configured is not trusted");
        }

        /// <summary>
        /// <c>PeerIntermediates</c> is the piece that did not exist while the bug did: the callback's
        /// <c>X509Chain</c> carries the peer's own certificates in <c>ChainPolicy.ExtraStore</c>, and this
        /// is what turns them into something the validator accepts.
        /// </summary>
        [Test]
        public void PeerIntermediates_SurfacesWhatTheHandshakeCarried()
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.ExtraStore.Add(_subCa2);
            chain.ChainPolicy.ExtraStore.Add(_subCa1);

            var carried = TrustRoots.PeerIntermediates(chain);

            Assert.Multiple(() =>
            {
                Assert.That(carried, Is.Not.Null);
                Assert.That(carried!, Has.Count.EqualTo(2));
                Assert.That(RootOnly().Validate(_leaf, carried).Ok, Is.True,
                            "what it returns is exactly what the validator needs");
            });
        }

        /// <summary>No chain, or an empty one, is <c>null</c> rather than an empty list — "the peer sent
        /// nothing" and "there was nothing to ask" read the same to the validator and should.</summary>
        [Test]
        public void PeerIntermediates_IsNullWhenThereIsNothing()
        {
            using var empty = new X509Chain();

            Assert.Multiple(() =>
            {
                Assert.That(TrustRoots.PeerIntermediates(null),  Is.Null);
                Assert.That(TrustRoots.PeerIntermediates(empty), Is.Null);
            });
        }

    }

}
