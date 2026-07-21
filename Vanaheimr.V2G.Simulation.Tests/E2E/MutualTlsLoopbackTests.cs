using System.Net;
using System.Net.Security;
using System.Security.Authentication;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.PKI;

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.TestData;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.E2E
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

        private static TlsOptions SeccTls(V2GTestPki pki) => new()
        {
            ServerCertificate           = pki.SeccServerCert,
            RequireClientCertificate    = true,
            ClientCertificateValidation = pki.ValidateVehicleClient,
        };

        private static TlsOptions EvccTls(V2GTestPki pki) => new()
        {
            ClientCertificate           = pki.VehicleClientCert,
            ServerCertificateValidation = pki.ValidateSeccServer,
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
                Assert.That(((SslStream) seccStream).IsMutuallyAuthenticated, Is.True,
                    "the EVCC must have presented and passed client-certificate authentication");

                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                "localhost", listener.LocalEndpoint.Port, EvccTls(pki), cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), TimeSpan.FromSeconds(2));
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

            var evcc = new Evcc20Ac(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), TimeSpan.FromSeconds(2));
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            Assert.That((await seccTask).IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
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
            var evccTls = new TlsOptions { ServerCertificateValidation = pki.ValidateSeccServer };
            _ = Task.Run(async () =>
            {
                try
                {
                    using var s = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port, evccTls, cts.Token);
                    await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
                }
                catch { /* client-side outcome is timing-dependent under TLS 1.3 */ }
            }, cts.Token);

            Assert.That(async () => await seccTask,
                Throws.InstanceOf<AuthenticationException>().Or.InstanceOf<IOException>());
        }
    }
}
