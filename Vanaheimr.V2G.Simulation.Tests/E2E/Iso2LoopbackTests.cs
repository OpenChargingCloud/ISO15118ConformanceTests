using System.Net;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.E2E
{
    /// <summary>
    /// Real TCP, loopback-only, end-to-end: our EVCC talks to our SECC over an actual socket (OS-assigned
    /// free port), starting with the SAP handshake, then a full ISO 15118-2 AC or DC happy path to
    /// SessionStop. Proves codec + V2GTP framing + SAP + sequencing all work together over a real
    /// transport, not just in-process method calls.
    /// </summary>
    [TestFixture]
    public class Iso2LoopbackTests
    {
        [Test]
        public async Task AcSession_RunsToCompletion()
        {
            await RunSessionAsync(PowerMode.Ac);
        }

        [Test]
        public async Task DcSession_RunsToCompletion()
        {
            await RunSessionAsync(PowerMode.Dc);
        }

        /// <summary>
        /// Full -2 Plug &amp; Charge loopback (AC): the EVCC pays via Contract — PaymentDetails with its
        /// contract chain, a signed AuthorizationReq (Josev form), and one signed MeteringReceiptReq per
        /// charging-status cycle (the SECC demands receipts on Contract sessions) — and the SECC verifies
        /// everything via its dual-grammar paths.
        /// </summary>
        [Test]
        public async Task AcPncSession_SignedAuthAndMeteringReceipts_VerifyAtSecc()
        {
            using var contractKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            using var contract = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                    "CN=UKTEST000000001A", contractKey, System.Security.Cryptography.HashAlgorithmName.SHA256)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token);
                var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token);

            var evcc = new Evcc2(evccStream, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(), TimeSpan.FromSeconds(2))
            {
                Pnc = new Vanaheimr.V2G.Simulation.StateMachines.Iso20.PncEvccOptions(contract.RawData, new[] { contract.RawData }, contractKey),
            };
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            var secc = await seccTask;
            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(evcc.AuthorizationMode, Is.EqualTo("pnc-signed"));
                Assert.That(secc.PnCAuth, Is.Not.Null);
                Assert.That(secc.PnCAuth!.ChallengeOk, Is.True);
                Assert.That(secc.PnCAuth.DigestOk, Is.True);
                Assert.That(secc.PnCAuth.SignatureOk, Is.True);
                Assert.That(secc.PnCAuth.SignatureGrammar, Is.EqualTo("xmldsig-standalone"));
                Assert.That(evcc.MeteringReceiptsSent, Is.EqualTo(1),
                    "the SECC demands exactly one receipt per session (a Josev EVCC loops forever otherwise)");
                Assert.That(secc.MeteringReceipts, Has.Count.EqualTo(1));
                Assert.That(secc.MeteringReceipts, Has.All.Matches<Iso2ReceiptResult>(
                    r => r.DigestOk && r.SignatureOk && r.SignatureGrammar == "xmldsig-standalone"));
            });
        }

        private static async Task RunSessionAsync(PowerMode mode)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token);

                var secc = new Secc2(mode, TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token);

            var evcc = new Evcc2(evccStream, mode, TimeProvider.System, new ImmediateAsyncDelay(), TimeSpan.FromSeconds(2));
            await evcc.RunAsync(cts.Token);

            var secc = await seccTask;
            Assert.That(secc.IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }
    }
}
