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
        /// Full -2 smart-charging loopback (AC, EIM): the SECC offers TWO SAScheduleTuples whose
        /// SalesTariffs are digitally signed into the response header (§7.9.2.5); the EVCC verifies the
        /// tariff signature (digest per tariff + ECDSA under the combined -2 grammar), picks the cheaper
        /// tuple (2: avg price level 1.5 vs 2.5), shapes its ChargingProfile to that tuple's 7.4/22-kW
        /// PMax steps, and the SECC validates the profile against the offer. External validation status
        /// (live 2026-07-22): a Josev EVCC consumed our signed offer + chose the cheap tuple, and our
        /// EVCC verified a real MO-Sub-CA2-signed Josev tariff — only our combined-grammar signing form
        /// has no external verifier (Josev's EVCC-side check is a code TODO).
        /// </summary>
        [Test]
        public async Task AcTariffSession_SignedTariffVerified_CheapestTupleProfiled()
        {
            using var tariffKey = System.Security.Cryptography.ECDsa.Create(
                System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            using var tariffPublic = System.Security.Cryptography.ECDsa.Create(
                tariffKey.ExportParameters(includePrivateParameters: false));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token);
                var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System) { TariffSignKey = tariffKey };
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token);

            var evcc = new Evcc2(evccStream, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
            {
                TariffVerifyKey = tariffPublic,
            };
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            var secc = await seccTask;
            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(evcc.Tariff, Is.Not.Null);
                Assert.That(evcc.Tariff!.TuplesOffered, Is.EqualTo(2));
                Assert.That(evcc.Tariff.SignaturePresent, Is.True);
                Assert.That(evcc.Tariff.DigestOk, Is.True, "every SalesTariff digest must match its fragment");
                Assert.That(evcc.Tariff.SignatureOk, Is.True, "the tariff ECDSA signature must verify");
                Assert.That(evcc.Tariff.SignatureGrammar, Is.EqualTo("iso2-msgdef"), "we sign in the spec/cbV2G form");
                Assert.That(evcc.Tariff.ChosenTupleId, Is.EqualTo(2), "the EV picks the cheaper tuple");
                Assert.That(evcc.Tariff.ProfileEntries, Is.EqualTo(2), "profile follows the two PMax steps");
                Assert.That(secc.ChargingProfileCheck, Is.Not.Null);
                Assert.That(secc.ChargingProfileCheck!.TupleIdOk, Is.True);
                Assert.That(secc.ChargingProfileCheck.WithinPMax, Is.True);
                Assert.That(secc.ChargingProfileCheck.TupleId, Is.EqualTo(2));
            });
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
                    "CN=UKSWI123456791A", contractKey, System.Security.Cryptography.HashAlgorithmName.SHA256)
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

            var evcc = new Evcc2(evccStream, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
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

        /// <summary>
        /// Pause/resume across two real TCP connections ([V2G2-740]): session 1 ends with
        /// <c>ChargingSession.Pause</c> (SECC parks the session id), session 2 reconnects, re-runs SAP, and
        /// rejoins with the old id — the SECC answers <c>OK_OldSessionJoined</c> and the session completes.
        /// </summary>
        [Test]
        public async Task AcSession_PauseThenResume_RejoinsOldSession()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            // ── session 1: run to a PAUSE stop ─
            var secc1Task = Task.Run(async () =>
            {
                using var s = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(s, ProtocolVariant.Iso15118_2, cts.Token);
                var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(s, cts.Token);
                return secc;
            }, cts.Token);

            byte[] sessionId;
            using (var evccStream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token))
            {
                await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token);
                var evcc = new Evcc2(evccStream, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
                {
                    StopMode = Vanaheimr.V2G.Iso15118_2.Generated.ChargingSession.Pause,
                };
                await evcc.RunAsync(cts.Token);
                sessionId = evcc.SessionId;
            }
            var secc1 = await secc1Task;
            Assert.That(secc1.Paused, Is.True, "the SECC must record the pause");
            Assert.That(secc1.SessionId, Is.EqualTo(sessionId));

            // ── session 2: reconnect and resume with the old id ─
            var secc2Task = Task.Run(async () =>
            {
                using var s = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(s, ProtocolVariant.Iso15118_2, cts.Token);
                var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System) { ResumeSessionId = sessionId };
                await secc.RunAsync(s, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream2 = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream2, ProtocolVariant.Iso15118_2, cts.Token);
            var evcc2 = new Evcc2(evccStream2, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
            {
                ResumeSessionId = sessionId,
            };
            await evcc2.RunAsync(cts.Token);
            var secc2 = await secc2Task;

            Assert.Multiple(() =>
            {
                Assert.That(evcc2.SessionSetupCode,
                    Is.EqualTo(Vanaheimr.V2G.Iso15118_2.Generated.ResponseCode.OK_OldSessionJoined),
                    "the resumed SessionSetup must rejoin the paused session");
                Assert.That(evcc2.SessionId, Is.EqualTo(sessionId), "the session id survives the pause");
                Assert.That(secc2.IsDone, Is.True);
                Assert.That(secc2.Paused, Is.False, "session 2 ends with Terminate");
            });
        }

        /// <summary>
        /// SECC-triggered renegotiation ([V2G2-841]) over loopback: the SECC's first charging-status response
        /// carries <c>EVSENotification.ReNegotiation</c>, the EVCC reacts with
        /// <c>PowerDeliveryReq(Renegotiate)</c> → a fresh ChargeParameterDiscovery → PowerDelivery(Start),
        /// and the session still completes normally.
        /// </summary>
        [Test]
        public async Task DcSession_SeccTriggeredRenegotiation_RunsToCompletion()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var s = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(s, ProtocolVariant.Iso15118_2, cts.Token);
                var secc = new Secc2(PowerMode.Dc, TimeSpan.FromSeconds(60), TimeProvider.System) { RequestRenegotiation = true };
                await secc.RunAsync(s, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token);
            var evcc = new Evcc2(evccStream, PowerMode.Dc, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            await evcc.RunAsync(cts.Token);
            var secc = await seccTask;

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(evcc.Renegotiations, Is.EqualTo(1), "the EVCC must react to the ReNegotiation notification");
                Assert.That(secc.Renegotiations, Is.EqualTo(1), "the SECC must see one PowerDelivery(Renegotiate)");
            });
        }

        /// <summary>EV-initiated renegotiation: same flow, but the EVCC opens it on its own.</summary>
        [Test]
        public async Task AcSession_EvInitiatedRenegotiation_RunsToCompletion()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var s = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(s, ProtocolVariant.Iso15118_2, cts.Token);
                var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(s, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token);
            var evcc = new Evcc2(evccStream, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
            {
                Renegotiate = true,
            };
            await evcc.RunAsync(cts.Token);
            var secc = await seccTask;

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(evcc.Renegotiations, Is.EqualTo(1));
                Assert.That(secc.Renegotiations, Is.EqualTo(1));
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

            var evcc = new Evcc2(evccStream, mode, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            await evcc.RunAsync(cts.Token);

            var secc = await seccTask;
            Assert.That(secc.IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }
    }
}
