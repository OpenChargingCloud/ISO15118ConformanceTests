using Vanaheimr.V2G.Simulation.StateMachines;

namespace Vanaheimr.V2G.Simulation.Cli
{
    /// <summary>
    /// Hand-rolled flag parsing (no arg-parsing package anywhere in this repo) for the two subcommands.
    /// Beyond protocol/mode/TLS it selects the optional front stages and the TLS backend:
    /// <list type="bullet">
    ///   <item><c>--tls-backend dotnet|bc</c> — .NET SslStream (default when <c>--tls</c>) or BouncyCastle
    ///         (-20-faithful mutual TLS; needs <c>--pki-dir</c>).</item>
    ///   <item><c>--slac</c> — run a SLAC pairing stage first (SECC <c>--slac-listen</c>, EVCC <c>--slac-peer</c>).</item>
    ///   <item><c>--sdp</c> — discover/advertise the endpoint via SDP on <c>--interface</c> instead of a fixed one.</item>
    /// </list>
    /// </summary>
    public sealed record CliArgs(
        Role Role, string? ConnectHost, int ConnectPort, int ListenPort,
        ProtocolVariant Protocol, PowerMode Mode, TlsBackend TlsBackend,
        bool UseSdp, string? Interface,
        bool UseSlac, int SlacListenPort, string? SlacPeerHost, int SlacPeerPort,
        string? PkiDir)
    {
        public static CliArgs Parse(string[] args)
        {
            if (args.Length == 0 || (args[0] != "evcc" && args[0] != "secc"))
                throw new ArgumentException(Usage);

            var role = args[0] == "evcc" ? Role.Evcc : Role.Secc;
            string? connectHost = null;
            int connectPort = 0, listenPort = 0;
            var protocol = ProtocolVariant.Iso15118_2;
            var mode = PowerMode.Ac;
            var backend = TlsBackend.None;
            bool tls = false;
            bool useSdp = false, useSlac = false;
            string? iface = null, slacPeerHost = null, pkiDir = null;
            int slacListenPort = 0, slacPeerPort = 0;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--connect":
                        (connectHost, connectPort) = SplitHostPort(args[++i], "--connect");
                        break;
                    case "--listen":
                        listenPort = ParsePort(args[++i], "--listen");
                        break;
                    case "--protocol":
                        protocol = args[++i] switch
                        {
                            "2" => ProtocolVariant.Iso15118_2,
                            "20" => ProtocolVariant.Iso15118_20,
                            var v => throw new ArgumentException($"--protocol expects 2 or 20, got '{v}'."),
                        };
                        break;
                    case "--mode":
                        mode = args[++i] switch
                        {
                            "ac" => PowerMode.Ac,
                            "dc" => PowerMode.Dc,
                            var v => throw new ArgumentException($"--mode expects ac or dc, got '{v}'."),
                        };
                        break;
                    case "--tls":
                        tls = true;
                        break;
                    case "--tls-backend":
                        backend = args[++i] switch
                        {
                            "dotnet" => TlsBackend.Dotnet,
                            "bc" or "bouncycastle" => TlsBackend.BouncyCastle,
                            var v => throw new ArgumentException($"--tls-backend expects dotnet or bc, got '{v}'."),
                        };
                        break;
                    case "--sdp":
                        useSdp = true;
                        break;
                    case "--interface":
                        iface = args[++i];
                        break;
                    case "--slac":
                        useSlac = true;
                        break;
                    case "--slac-listen":
                        slacListenPort = ParsePort(args[++i], "--slac-listen");
                        break;
                    case "--slac-peer":
                        (slacPeerHost, slacPeerPort) = SplitHostPort(args[++i], "--slac-peer");
                        break;
                    case "--pki-dir":
                        pkiDir = args[++i];
                        break;
                    default:
                        throw new ArgumentException($"unknown argument '{args[i]}'.\n{Usage}");
                }
            }

            // --tls is shorthand for the .NET backend; --tls-backend wins if both are given.
            if (backend == TlsBackend.None && tls)
                backend = TlsBackend.Dotnet;

            Validate(role, connectHost, listenPort, backend, useSdp, iface, useSlac, slacListenPort, slacPeerHost, pkiDir);

            return new CliArgs(role, connectHost, connectPort, listenPort, protocol, mode, backend,
                               useSdp, iface, useSlac, slacListenPort, slacPeerHost, slacPeerPort, pkiDir);
        }

        private static void Validate(Role role, string? connectHost, int listenPort, TlsBackend backend,
                                     bool useSdp, string? iface, bool useSlac, int slacListenPort,
                                     string? slacPeerHost, string? pkiDir)
        {
            if (role == Role.Evcc && connectHost is null && !useSdp)
                throw new ArgumentException($"evcc requires --connect host:port (or --sdp --interface <name>).\n{Usage}");
            if (role == Role.Secc && listenPort == 0)
                throw new ArgumentException($"secc requires --listen port.\n{Usage}");

            if (backend == TlsBackend.BouncyCastle && pkiDir is null)
                throw new ArgumentException("--tls-backend bc requires --pki-dir <dir> (shared V2G certificate material).");

            if (useSdp && iface is null)
                throw new ArgumentException("--sdp requires --interface <name> (the V2G network interface).");

            if (useSlac && role == Role.Evcc && slacPeerHost is null)
                throw new ArgumentException("evcc --slac requires --slac-peer <host:port> (the EVSE's SLAC endpoint).");
            if (useSlac && role == Role.Secc && slacListenPort == 0)
                throw new ArgumentException("secc --slac requires --slac-listen <port>.");
        }

        private static (string Host, int Port) SplitHostPort(string value, string flag)
        {
            var idx = value.LastIndexOf(':');
            if (idx <= 0 || !int.TryParse(value[(idx + 1)..], out var port))
                throw new ArgumentException($"{flag} expects host:port, got '{value}'.");
            return (value[..idx], port);
        }

        private static int ParsePort(string value, string flag)
            => int.TryParse(value, out var port) ? port : throw new ArgumentException($"{flag} expects a port number, got '{value}'.");

        public const string Usage =
            "usage: evcc --connect <host:port> --protocol 2|20 --mode ac|dc [tls/stage options]\n" +
            "       secc --listen  <port>      --protocol 2|20 --mode ac|dc [tls/stage options]\n" +
            "  TLS:   --tls | --tls-backend dotnet|bc   (bc = -20-faithful mutual TLS, needs --pki-dir <dir>)\n" +
            "  SDP:   --sdp --interface <name>          (discover/advertise instead of a fixed endpoint)\n" +
            "  SLAC:  --slac  (secc: --slac-listen <port>; evcc: --slac-peer <host:port>)";
    }
}
