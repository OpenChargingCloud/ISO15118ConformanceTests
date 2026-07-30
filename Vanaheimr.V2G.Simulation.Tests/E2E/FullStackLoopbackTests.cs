using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

using NUnit.Framework;

using Org.BouncyCastle.Tls;

using org.GraphDefined.Vanaheimr.Hermod.Ethernet;

using cloud.charging.open.protocols.ISO15118.PKI;
using cloud.charging.open.protocols.ISO15118.SLAC.Avln;
using cloud.charging.open.protocols.ISO15118.SLAC.StateMachine;
using cloud.charging.open.protocols.ISO15118.SLAC.Transport;

using Vanaheimr.V2G.Simulation.Discovery;
using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.Slac;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.TestData;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.E2E
{
    /// <summary>
    /// The whole ISO 15118 entry sequence end-to-end in one run:
    /// <b>SLAC → SDP → TLS → SAP → session</b>, EV vs. SECC, loopback-only.
    /// <list type="number">
    ///   <item><b>SLAC</b>: a real EV↔EVSE match over loopback UDP; both PLC chips are keyed with the
    ///         negotiated NID/NMK.</item>
    ///   <item><b>SDP</b>: the <see cref="ISeccDiscovery"/> seam yields the SECC's TCP endpoint. Uses
    ///         <see cref="FixedSeccDiscovery"/> — the live multicast <c>SdpSeccDiscovery</c> is the drop-in
    ///         for real deployments (same-host multicast isn't deterministic, see docs/pki-model.md).</item>
    ///   <item><b>TLS</b>: -20-faithful <b>mutual</b> TLS via the BouncyCastle backend (TLS 1.3, secp521r1;
    ///         SECC leaf = server, Vehicle leaf = client, shared V2G Root).</item>
    ///   <item><b>SAP + session</b>: SupportedAppProtocol negotiates -20, then a full DC happy path runs to
    ///         SessionStop over the authenticated stream.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class FullStackLoopbackTests
    {
        private static int FreeUdpPort()
        {
            using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint) probe.Client.LocalEndPoint!).Port;
        }

        private static MACAddress Mac(params byte[] bytes) => MACAddress.FromPhysicalAddress(new PhysicalAddress(bytes));

        [Test]
        public async Task Slac_Sdp_Tls_Session_RunEndToEnd_Iso20Dc()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // ── PKI for the -20-faithful mutual TLS (secp521r1) ────────────────────────────────────
            var (seccBcTls, evccBcTls) = V2GBcTlsFixture.MutualTls(V2GAlgorithm.EcdsaP521, SignatureScheme.ecdsa_secp521r1_sha512);

            // ── SLAC transports (loopback UDP) + PLC chips ─────────────────────────────────────────
            var slacEvseEp = new IPEndPoint(IPAddress.Loopback, FreeUdpPort());
            await using var slacEvseTransport = new UdpSlacTransport(Mac(0x00, 0x11, 0x22, 0x33, 0x44, 0x55), slacEvseEp);
            await using var slacEvTransport   = new UdpSlacTransport(Mac(0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF),
                                                                     new IPEndPoint(IPAddress.Loopback, 0),
                                                                     bootstrapPeers: [slacEvseEp]);
            var nid = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
            var nmk = Enumerable.Range(0, 16).Select(i => (byte) i).ToArray();
            var evChip   = new SimulatedChipController();
            var evseChip = new SimulatedChipController();

            // ── The SECC's TCP endpoint (what SDP would advertise) ─────────────────────────────────
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0), seccBcTls);
            ISeccDiscovery discovery = new FixedSeccDiscovery(
                new SeccEndpoint(IPAddress.Loopback, listener.LocalEndpoint.Port, Tls: true));

            // EVSE-side SLAC must be listening before the EV starts pairing.
            await using var slacEvse = new SlacEvseStage(slacEvseTransport,
                new EvseSlacOptions { EvseId = new byte[17], Nid = nid, Nmk = nmk }, chip: evseChip);
            await slacEvse.StartAsync(cts.Token);

            // ── SECC role: SLAC → accept(mTLS) → SAP → session ─────────────────────────────────────
            var seccRole = Task.Run(async () =>
            {
                var slac = await slacEvse.WaitForMatchAsync(cts.Token);

                using var stream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(stream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(stream, cts.Token);
                return (slac, secc);
            }, cts.Token);

            // ── EV role: SLAC → discover → connect(mTLS) → SAP → session ───────────────────────────
            var evRole = Task.Run(async () =>
            {
                var slacEv = new SlacEvStage(slacEvTransport,
                    new EvSlacOptions
                    {
                        PevId                     = new byte[17],
                        ParmCnfCollectionWindow   = TimeSpan.FromMilliseconds(200),
                        AttenCharCollectionWindow = TimeSpan.FromMilliseconds(400),
                    },
                    chip: evChip);
                var slac = await slacEv.PairAsync(cts.Token);

                var endpoint = await discovery.DiscoverAsync(cts.Token);

                using var stream = await TcpV2GClient.ConnectAsync(endpoint.Host, endpoint.Port, evccBcTls, cts.Token);
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, cts.Token);
                var evcc = new Evcc20Dc(stream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
                await evcc.RunAsync(cts.Token);
                return (slac, evcc);
            }, cts.Token);

            await Task.WhenAll(seccRole, evRole);
            var (seccSlac, seccMachine) = await seccRole;
            var (evSlac, evccMachine)   = await evRole;

            Assert.Multiple(() =>
            {
                // SLAC: both sides agreed on the key, both chips keyed.
                Assert.That(evSlac.Nid, Is.EqualTo(nid), "SLAC: EV negotiated NID");
                Assert.That(seccSlac.Nid, Is.EqualTo(nid), "SLAC: EVSE negotiated NID");
                Assert.That(evChip.LastNmk, Is.EqualTo(nmk), "SLAC: EV chip keyed");
                Assert.That(evseChip.LastNmk, Is.EqualTo(nmk), "SLAC: EVSE chip keyed");

                // Session: ran to completion over the discovered + mutually-authenticated stream.
                Assert.That(seccMachine.IsDone, Is.True, "session reached its terminal state");
                Assert.That(evccMachine.Exchanges, Is.GreaterThan(0), "the EV exchanged messages");
            });
        }
    }
}
