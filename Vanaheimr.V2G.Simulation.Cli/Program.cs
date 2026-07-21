using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using org.GraphDefined.Vanaheimr.Hermod.Ethernet;

using cloud.charging.open.protocols.ISO15118.NetworkInterfaces;
using cloud.charging.open.protocols.ISO15118.SDP.Client;
using cloud.charging.open.protocols.ISO15118.SDP.Messages;
using cloud.charging.open.protocols.ISO15118.SDP.Server;
using cloud.charging.open.protocols.ISO15118.SLAC.StateMachine;
using cloud.charging.open.protocols.ISO15118.SLAC.Transport;

using Vanaheimr.V2G.Simulation.Discovery;
using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.Slac;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Timing;
using Vanaheimr.V2G.Simulation.Transport;
using Vanaheimr.V2G.Simulation.Transport.BouncyCastle;

namespace Vanaheimr.V2G.Simulation.Cli
{
    /// <summary>
    /// One-shot EVCC/SECC session runner with selectable front stages (SLAC, SDP) and TLS backend
    /// (.NET SslStream or BouncyCastle). See <see cref="CliArgs.Usage"/>.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            CliArgs cliArgs;
            try { cliArgs = CliArgs.Parse(args); }
            catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }

            var sw = Stopwatch.StartNew();
            try
            {
                if (cliArgs.Role == Role.Secc) await RunSeccAsync(cliArgs);
                else                            await RunEvccAsync(cliArgs);

                Console.WriteLine($"\n✓ Session complete in {sw.ElapsedMilliseconds} ms.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\n✗ Session aborted: {ex.Message}");
                return 1;
            }
        }

        // ── SECC ───────────────────────────────────────────────────────────────────────────────────

        private static async Task RunSeccAsync(CliArgs args)
        {
            if (args.UseSlac)
                await RunSeccSlacAsync(args);

            using var devCert = args.TlsBackend == TlsBackend.Dotnet ? CreateDevCertificate() : null;
            var dotnetTls = devCert is null ? null : new TlsOptions { ServerCertificate = devCert };
            var bcTls     = args.TlsBackend == TlsBackend.BouncyCastle ? CliPki.GenerateSeccOptions(args.PkiDir!) : null;

            using var listener = bcTls is not null
                                     ? new TcpV2GListener(new IPEndPoint(IPAddress.IPv6Any, args.ListenPort), bcTls)
                                     : new TcpV2GListener(new IPEndPoint(IPAddress.IPv6Any, args.ListenPort), dotnetTls);

            Console.WriteLine($"SECC listening on {listener.LocalEndpoint} (protocol {ProtocolName(args.Protocol)}, " +
                              $"{ModeName(args.Mode)}, TLS {args.TlsBackend})...");

            await using var sdp = args.UseSdp ? await StartSeccSdpAsync(args, listener.LocalEndpoint.Port) : null;

            using var stream = await listener.AcceptAsync();
            await SapHandshake.RunSeccSideAsync(stream, args.Protocol, mode: args.Mode);
            await RunSeccSessionAsync(stream, args);
        }

        private static async Task RunSeccSessionAsync(Stream stream, CliArgs args)
        {
            if (args.Protocol == ProtocolVariant.Iso15118_2)
                await new Secc2(args.Mode, TimeSpan.FromSeconds(60), TimeProvider.System).RunAsync(stream);
            else
            {
                Secc20Base secc = args.Mode == PowerMode.Dc
                    ? new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System)
                    : new Secc20Ac(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(stream);
            }
        }

        private static async Task RunSeccSlacAsync(CliArgs args)
        {
            await using var transport = new UdpSlacTransport(RandomMac(), new IPEndPoint(IPAddress.Any, args.SlacListenPort));
            await using var slac = new SlacEvseStage(transport,
                new EvseSlacOptions { EvseId = new byte[17], Nid = RandomNumberGenerator.GetBytes(7), Nmk = RandomNumberGenerator.GetBytes(16) });
            await slac.StartAsync();
            Console.WriteLine($"SLAC: EVSE listening on UDP :{args.SlacListenPort} for a PEV...");
            var result = await slac.WaitForMatchAsync();
            Console.WriteLine($"SLAC: paired (NID {Convert.ToHexString(result.Nid)}).");
        }

        private static async Task<SeccSdpAdvertiser> StartSeccSdpAsync(CliArgs args, int tcpPort)
        {
            var iface = ResolveInterface(args.Interface!);
            var server = new SECC_SDPServer(new SECC_SDPServerOptions
            {
                Interface        = iface,
                SeccPort         = (ushort) tcpPort,
                AcceptedVersions = new HashSet<SDP_Version> { SDP_Version.ISO_15118_2, SDP_Version.ISO_15118_20 },
                OfferedSecurity  = args.TlsBackend == TlsBackend.None ? SDP_Security.NoTLS : SDP_Security.TLS,
            });
            Console.WriteLine($"SDP: advertising [{iface.LinkLocalIPAddress}%{iface.Index}]:{tcpPort} on {iface.Name}...");
            var advertiser = new SeccSdpAdvertiser(server);
            await advertiser.StartAsync();
            return advertiser;
        }

        // ── EVCC ───────────────────────────────────────────────────────────────────────────────────

        private static async Task RunEvccAsync(CliArgs args)
        {
            if (args.UseSlac)
                await RunEvccSlacAsync(args);

            var (host, port) = await ResolveEvccEndpointAsync(args);

            using var stream = await ConnectEvccAsync(args, host, port);
            await SapHandshake.RunEvccSideAsync(stream, args.Protocol, mode: args.Mode);
            await RunEvccSessionAsync(stream, args);
        }

        private static async Task<Stream> ConnectEvccAsync(CliArgs args, string host, int port)
        {
            switch (args.TlsBackend)
            {
                case TlsBackend.BouncyCastle:
                    return await TcpV2GClient.ConnectAsync(host, port, CliPki.LoadEvccOptions(args.PkiDir!));

                case TlsBackend.Dotnet:
                    // Dev CLI only: no out-of-band way to learn the SECC dev-cert thumbprint, so accept any.
                    Console.WriteLine("WARNING: accepting any TLS server certificate — dev CLI only, never against a real SECC.");
                    return await TcpV2GClient.ConnectAsync(host, port, new TlsOptions { ServerCertificateValidation = (_, _, _, _) => true });

                default:
                    return await TcpV2GClient.ConnectAsync(host, port);
            }
        }

        private static async Task RunEvccSessionAsync(Stream stream, CliArgs args)
        {
            if (args.Protocol == ProtocolVariant.Iso15118_2)
            {
                var evcc = new Evcc2(stream, args.Mode, TimeProvider.System, new TaskAsyncDelay(), TimeSpan.FromSeconds(2));
                await evcc.RunAsync();
                Console.WriteLine($"  {evcc.Exchanges} exchanges, {evcc.BytesOnWire} bytes on the wire (request side).");
            }
            else
            {
                Evcc20Base evcc = args.Mode == PowerMode.Dc
                    ? new Evcc20Dc(stream, TimeProvider.System, new TaskAsyncDelay(), TimeSpan.FromSeconds(2))
                    : new Evcc20Ac(stream, TimeProvider.System, new TaskAsyncDelay(), TimeSpan.FromSeconds(2));
                await evcc.RunAsync();
                Console.WriteLine($"  {evcc.Exchanges} exchanges, {evcc.BytesOnWire} bytes on the wire (request side).");
            }
        }

        private static async Task<(string Host, int Port)> ResolveEvccEndpointAsync(CliArgs args)
        {
            if (!args.UseSdp)
                return (args.ConnectHost!, args.ConnectPort);

            var iface = ResolveInterface(args.Interface!);
            var discovery = new SdpSeccDiscovery(new EVCC_SDPClientOptions
            {
                Interface         = iface,
                RequestedSecurity = args.TlsBackend == TlsBackend.None ? SDP_Security.NoTLS : SDP_Security.TLS,
            });
            Console.WriteLine($"SDP: discovering the SECC on {iface.Name}...");
            var endpoint = await discovery.DiscoverAsync();
            Console.WriteLine($"SDP: found SECC at [{endpoint.Host}]:{endpoint.Port} (TLS {endpoint.Tls}).");
            return (endpoint.Host, endpoint.Port);
        }

        private static async Task RunEvccSlacAsync(CliArgs args)
        {
            var peer = new IPEndPoint(IPAddress.Parse(args.SlacPeerHost!), args.SlacPeerPort);
            await using var transport = new UdpSlacTransport(RandomMac(), new IPEndPoint(IPAddress.Any, 0), bootstrapPeers: [peer]);
            var slac = new SlacEvStage(transport, new EvSlacOptions { PevId = new byte[17] });
            Console.WriteLine($"SLAC: pairing with EVSE at {peer}...");
            var result = await slac.PairAsync();
            Console.WriteLine($"SLAC: paired (NID {Convert.ToHexString(result.Nid)}).");
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────────

        private static V2GNetworkInterface ResolveInterface(string name)
            => new SystemV2GNetworkInterfaceProvider().FindByName(name)
               ?? throw new ArgumentException($"no V2G-capable network interface named '{name}' found.");

        private static MACAddress RandomMac() => MACAddress.FromPhysicalAddress(new PhysicalAddress(RandomNumberGenerator.GetBytes(6)));

        private static string ProtocolName(ProtocolVariant p) => p == ProtocolVariant.Iso15118_2 ? "-2" : "-20";
        private static string ModeName(PowerMode m) => m == PowerMode.Ac ? "AC" : "DC";

        private static X509Certificate2 CreateDevCertificate()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var req = new CertificateRequest("CN=localhost", ecdsa, HashAlgorithmName.SHA256);
            var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
            Console.WriteLine("Using a fresh self-signed DEV certificate — not for production use.");
            return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), password: null, X509KeyStorageFlags.Exportable);
        }
    }
}
