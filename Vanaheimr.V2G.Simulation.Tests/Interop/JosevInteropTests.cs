using System.Net;
using System.Security.Authentication;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.Interop
{
    /// <summary>
    /// Tier-2 interop against <b>Josev</b> (SwitchEV/iso15118 or the EVerest fork). These are
    /// <see cref="ExplicitAttribute">[Explicit]</see> and env-var-gated, so they never run in the standard
    /// <c>dotnet test</c> suite (which must stay green offline). Bring a Josev endpoint up per
    /// <c>tools/interop-josev/README.md</c>, then run this fixture by category:
    /// <code>dotnet test --filter TestCategory=Interop</code>
    /// <para>Env vars: <c>V2G_INTEROP_SECC=host:port</c> (our EVCC → Josev SECC),
    /// <c>V2G_INTEROP_LISTEN=port</c> (Josev EVCC → our SECC), <c>V2G_INTEROP_PROTOCOL=2|20</c> (default 2),
    /// <c>V2G_INTEROP_MODE=ac|dc</c> (default ac), <c>V2G_INTEROP_TLS=1</c> (accept any server cert, dev only).</para>
    /// </summary>
    [TestFixture]
    [Category("Interop")]
    [Explicit("Requires a running Josev endpoint (see tools/interop-josev/README.md); never part of the offline CI run.")]
    public class JosevInteropTests
    {
        private static readonly TimeSpan PerMessageTimeout = TimeSpan.FromSeconds(5);

        [Test]
        public async Task OurEvcc_AgainstJosevSecc_RunsToCompletion()
        {
            var (host, port) = SeccEndpointOrIgnore();
            var (protocol, mode) = ProtocolAndMode();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var stream = await TcpV2GClient.ConnectAsync(host, port, DevTlsOrNull(), cts.Token);
            await SapHandshake.RunEvccSideAsync(stream, protocol, cts.Token);

            var exchanges = await RunEvccAsync(stream, protocol, mode, cts.Token);
            Assert.That(exchanges, Is.GreaterThan(0), "our EVCC exchanged at least one message with Josev's SECC");
        }

        [Test]
        public async Task JosevEvcc_AgainstOurSecc_RunsToCompletion()
        {
            var listenPort = ListenPortOrIgnore();
            var (protocol, mode) = ProtocolAndMode();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Any, listenPort), DevServerTlsOrNull());
            TestContext.Out.WriteLine($"Waiting for a Josev EVCC to connect on :{listenPort} ...");

            using var stream = await listener.AcceptAsync(cts.Token);
            await SapHandshake.RunSeccSideAsync(stream, protocol, cts.Token);

            var isDone = await RunSeccAsync(stream, protocol, mode, cts.Token);
            Assert.That(isDone, Is.True, "our SECC drove Josev's EVCC to the terminal session state");
        }

        // ── run helpers ────────────────────────────────────────────────────────────────────────────

        private static async Task<int> RunEvccAsync(Stream stream, ProtocolVariant protocol, PowerMode mode, CancellationToken ct)
        {
            if (protocol == ProtocolVariant.Iso15118_2)
            {
                var evcc = new Evcc2(stream, mode, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout);
                await evcc.RunAsync(ct);
                return evcc.Exchanges;
            }

            Evcc20Base evcc20 = mode == PowerMode.Dc
                ? new Evcc20Dc(stream, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout)
                : new Evcc20Ac(stream, TimeProvider.System, new TaskAsyncDelay(), PerMessageTimeout);
            await evcc20.RunAsync(ct);
            return evcc20.Exchanges;
        }

        private static async Task<bool> RunSeccAsync(Stream stream, ProtocolVariant protocol, PowerMode mode, CancellationToken ct)
        {
            if (protocol == ProtocolVariant.Iso15118_2)
            {
                var secc = new Secc2(mode, TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(stream, ct);
                return secc.IsDone;
            }

            Secc20Base secc20 = mode == PowerMode.Dc
                ? new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System)
                : new Secc20Ac(TimeSpan.FromSeconds(60), TimeProvider.System);
            await secc20.RunAsync(stream, ct);
            return secc20.IsDone;
        }

        // ── env plumbing ───────────────────────────────────────────────────────────────────────────

        private static (string Host, int Port) SeccEndpointOrIgnore()
        {
            var value = Environment.GetEnvironmentVariable("V2G_INTEROP_SECC");
            if (string.IsNullOrWhiteSpace(value))
                Assert.Ignore("set V2G_INTEROP_SECC=host:port (a running Josev SECC) to run this interop test.");

            var idx = value!.LastIndexOf(':');
            if (idx <= 0 || !int.TryParse(value[(idx + 1)..], out var port))
                throw new ArgumentException($"V2G_INTEROP_SECC must be host:port, got '{value}'.");
            return (value[..idx], port);
        }

        private static int ListenPortOrIgnore()
        {
            var value = Environment.GetEnvironmentVariable("V2G_INTEROP_LISTEN");
            if (string.IsNullOrWhiteSpace(value))
                Assert.Ignore("set V2G_INTEROP_LISTEN=port and point a Josev EVCC at it to run this interop test.");
            return int.TryParse(value, out var port) ? port : throw new ArgumentException($"V2G_INTEROP_LISTEN must be a port, got '{value}'.");
        }

        private static (ProtocolVariant, PowerMode) ProtocolAndMode()
        {
            var protocol = Environment.GetEnvironmentVariable("V2G_INTEROP_PROTOCOL") switch
            {
                "20" => ProtocolVariant.Iso15118_20,
                _ => ProtocolVariant.Iso15118_2,
            };
            var mode = Environment.GetEnvironmentVariable("V2G_INTEROP_MODE") == "dc" ? PowerMode.Dc : PowerMode.Ac;
            return (protocol, mode);
        }

        private static TlsOptions? DevTlsOrNull()
            => Environment.GetEnvironmentVariable("V2G_INTEROP_TLS") == "1"
                   ? new TlsOptions
                     {
                         ServerCertificateValidation = (_, _, _, _) => true, // dev only: accept any Josev server cert
                         // The one place a permissive set is right: this probes a third-party SECC whose version
                         // we do not control (Josev serves TLS 1.2 unilateral by default, 1.3 mutual only with
                         // ENABLE_TLS_1_3=True), and V2G_INTEROP_PROTOCOL picks -2 or -20 at runtime. Matches the
                         // dev CLI (Simulation.Cli/Program.cs). This is an interop probe, not a conformance path.
                         EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                     }
                   : null;

        private static TlsOptions? DevServerTlsOrNull()
            => null; // our SECC serving Josev over TLS would need a checked-in test cert; start with plain TCP (-2 EIM).
    }
}
