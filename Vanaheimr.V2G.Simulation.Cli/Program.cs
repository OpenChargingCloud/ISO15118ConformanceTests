using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Cli
{
    /// <summary>
    /// One-shot EVCC/SECC session runner over a real TCP (+ optional TLS) connection — no SDP, the
    /// address/port is always explicit. See <see cref="CliArgs.Usage"/> for the two subcommands.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            CliArgs cliArgs;
            try
            {
                cliArgs = CliArgs.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                if (cliArgs.Role == Role.Secc)
                    await RunSeccAsync(cliArgs);
                else
                    await RunEvccAsync(cliArgs);

                Console.WriteLine($"\n✓ Session complete in {sw.ElapsedMilliseconds} ms.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\n✗ Session aborted: {ex.Message}");
                return 1;
            }
        }

        private static async Task RunSeccAsync(CliArgs args)
        {
            using var cert = args.UseTls ? CreateDevCertificate() : null;
            var tls = cert is null ? null : new TlsOptions { ServerCertificate = cert };

            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Any, args.ListenPort), tls);
            Console.WriteLine($"SECC listening on {listener.LocalEndpoint} (protocol {(args.Protocol == ProtocolVariant.Iso15118_2 ? "-2" : "-20")}, " +
                               $"{(args.Mode == PowerMode.Ac ? "AC" : "DC")}, TLS {(args.UseTls ? "on" : "off")})...");

            using var stream = await listener.AcceptAsync();
            await SapHandshake.RunSeccSideAsync(stream, args.Protocol);

            if (args.Protocol == ProtocolVariant.Iso15118_2)
            {
                var secc = new Secc2(args.Mode, TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(stream);
            }
            else
            {
                Secc20Base secc = args.Mode == PowerMode.Dc
                    ? new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System)
                    : new Secc20Ac(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(stream);
            }
        }

        private static async Task RunEvccAsync(CliArgs args)
        {
            TlsOptions? tls = null;
            if (args.UseTls)
            {
                // Dev CLI only: this simulator has no out-of-band way to learn the SECC's dev-certificate
                // thumbprint ahead of time (unlike the test suite, which generates both sides together and
                // pins on it — see TlsLoopbackTests), so it accepts whatever the SECC presents. Never do
                // this against a real endpoint.
                Console.WriteLine("WARNING: accepting any TLS server certificate — dev CLI only, never do this against a real SECC.");
                tls = new TlsOptions { ServerCertificateValidation = (_, _, _, _) => true };
            }

            using var stream = await TcpV2GClient.ConnectAsync(args.ConnectHost!, args.ConnectPort, tls);
            await SapHandshake.RunEvccSideAsync(stream, args.Protocol);

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

        // Dev-only self-signed certificate — generated fresh per run, never written to disk, never used
        // outside this CLI. See tools/exificient-ref and tools/cbv2g-ref for the repo's other "dev tool,
        // not for production" conventions.
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
