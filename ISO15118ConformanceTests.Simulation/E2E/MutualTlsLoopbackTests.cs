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

using System.Net;
using System.Net.Security;
using System.Security.Authentication;

using NUnit.Framework;

using Org.BouncyCastle.Tls;

using cloud.charging.open.protocols.ISO15118.PKI;

using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using ISO15118ConformanceTests.Simulation.TestData;
using ISO15118ConformanceTests.Simulation.Timing;
using cloud.charging.open.protocols.ISO15118.Transport;

namespace ISO15118ConformanceTests.Simulation.E2E
{
    /// <summary>
    /// ISO 15118-20 over <b>mutual TLS</b>, loopback-only: the SECC presents its SECC leaf and requires +
    /// validates the EVCC's <b>Vehicle</b> client certificate; the EVCC presents the Vehicle leaf and
    /// validates the SECC's server certificate. Both chains anchor to a single V2G Root generated at
    /// test-time by the WWCP PKI builder — see <see cref="V2GTestPki"/> and <c>docs/pki-model.md</c>.
    /// Proves the same state-machine/framing code runs unchanged under a bilaterally-authenticated
    /// <see cref="SslStream"/>. (Curve note: P-256 rather than -20's nominal secp521r1 — Windows Schannel
    /// cannot use P-521 certs for TLS; see <see cref="NewPki"/>.)
    /// </summary>
    [TestFixture]
    public class MutualTlsLoopbackTests
    {
        // ISO 15118-20's nominal TLS curve is secp521r1, but Windows Schannel cannot use P-521
        // certificates for TLS authentication (verified: the handshake fails "Authentication failed";
        // P-256/P-384 work, and P-521 works on OpenSSL-backed .NET on Linux). We therefore exercise the
        // mutual-TLS *mechanism* — SECC server cert + Vehicle client cert, shared V2G Root — with a
        // Schannel-supported curve. The app-layer -20 signature suite still uses P-521/Ed448 via
        // BouncyCastle, independent of the TLS layer. See docs/pki-model.md.
        private static V2GTestPki NewPki() => V2GTestPki.Create(V2GAlgorithm.EcdsaP256, V2GProfileFlavor.Lab);

        // TLS 1.3 stated explicitly rather than inherited: it is what -20 mandates (docs/pki-model.md), and
        // TlsAssert below verifies the session really runs it instead of a silently downgraded version.
        private static TlsOptions SeccTls(V2GTestPki pki) => new()
        {
            ServerCertificate           = pki.SeccServerCert,
            RequireClientCertificate    = true,
            ClientCertificateValidation = pki.ValidateVehicleClient,
            EnabledSslProtocols         = SslProtocols.Tls13,
            CipherSuites                = TlsProfiles.Iso20CipherSuites,
        };

        private static TlsOptions EvccTls(V2GTestPki pki) => new()
        {
            ClientCertificate           = pki.VehicleClientCert,
            ServerCertificateValidation = pki.ValidateSeccServer,
            EnabledSslProtocols         = SslProtocols.Tls13,
            CipherSuites                = TlsProfiles.Iso20CipherSuites,
        };

        [Test]
        public async Task Iso20DcSession_RunsToCompletionOverMutualTls()
        {
            using var pki = NewPki();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0), SeccTls(pki));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                TlsAssert.NegotiatedVersion(seccStream, SslProtocols.Tls13);
                TlsAssert.NegotiatedCipherSuite(seccStream, TlsProfiles.Iso20CipherSuites);
                TlsAssert.MutuallyAuthenticated(seccStream,
                    "the EVCC must have presented and passed client-certificate authentication");

                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                "localhost", listener.LocalEndpoint.Port, EvccTls(pki), cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            Assert.That((await seccTask).IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }

        [Test]
        public async Task Iso20AcSession_RunsToCompletionOverMutualTls()
        {
            using var pki = NewPki();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0), SeccTls(pki));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Ac(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                "localhost", listener.LocalEndpoint.Port, EvccTls(pki), cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Ac(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            Assert.That((await seccTask).IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }

        [Test]
        public async Task Iso20DcSession_OverMutualTls_ClientSendsIntermediateChainToRootOnlySecc()
        {
            // Mirrors the live Josev TLS 1.3 mutual run: the SECC trusts ONLY the V2G root (no out-of-band
            // intermediates) and validates the client purely from the certs it sent over the wire. Succeeds only
            // because the EVCC presents ClientCertificate + ClientCertificateChain, so SslStream transmits the
            // Vehicle Sub-CAs. Without the chain (leaf only) the root-only SECC can't build the path — the very
            // failure the live run first hit.

            // Unlike the other tests here, this one cannot borrow the macOS TLS 1.3 fallback: what it asserts is
            // a property of SslStream itself — that a certificate context puts the intermediates on the wire.
            // Substituting the backend would leave the test green while testing something else, so it is
            // skipped instead. (The second half of this note used to be "and BouncyCastle's peer validation is
            // leaf-only", which stopped being true on 2026-08-16: TlsPlatform now bridges onto
            // ValidatePeerChain, and the root-only case has its own arm in Iso20BackendOptInLoopbackTests.)
            if (!TlsPlatform.SslStreamSupportsTls13)
                Assert.Ignore("Needs a TLS 1.3 SslStream session; this platform has none (see TlsPlatform). " +
                              "Covered on Windows/Linux, and the -20-faithful chain handling is covered on the " +
                              "BouncyCastle path by BcMutualTlsLoopbackTests.");

            using var pki = NewPki();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var seccTls = new TlsOptions
            {
                ServerCertificate           = pki.SeccServerCert,
                RequireClientCertificate    = true,
                ClientCertificateValidation = pki.ValidateVehicleClientWireChainOnly,
                EnabledSslProtocols         = SslProtocols.Tls13,
                CipherSuites                = TlsProfiles.Iso20CipherSuites,
            };
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0), seccTls);

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                TlsAssert.NegotiatedVersion(seccStream, SslProtocols.Tls13);
                TlsAssert.NegotiatedCipherSuite(seccStream, TlsProfiles.Iso20CipherSuites);
                TlsAssert.MutuallyAuthenticated(seccStream, "the EVCC must have passed client-certificate authentication");
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            var evccTls = new TlsOptions
            {
                ClientCertificate           = pki.VehicleClientCert,
                ClientCertificateChain      = pki.VehicleIntermediates,
                ServerCertificateValidation = pki.ValidateSeccServer,
                EnabledSslProtocols         = SslProtocols.Tls13,
                CipherSuites                = TlsProfiles.Iso20CipherSuites,
            };
            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port, evccTls, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            await Task.WhenAll(evcc.RunAsync(cts.Token), seccTask);

            Assert.That((await seccTask).IsDone, Is.True);
        }

        [Test]
        public void Secc_RejectsClientWithoutCertificate()
        {
            using var pki = NewPki();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0), SeccTls(pki));

            // The SECC requires + validates a client cert; the validation callback rejects a null cert.
            var seccTask = listener.AcceptAsync(cts.Token);

            // EVCC offers no client certificate. Fire the connect so the server processes the (empty) cert;
            // under TLS 1.3 the *client* may finish its side before the server rejects, so we don't assert on
            // it — the reliable signal is the SECC side failing to authenticate.
            var evccTls = new TlsOptions
            {
                ServerCertificateValidation = pki.ValidateSeccServer,
                EnabledSslProtocols         = SslProtocols.Tls13,
                CipherSuites                = TlsProfiles.Iso20CipherSuites,
            };
            _ = Task.Run(async () =>
            {
                try
                {
                    using var s = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port, evccTls, cts.Token);
                    await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
                }
                catch { /* client-side outcome is timing-dependent under TLS 1.3 */ }
            }, cts.Token);

            // How the rejection surfaces depends on the backend: SslStream raises AuthenticationException /
            // IOException, the BouncyCastle fallback a TlsFatalAlert(certificate_required) from ValidatePeer.
            Assert.That(async () => await seccTask,
                Throws.InstanceOf<AuthenticationException>()
                      .Or.InstanceOf<IOException>()
                      .Or.InstanceOf<TlsFatalAlert>());
        }
    }
}
