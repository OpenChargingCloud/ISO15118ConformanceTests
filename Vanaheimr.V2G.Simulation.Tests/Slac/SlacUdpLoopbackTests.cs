using System.Net;
using System.Net.NetworkInformation;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.Ethernet;

using cloud.charging.open.protocols.ISO15118.SLAC.Avln;
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
        private static MACAddress Mac(params byte[] bytes) => MACAddress.FromPhysicalAddress(new PhysicalAddress(bytes));

        [Test]
        public async Task EvAndEvse_CompleteSlacMatch_OverUdpLoopback()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            // Both sides bind port 0 and the EVSE's assigned port is read back off the live
            // socket. Probing for a "free" port first and binding it a moment later is a race:
            // the probe has to be closed to release the port, and anything on the machine can
            // take it in that window.
            await using var evseTransport = new UdpSlacTransport(Mac(0x00, 0x11, 0x22, 0x33, 0x44, 0x55),
                                                                 new IPEndPoint(IPAddress.Loopback, 0));
            await using var evTransport   = new UdpSlacTransport(Mac(0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF),
                                                                 new IPEndPoint(IPAddress.Loopback, 0),
                                                                 bootstrapPeers: [evseTransport.LocalEndpoint]);

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

        [Test]
        public async Task SlacMatch_ProgramsBothPlcChips_WithTheNegotiatedKey()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            // Both sides bind port 0 and the EVSE's assigned port is read back off the live
            // socket. Probing for a "free" port first and binding it a moment later is a race:
            // the probe has to be closed to release the port, and anything on the machine can
            // take it in that window.
            await using var evseTransport = new UdpSlacTransport(Mac(0x00, 0x11, 0x22, 0x33, 0x44, 0x66),
                                                                 new IPEndPoint(IPAddress.Loopback, 0));
            await using var evTransport   = new UdpSlacTransport(Mac(0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0x00),
                                                                 new IPEndPoint(IPAddress.Loopback, 0),
                                                                 bootstrapPeers: [evseTransport.LocalEndpoint]);

            var nid = new byte[] { 7, 6, 5, 4, 3, 2, 1 };
            var nmk = Enumerable.Range(16, 16).Select(i => (byte) i).ToArray();

            var evChip   = new SimulatedChipController();
            var evseChip = new SimulatedChipController();

            await using var evse = new SlacEvseStage(evseTransport,
                new EvseSlacOptions { EvseId = new byte[17], Nid = nid, Nmk = nmk },
                chip: evseChip);
            await evse.StartAsync(cts.Token);

            var ev = new SlacEvStage(evTransport,
                new EvSlacOptions
                {
                    PevId                     = new byte[17],
                    ParmCnfCollectionWindow   = TimeSpan.FromMilliseconds(200),
                    AttenCharCollectionWindow = TimeSpan.FromMilliseconds(400),
                },
                chip: evChip);

            await Task.WhenAll(ev.PairAsync(cts.Token), evse.WaitForMatchAsync(cts.Token));

            // Both PLC chips must have been programmed with the SLAC-negotiated key (AVLN-ready stage ran).
            Assert.Multiple(() =>
            {
                Assert.That(evChip.LastNid, Is.EqualTo(nid), "the EV chip must be keyed with the negotiated NID");
                Assert.That(evChip.LastNmk, Is.EqualTo(nmk));
                Assert.That(evseChip.LastNid, Is.EqualTo(nid), "the EVSE chip must be keyed with the negotiated NID");
                Assert.That(evseChip.LastNmk, Is.EqualTo(nmk));
            });
        }
    }
}
