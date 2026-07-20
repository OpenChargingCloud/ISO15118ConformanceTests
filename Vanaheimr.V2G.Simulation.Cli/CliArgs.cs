using Vanaheimr.V2G.Simulation.StateMachines;

namespace Vanaheimr.V2G.Simulation.Cli
{
    /// <summary>
    /// Hand-rolled flag parsing (no arg-parsing package anywhere in this repo) for the two subcommands:
    /// <c>evcc --connect host:port --protocol 2|20 --mode ac|dc [--tls]</c> and
    /// <c>secc --listen port --protocol 2|20 --mode ac|dc [--tls]</c>.
    /// No SDP — the address/port is always explicit (see the Phase 5 scope note in README.md).
    /// </summary>
    public sealed record CliArgs(
        Role Role, string? ConnectHost, int ConnectPort, int ListenPort,
        ProtocolVariant Protocol, PowerMode Mode, bool UseTls)
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
            bool tls = false;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--connect":
                        var parts = args[++i].Split(':');
                        if (parts.Length != 2 || !int.TryParse(parts[1], out connectPort))
                            throw new ArgumentException($"--connect expects host:port, got '{args[i]}'.");
                        connectHost = parts[0];
                        break;
                    case "--listen":
                        if (!int.TryParse(args[++i], out listenPort))
                            throw new ArgumentException($"--listen expects a port number, got '{args[i]}'.");
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
                    default:
                        throw new ArgumentException($"unknown argument '{args[i]}'.\n{Usage}");
                }
            }

            if (role == Role.Evcc && connectHost is null)
                throw new ArgumentException($"evcc requires --connect host:port.\n{Usage}");
            if (role == Role.Secc && listenPort == 0)
                throw new ArgumentException($"secc requires --listen port.\n{Usage}");

            return new CliArgs(role, connectHost, connectPort, listenPort, protocol, mode, tls);
        }

        public const string Usage =
            "usage: evcc --connect <host:port> --protocol 2|20 --mode ac|dc [--tls]\n" +
            "       secc --listen <port>       --protocol 2|20 --mode ac|dc [--tls]";
    }
}
