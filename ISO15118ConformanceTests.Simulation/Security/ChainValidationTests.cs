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

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Security;
using cloud.charging.open.protocols.ISO15118.SharedCC;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.Transport;

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

        /// <summary>
        /// A peer that hands over its own root does not thereby become trusted — the property every
        /// negative control in every chain run depends on, and the one no counterparty could demonstrate
        /// until tux-evse, whose `_client_chain.pem` carries leaf, Sub-CA <em>and</em> root.
        /// </summary>
        /// <remarks>
        /// Nothing is missing here, which is what separates it from the other negative cases: the chain
        /// builds all the way to a self-signed certificate and is refused for being one nobody configured,
        /// not for running out of issuers. A validator that walked the presented chain to its end and
        /// stopped at "self-signed, therefore a root" would accept this.
        /// <para>
        /// Asserted on the <em>outcome</em> and never on <see cref="ChainResult.Reason"/>: that text comes
        /// from the platform and is both OS-dependent — OpenSSL says "self-signed certificate in
        /// certificate chain" where Windows says the chain terminated in an untrusted root — and
        /// localised, so on a German Windows it arrives in German. Run notes quote it; tests must not
        /// depend on it.
        /// </para>
        /// </remarks>
        [Test]
        public void APeerSuppliedRoot_DoesNotBecomeAnAnchor()
        {
            // The store holds the intermediates only; the peer supplies everything, root included.
            var subCasOnly = new V2GChainValidator([.. new[] { _subCa1, _subCa2 }]);

            var result = subCasOnly.Validate(_leaf, [_subCa2.RawData, _subCa1.RawData, _root.RawData]);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False, "a root arriving from the peer is not a root we trust");
                Assert.That(result.Anchor, Is.Null, "a refused chain reports no anchor");
                Assert.That(result.Reason, Is.Not.Empty, "and it says why, in the platform's own words");
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

        /// <summary>
        /// The same regression, one layer out — in the <em>interop fixtures'</em> own callback, which is a
        /// second implementation of this and was missed when the first was fixed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Everything above pins <see cref="V2GChainValidator"/>, which the two runnable peers go through.
        /// <c>InteropEnvironment.DevTlsOrNull</c> builds its own <see cref="X509Chain"/> instead, and until
        /// 2026-08-14 it discarded the callback's chain argument exactly as the app once did — so every
        /// counterparty was judged on its bare leaf and every TLS run had to be handed the intermediates in
        /// its trust bundle. Nothing detected that, because a bundle carrying root + Sub-CAs passes either
        /// way; only a <em>root-only</em> anchor tells them apart.
        /// </para>
        /// <para>
        /// Found on the wire against EVerest's <c>EvseV2G</c>, which sends all three certificates: the
        /// root-only run was refused here and accepted by <c>openssl s_client -CAfile</c> against the same
        /// station minutes apart
        /// (<c>docs/interop-runs/2026-08-14-everest-iso2-ac-tls12/</c>).
        /// </para>
        /// </remarks>
        [Test]
        public void TheInteropFixturesCallback_AlsoReadsWhatThePeerSent()
        {

            var pem      = Path.Combine(Path.GetTempPath(), $"chainvalidation-root-{Guid.NewGuid():N}.pem");
            var oldTls   = Environment.GetEnvironmentVariable("V2G_INTEROP_TLS");
            var oldTrust = Environment.GetEnvironmentVariable("V2G_INTEROP_TLS_TRUST");

            try
            {

                // A trust store the way a real one looks: anchors, no intermediates.
                File.WriteAllText(pem, _root.ExportCertificatePem());
                Environment.SetEnvironmentVariable("V2G_INTEROP_TLS",       "1");
                Environment.SetEnvironmentVariable("V2G_INTEROP_TLS_TRUST", pem);

                var validate = Interop.InteropEnvironment
                                      .DevTlsOrNull(ProtocolVariant.Iso15118_2)!
                                      .ServerCertificateValidation!;

                // What .NET hands the callback: the peer's own certificates in ChainPolicy.ExtraStore.
                using var sent    = new X509Chain();
                sent.ChainPolicy.ExtraStore.Add(_subCa2);
                sent.ChainPolicy.ExtraStore.Add(_subCa1);
                using var nothing = new X509Chain();

                Assert.Multiple(() =>
                {
                    Assert.That(validate(this, _leaf, sent,    SslPolicyErrors.None), Is.True,
                                "the peer sent the path to the only trusted root, so the leaf validates");
                    Assert.That(validate(this, _leaf, nothing, SslPolicyErrors.None), Is.False,
                                "and a station that really does send a bare leaf is still refused");
                });

            }
            finally
            {
                Environment.SetEnvironmentVariable("V2G_INTEROP_TLS",       oldTls);
                Environment.SetEnvironmentVariable("V2G_INTEROP_TLS_TRUST", oldTrust);
                try { File.Delete(pem); } catch { }
            }

        }

        /// <summary>
        /// The <em>same</em> callback again, one layer further out: carried onto the <b>managed</b> TLS
        /// backend. Third costume of one defect, and the one that cost a measurement.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>TlsPlatform</c> used to bridge a <see cref="RemoteCertificateValidationCallback"/> onto
        /// <c>BcTlsOptions.ValidatePeerLeaf</c> and invoke it with <c>chain: null</c> — so the fix directly
        /// above, which reads that argument, had nothing to read the moment a run selected BouncyCastle. A
        /// root-only anchor then refused every station on that backend.
        /// </para>
        /// <para>
        /// Found 2026-08-16 by the isomux §4 attempt, where <em>both</em> arms died at
        /// <c>bad_certificate(42)</c> — the control included, which is what said the fault was ours rather
        /// than EVerest's. The run selected the managed backend for the first time in this project's history
        /// because <c>trusted_ca_keys</c> cannot be sent any other way, so the defect had been unreachable
        /// until then (<c>docs/interop-runs/2026-08-16-everest-isomux-trusted-ca-keys/</c>).
        /// </para>
        /// </remarks>
        [Test]
        public void TheSameCallbackOnTheManagedBackend_ReadsWhatThePeerSentToo()
        {

            var pem      = Path.Combine(Path.GetTempPath(), $"chainvalidation-bc-root-{Guid.NewGuid():N}.pem");
            var oldTls   = Environment.GetEnvironmentVariable("V2G_INTEROP_TLS");
            var oldTrust = Environment.GetEnvironmentVariable("V2G_INTEROP_TLS_TRUST");

            try
            {

                File.WriteAllText(pem, _root.ExportCertificatePem());
                Environment.SetEnvironmentVariable("V2G_INTEROP_TLS",       "1");
                Environment.SetEnvironmentVariable("V2G_INTEROP_TLS_TRUST", pem);

                // The -2 profile, which is what forces this backend in the first place: trusted_ca_keys is a
                // TLS 1.2 extension and SslStream cannot send it.
                var managed = TlsPlatform.ToBcClientOptions(
                                  Interop.InteropEnvironment.DevTlsOrNull(ProtocolVariant.Iso15118_2)!);

                Assert.That(managed.ValidatePeerChain, Is.Not.Null,
                            "a TlsOptions session must bridge onto the chain hook, not the leaf one");

                Assert.Multiple(() =>
                {
                    // What BouncyCastle hands over: the peer's certificate list, leaf first.
                    Assert.That(managed.ValidatePeerChain!([_leaf.RawData, _subCa2.RawData, _subCa1.RawData]), Is.True,
                                "the station sent the path to the only trusted root, so its leaf validates");
                    Assert.That(managed.ValidatePeerChain!([_leaf.RawData]), Is.False,
                                "and a station that really does send a bare leaf is still refused");

                    Assert.That(managed.ValidatePeerLeaf, Is.Null,
                                "both hooks must pass when both are set, so a leaf-only copy of a "
                              + "chain-checking callback would fail first — exactly the case being fixed");
                });

            }
            finally
            {
                Environment.SetEnvironmentVariable("V2G_INTEROP_TLS",       oldTls);
                Environment.SetEnvironmentVariable("V2G_INTEROP_TLS_TRUST", oldTrust);
                try { File.Delete(pem); } catch { }
            }

        }

    }

}
