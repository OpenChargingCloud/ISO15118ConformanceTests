using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.TestData;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.E2E
{
    /// <summary>
    /// Proves the exact same state-machine/framing code that runs over plain TCP in
    /// <see cref="Iso2LoopbackTests"/>/<see cref="Iso20LoopbackTests"/> also runs unchanged over
    /// <see cref="SslStream"/> — one -2 flow (TLS optional per spec) and one -20 flow (TLS "vorgesehen").
    /// A self-signed test certificate is generated fresh for the run (see <see cref="TestCertificate"/>);
    /// the client validates it by thumbprint rather than blanket-accepting anything, so the callback
    /// still exercises real validation logic.
    /// </summary>
    [TestFixture]
    public class TlsLoopbackTests
    {
        private static TlsOptions ServerTls(X509Certificate2 cert) => new() { ServerCertificate = cert };

        private static TlsOptions ClientTls(X509Certificate2 serverCert) => new()
        {
            ServerCertificateValidation = (_, presented, _, _) =>
                presented is X509Certificate2 p && p.Thumbprint == serverCert.Thumbprint,
        };

        [Test]
        public async Task Iso2AcSession_RunsToCompletionOverTls()
        {
            using var cert = TestCertificate.CreateSelfSigned();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0), ServerTls(cert));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token);

                var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                "localhost", listener.LocalEndpoint.Port, ClientTls(cert), cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token);

            var evcc = new Evcc2(evccStream, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            Assert.That((await seccTask).IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }

        [Test]
        public async Task Iso20DcSession_RunsToCompletionOverTls()
        {
            using var cert = TestCertificate.CreateSelfSigned();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0), ServerTls(cert));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);

                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                "localhost", listener.LocalEndpoint.Port, ClientTls(cert), cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            Assert.That((await seccTask).IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }
    }
}
