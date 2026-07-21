using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.Ethernet;

using cloud.charging.open.protocols.ISO15118.SLAC.StateMachine;
using cloud.charging.open.protocols.ISO15118.SLAC.Transport;

using Vanaheimr.V2G.Simulation.Slac;

namespace Vanaheimr.V2G.Simulation.Tests.Slac
{
    /// <summary>
    /// A real EV↔EVSE SLAC match run end-to-end over loopback UDP (<see cref="UdpSlacTransport"/>, which
    /// unicasts to bootstrap/learned peers — no multicast, so it is deterministic on one host, unlike SDP).
    /// The EVSE listens first, the EV runs the full CM_SLAC_PARM → sounding → CM_SLAC_MATCH sequence, and
    /// both sides must end up with identical PLC credentials (NID/NMK).
    /// </summary>
    [TestFixture]
    public class SlacUdpLoopbackTests
    {
        private static int FreeUdpPort()
        {
            using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint) probe.Client.LocalEndPoint!).Port;
        }

        private static MACAddress Mac(params byte[] bytes) => MACAddress.FromPhysicalAddress(new PhysicalAddress(bytes));

        [Test]
        public async Task EvAndEvse_CompleteSlacMatch_OverUdpLoopback()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var evseEndpoint = new IPEndPoint(IPAddress.Loopback, FreeUdpPort());

            await using var evseTransport = new UdpSlacTransport(Mac(0x00, 0x11, 0x22, 0x33, 0x44, 0x55), evseEndpoint);
            await using var evTransport   = new UdpSlacTransport(Mac(0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF),
                                                                 new IPEndPoint(IPAddress.Loopback, 0),
                                                                 bootstrapPeers: [evseEndpoint]);

            var nid = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
            var nmk = Enumerable.Range(0, 16).Select(i => (byte) i).ToArray();

            // EVSE starts listening first so it is ready before the EV's first CM_SLAC_PARM.REQ.
            await using var evse = new SlacEvseStage(evseTransport,
                new EvseSlacOptions { EvseId = new byte[17], Nid = nid, Nmk = nmk });
            await evse.StartAsync(cts.Token);

            var ev = new SlacEvStage(evTransport, new EvSlacOptions
            {
                PevId                     = new byte[17],
                ParmCnfCollectionWindow   = TimeSpan.FromMilliseconds(200),
                AttenCharCollectionWindow = TimeSpan.FromMilliseconds(400),
            });

            var evTask   = ev.PairAsync(cts.Token);
            var evseTask = evse.WaitForMatchAsync(cts.Token);
            await Task.WhenAll(evTask, evseTask);

            var evResult   = await evTask;
            var evseResult = await evseTask;

            Assert.Multiple(() =>
            {
                Assert.That(evResult.Nid, Is.EqualTo(nid), "EV must receive the EVSE's NID");
                Assert.That(evResult.Nmk, Is.EqualTo(nmk), "EV must receive the EVSE's NMK");
                Assert.That(evseResult.Nid, Is.EqualTo(nid));
                Assert.That(evseResult.Nmk, Is.EqualTo(nmk));
            });
        }
    }
}
