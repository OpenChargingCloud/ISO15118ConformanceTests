using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Authentication;
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

            // .NET backend: a supplied --server-cert (e.g. a CPO/SECC leaf chain a real EVCC's trust anchor
            // accepts) takes precedence over the fresh self-signed dev cert. With --require-client-cert the SECC
            // requires + (dev: accepts any) the EVCC's client certificate for mutual TLS.
            var (serverLeaf, serverChain) = LoadCertificateWithChain(args.ServerCertPath, args.ServerCertPass);
            using var devCert = args.TlsBackend == TlsBackend.Dotnet && serverLeaf is null ? CreateDevCertificate() : null;
            var dotnetTls = args.TlsBackend != TlsBackend.Dotnet ? null : new TlsOptions
            {
                ServerCertificate         = serverLeaf ?? devCert,
                ServerCertificateChain    = serverChain,
                EnabledSslProtocols       = SslProtocols.Tls12 | SslProtocols.Tls13,
                RequireClientCertificate  = args.RequireClientCert,
                ClientCertificateValidation = args.RequireClientCert ? (_, _, _, _) => true : null,
            };
            if (serverLeaf is not null)
                Console.WriteLine($"Presenting server certificate: {serverLeaf.Subject} (+{serverChain?.Count ?? 0} intermediate(s))"
                                  + (args.RequireClientCert ? "; requiring a client certificate (mutual TLS, dev: accept-any)" : ""));
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
                secc.PreferDynamicControlMode = args.PreferDynamic;
                await secc.RunAsync(stream);
                if (secc.PnCAuth is { } pnc)
                    Console.WriteLine($"Plug & Charge: contract {pnc.ContractSubject}; challenge {(pnc.ChallengeOk ? "OK" : "MISMATCH")}, " +
                                      $"digest {(pnc.DigestOk ? "OK" : "FAIL")}, signature {(pnc.SignatureOk ? "OK" : "FAIL")} " +
                                      $"({pnc.SignatureMethod}{(pnc.SignatureOk ? $", grammar={pnc.SignatureGrammar}" : "")}).");
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
            var iface   = ResolveInterface(args.Interface!);
            var noTls   = args.TlsBackend == TlsBackend.None;
            var server  = new SECC_SDPServer(BuildSeccSdpOptions(iface, tcpPort, noTls));
            // iface.LinkLocalIPAddress already carries the interface ScopeId on Linux; re-derive a scoped
            // address so the display shows the scope exactly once (not "…%2%2").
            var scoped  = new IPAddress(iface.LinkLocalIPAddress.GetAddressBytes(), iface.Index);
            Console.WriteLine($"SDP: advertising [{scoped}]:{tcpPort} ({(noTls ? "NoTLS" : "TLS")}) on {iface.Name}...");
            var advertiser = new SeccSdpAdvertiser(server);
            await advertiser.StartAsync();
            return advertiser;
        }

        /// <summary>
        /// Builds the SECC SDP-server options for the CLI. A <b>plaintext</b> SECC (<paramref name="noTls"/>)
        /// advertises <see cref="SDP_Security.NoTLS"/> and — crucially — sets
        /// <see cref="SECC_SDPServerOptions.RejectNoTlsRequests"/> to <c>false</c> so it actually answers a
        /// plaintext EVCC's SDP_Request; the option's TLS-deployment-oriented default (<c>true</c>) would
        /// otherwise silently drop it and make <c>--sdp</c> discovery appear broken. A TLS SECC advertises
        /// <see cref="SDP_Security.TLS"/> and keeps rejecting no-TLS downgrade requests.
        /// </summary>
        internal static SECC_SDPServerOptions BuildSeccSdpOptions(V2GNetworkInterface iface, int tcpPort, bool noTls)
            => new()
            {
                Interface           = iface,
                SeccPort            = (ushort) tcpPort,
                AcceptedVersions    = new HashSet<SDP_Version> { SDP_Version.ISO_15118_2, SDP_Version.ISO_15118_20 },
                OfferedSecurity     = noTls ? SDP_Security.NoTLS : SDP_Security.TLS,
                RejectNoTlsRequests = !noTls,
            };

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
                    var (clientLeaf, clientChain) = LoadCertificateWithChain(args.ClientCertPath, args.ClientCertPass);
                    var tlsOptions = new TlsOptions
                    {
                        ServerCertificateValidation = (_, _, _, _) => true,
                        // Negotiate TLS 1.2 or 1.3 so this interoperates with a peer in either mode (Josev's
                        // SECC serves TLS 1.2 unilateral by default, TLS 1.3 mutual with ENABLE_TLS_1_3=True).
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        ClientCertificate = clientLeaf,
                        ClientCertificateChain = clientChain,
                    };
                    if (clientLeaf is not null)
                        Console.WriteLine($"Presenting client certificate for mutual TLS: {clientLeaf.Subject} (+{clientChain?.Count ?? 0} intermediate(s))");
                    return await TcpV2GClient.ConnectAsync(host, port, tlsOptions);

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

        /// <summary>Loads a PKCS#12 certificate for TLS (client or server), splitting the private-key leaf from its
        /// intermediate CA chain so <c>SslStream</c> can send both. Returns (null, null) when <paramref name="path"/> is null.</summary>
        private static (X509Certificate2? Leaf, X509Certificate2Collection? Chain) LoadCertificateWithChain(string? path, string? password)
        {
            if (path is null)
                return (null, null);

            var all = X509CertificateLoader.LoadPkcs12CollectionFromFile(path, password, X509KeyStorageFlags.Exportable);
            var leaf = all.FirstOrDefault(c => c.HasPrivateKey) ?? all[0];
            var chain = new X509Certificate2Collection(all.Where(c => !ReferenceEquals(c, leaf)).ToArray());
            return (leaf, chain);
        }

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
