/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
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

using System.Net;
using System.Security.Cryptography;

using NUnit.Framework;

using Org.BouncyCastle.Security;

using cloud.charging.open.protocols.ISO15118.PKI;
using cloud.charging.open.protocols.ISO15118.Transport;
using cloud.charging.open.protocols.ISO15118.Transport.BouncyCastle;

namespace ISO15118ConformanceTests.Simulation.E2E
{

    /// <summary>
    /// `[V2G2-651]` on the wire: an ISO 15118-<b>2</b> car names the V2G root certificates it holds, in
    /// RFC 6066's <c>trusted_ca_keys</c> ClientHello extension, over TLS 1.2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until 2026-08-16 <c>grep -rn trusted_ca_keys</c> matched nothing in this stack, so a *shall* on
    /// every `-2` EV was unimplemented — and this project had filed
    /// <c>docs/reports/everest-isomux.md</c> §4 about a station that disables support for it while being
    /// unable to run the failing case, for want of exactly this client.
    /// </para>
    /// <para>
    /// <b>Why BouncyCastle.</b> <c>SslStream</c> cannot add a ClientHello extension on any platform, so
    /// the managed backend is the only one that can carry this; it grew a TLS 1.2 profile for the purpose
    /// (<see cref="BcTlsOptions.Iso2Profile"/>). A `-2` session configured with roots on the SslStream
    /// path is <b>refused</b> rather than run without them — pinned by
    /// <c>TlsOptionsBridgeTests</c> in the transport's own suite.
    /// </para>
    /// <para>
    /// <b>What these do not claim.</b> Our station records the extension and serves the chain it was
    /// configured with regardless: `[V2G2-871]`'s selection duty is the *station's*, and nothing here
    /// implements it. The car half is what `[V2G2-651]` asks of us, and the car half is what is tested.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class BcTrustedCaKeysTests
    {

        /// <summary>Two roots named, two <c>cert_sha1_hash</c> entries arriving, in order, and nothing else.</summary>
        [Test]
        public async Task Iso2Tls12_TheCarNamesItsRoots_AndTheStationSeesThem()
        {

            var (rootA, rootB) = (RandomRootDer(), RandomRootDer());

            var seen = await RunHandshakeAsync(carRoots: [rootA, rootB]);

            Assert.Multiple(() =>
            {
                Assert.That(seen.Hashes.Count, Is.EqualTo(2),
                            "one TrustedAuthority per root the car holds ([V2G2-651])");
                Assert.That(seen.Hashes[0], Is.EqualTo(SHA1.HashData(rootA)).AsCollection,
                            "cert_sha1_hash of the first root, in the order the car listed them");
                Assert.That(seen.Hashes[1], Is.EqualTo(SHA1.HashData(rootB)).AsCollection);
                Assert.That(seen.OtherTypes, Is.Zero,
                            "this car sends cert_sha1_hash only; the other three identifier types are legal "
                          + "RFC 6066 and would be counted here rather than silently dropped");
            });

        }


        /// <summary>One root is the ordinary deployment — and the case in which a station that honours the
        /// extension and one that ignores it are indistinguishable, which is why the isomux finding needed
        /// two.</summary>
        [Test]
        public async Task Iso2Tls12_ASingleRoot_IsStillNamed()
        {

            var root = RandomRootDer();

            var seen = await RunHandshakeAsync(carRoots: [root]);

            Assert.That(seen.Hashes.Count, Is.EqualTo(1));
            Assert.That(seen.Hashes[0], Is.EqualTo(SHA1.HashData(root)).AsCollection);

        }


        /// <summary>No roots configured, no extension: the pre-2026-08-16 behaviour, kept reachable so the
        /// difference is a decision rather than a default nobody chose.</summary>
        [Test]
        public async Task Iso2Tls12_WithoutRoots_SendsNoExtension()
        {

            var seen = await RunHandshakeAsync(carRoots: null);

            Assert.That(seen.Called, Is.False,
                        "the station's observer must not fire at all — an empty extension and an absent one "
                      + "are different things on the wire");

        }


        /// <summary>A self-signed root, DER — the extension names certificates by hash, so any distinct
        /// DER will do and no hierarchy has to be built for this.</summary>
        private static Byte[] RandomRootDer()
        {

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                              $"CN=Test V2G Root {Guid.NewGuid():N}", key, HashAlgorithmName.SHA256);

            var now = DateTimeOffset.UtcNow;

            using var root = request.CreateSelfSigned(now.AddMinutes(-10), now.AddHours(2));

            return root.RawData;

        }


        private sealed record Observed(Boolean Called, IReadOnlyList<Byte[]> Hashes, Int32 OtherTypes);


        /// <summary>One ISO 15118-2 TLS 1.2 handshake on the managed backend, and what the station saw.</summary>
        private static async Task<Observed> RunHandshakeAsync(Byte[][]? carRoots)
        {

            var hierarchy = V2GHierarchy.Build(
                                V2GAlgorithm.EcdsaP256,
                                new SecureRandom(),
                                V2GProfileOptions: new V2GProfileOptions(V2GProfileFlavor.Strict15118_2,
                                                                          V2GAlgorithm.EcdsaP256,
                                                                          V2GPolicySet.None));

            var called     = false;
            var hashes     = (IReadOnlyList<Byte[]>) Array.Empty<Byte[]>();
            var otherTypes = 0;

            var secc = new BcTlsOptions
            {
                Iso2Profile     = true,
                OwnCredentials  = new BcTlsCredentials(
                                      [hierarchy.SeccLeaf.Certificate.GetEncoded(),
                                       hierarchy.CpoSubCa2.Certificate.GetEncoded(),
                                       hierarchy.CpoSubCa1.Certificate.GetEncoded()],
                                      hierarchy.SeccLeaf.KeyPair.Private,
                                      Org.BouncyCastle.Tls.SignatureScheme.ecdsa_secp256r1_sha256),
                OnTrustedCaKeys = (seen, others) => { called = true; hashes = seen; otherTypes = others; },
            };

            var evcc = new BcTlsOptions
            {
                Iso2Profile   = true,
                TrustedCaKeys = carRoots,
                // -2 TLS is unilateral: the car authenticates the station and presents nothing itself.
                ValidatePeerLeaf = der => der.AsSpan().SequenceEqual(hierarchy.SeccLeaf.Certificate.GetEncoded()),
            };

            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0), secc);

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                // The handshake is the measurement; one byte each way proves the stream is live without
                // dragging a whole -2 session in, which Iso2LoopbackTests already covers.
                var buffer = new Byte[1];
                await seccStream.ReadExactlyAsync(buffer, cts.Token);
                await seccStream.WriteAsync(buffer, cts.Token);
                await seccStream.FlushAsync(cts.Token);
            }, cts.Token);

            using (var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port, evcc, cts.Token))
            {
                await evccStream.WriteAsync(new Byte[] { 0x42 }, cts.Token);
                await evccStream.FlushAsync(cts.Token);

                var echo = new Byte[1];
                await evccStream.ReadExactlyAsync(echo, cts.Token);
                Assert.That(echo[0], Is.EqualTo(0x42), "the TLS 1.2 stream carried a byte in both directions");
            }

            await seccTask;

            return new Observed(called, hashes, otherTypes);

        }

    }

}
