using System.Net;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Discovery;
using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.Discovery
{
    /// <summary>
    /// Proves the <see cref="ISeccDiscovery"/> seam actually drives a session: a discovery stage yields
    /// the SECC endpoint, which is then connected and run to completion. Uses <see cref="FixedSeccDiscovery"/>
    /// (deterministic, loopback) — the SDP-backed discovery follows the exact same seam.
    /// </summary>
    [TestFixture]
    public class FixedDiscoveryLoopbackTests
    {
        [Test]
        public async Task FixedDiscovery_DrivesIso20DcLoopbackSession()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var machine = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await machine.RunAsync(seccStream, cts.Token);
                return machine;
            }, cts.Token);

            // Discovery stage → endpoint (here fixed; the SDP client yields the same SeccEndpoint shape).
            ISeccDiscovery discovery = new FixedSeccDiscovery(
                new SeccEndpoint(IPAddress.Loopback, listener.LocalEndpoint.Port, Tls: false));
            var endpoint = await discovery.DiscoverAsync(cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(endpoint.Host, endpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            Assert.That((await seccTask).IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }
    }
}
