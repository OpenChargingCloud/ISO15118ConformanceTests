using System.Net;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.E2E
{
    /// <summary>
    /// Real TCP, loopback-only, end-to-end for ISO 15118-20: SAP negotiates -20, then a full DC or AC
    /// happy path runs to SessionStop across the three interleaved message sets (CommonMessages/DC/AC),
    /// each auto-detected per frame by <see cref="Vanaheimr.V2G.Tp.V2GTPDispatcher"/>.
    /// </summary>
    [TestFixture]
    public class Iso20LoopbackTests
    {
        [Test]
        public async Task DcSession_RunsToCompletion()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);

                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            var secc = await seccTask;
            Assert.That(secc.IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }

        /// <summary>
        /// -20 smart-charging loopback (DC, Scheduled mode): with a tariff key the SECC's
        /// ScheduleExchangeRes carries the rich <c>AbsolutePriceSchedule</c> (power-banded EUR/kWh price
        /// rule stacks) instead of the flat PriceLevelSchedule, digitally signed into the response header
        /// (ECDSA-P521/SHA-512, the -20 mandatory suite); the EVCC verifies digest + signature. NOTE: the
        /// -20 signature half stays self-consistent in-repo validation — no external implementation signs
        /// or verifies -20 price schedules (unlike -2, where Josev MO-signs its SalesTariff and gave our
        /// verify path a live oracle); a live Josev AC EVCC did consume this signed schedule and complete.
        /// </summary>
        [Test]
        public async Task DcTariffSession_SignedAbsolutePriceSchedule_VerifiesAtEv()
        {
            using var tariffKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP521);
            using var tariffPublic = System.Security.Cryptography.ECDsa.Create(
                tariffKey.ExportParameters(includePrivateParameters: false));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);

                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System) { TariffSignKey = tariffKey };
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
            {
                TariffVerifyKey = tariffPublic,
            };
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            var secc = await seccTask;
            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(evcc.Tariff, Is.Not.Null, "the EV must see the signed AbsolutePriceSchedule");
                Assert.That(evcc.Tariff!.SignaturePresent, Is.True);
                Assert.That(evcc.Tariff.DigestOk, Is.True, "the schedule digest must match its fragment");
                Assert.That(evcc.Tariff.SignatureOk, Is.True, "the P-521/SHA-512 signature must verify");
            });
        }

        /// <summary>
        /// Full Plug &amp; Charge loopback: the EVCC (with contract credentials) signs its AuthorizationReq in
        /// the Josev interop form (<see cref="XmlDsigInteropSign"/>) and the SECC's live verify path accepts it
        /// via the standalone-xmldsig fallback — challenge echo, reference digest, and ECDSA signature all OK.
        /// This is the loopback analogue of the live forward PnC run (our EVCC → Josev SECC).
        /// </summary>
        [Test]
        public async Task DcPncSession_SignedAuthorization_VerifiesAtSecc()
        {
            using var contractKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            var certReq = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                "CN=LoopbackContract", contractKey, System.Security.Cryptography.HashAlgorithmName.SHA256);
            using var contract = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);

                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
            {
                Pnc = new PncEvccOptions(contract.RawData, new[] { contract.RawData }, contractKey),
            };
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            var secc = await seccTask;
            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(evcc.AuthorizationMode, Is.EqualTo("pnc-signed"));
                Assert.That(secc.PnCAuth, Is.Not.Null, "the SECC must have seen a PnC AuthorizationReq");
                Assert.That(secc.PnCAuth!.ChallengeOk, Is.True, "GenChallenge must echo");
                Assert.That(secc.PnCAuth.DigestOk, Is.True, "reference digest (SHA-256) must match");
                Assert.That(secc.PnCAuth.SignatureOk, Is.True, "ECDSA signature must verify");
                Assert.That(secc.PnCAuth.SignatureGrammar, Is.EqualTo("xmldsig-standalone"),
                    "the EVCC signs in the Josev interop form");
            });
        }

        /// <summary>
        /// Full contract provisioning over loopback TCP: the EVCC (with a P-521 OEM provisioning identity)
        /// sends a signed CertificateInstallationReq, the SECC verifies the OEM signature, issues a fresh
        /// P-521 contract cert with the private scalar ECDH/AES-GCM-wrapped for the OEM key, signs the
        /// SignedInstallationData with its CPS leaf — and the EVCC verifies that signature and unwraps a
        /// working contract key. The session then continues through authorization to SessionStop.
        /// </summary>
        [Test]
        public async Task DcCertInstallSession_ProvisionsAWorkingContractKey()
        {
            using var oemSignKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP521);
            var oemReq = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                "CN=LoopbackOEMProv", oemSignKey, System.Security.Cryptography.HashAlgorithmName.SHA512);
            using var oemCert = oemReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
            using var oemEcdh = System.Security.Cryptography.ECDiffieHellman.Create(
                oemSignKey.ExportParameters(includePrivateParameters: true));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
            {
                CertInstallRequest = new CertInstallEvccOptions(oemCert.RawData, new[] { oemCert.RawData }, oemSignKey, oemEcdh),
            };
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            var secc = await seccTask;
            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(secc.CertInstall, Is.Not.Null);
                Assert.That(secc.CertInstall!.SignatureOk, Is.True, "the EVCC's OEM signature must verify at the SECC");
                Assert.That(secc.CertInstall.EncryptedForOem, Is.True, "a P-521 OEM key gets a real ECDH wrap");
                Assert.That(evcc.InstalledContractSignatureOk, Is.True, "the CPS signature over SignedInstallationData must verify");
                Assert.That(evcc.InstalledContractCertificate, Is.Not.Null);
                Assert.That(evcc.InstalledContractKey, Is.Not.Null, "the contract key must unwrap");
            });

            // The unwrapped key is a working P-521 signer matching the issued contract certificate.
            using var installedCert = System.Security.Cryptography.X509Certificates.X509CertificateLoader
                .LoadCertificate(evcc.InstalledContractCertificate!);
            using var contractPub = System.Security.Cryptography.X509Certificates.ECDsaCertificateExtensions
                .GetECDsaPublicKey(installedCert);
            var probe = new byte[] { 42, 42, 42 };
            var sig = evcc.InstalledContractKey!.SignData(probe, System.Security.Cryptography.HashAlgorithmName.SHA512);
            Assert.That(contractPub!.VerifyData(probe, sig, System.Security.Cryptography.HashAlgorithmName.SHA512), Is.True,
                "the unwrapped private key must match the issued contract certificate's public key");
            evcc.InstalledContractKey.Dispose();
        }

        /// <summary>
        /// -20 pause/resume across two real TCP connections: session 1 ends with
        /// <c>ChargingSession.Pause</c>, session 2 reconnects (fresh SAP) and rejoins with the old session
        /// id — the SECC answers <c>OK_OldSessionJoined</c> and the resumed session completes.
        /// </summary>
        [Test]
        public async Task DcSession_PauseThenResume_RejoinsOldSession()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc1Task = Task.Run(async () =>
            {
                using var s = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(s, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(s, cts.Token);
                return secc;
            }, cts.Token);

            byte[] sessionId;
            using (var evccStream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token))
            {
                await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
                {
                    StopMode = Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated.ChargingSession.Pause,
                };
                await evcc.RunAsync(cts.Token);
                sessionId = evcc.SessionId;
            }
            var secc1 = await secc1Task;
            Assert.That(secc1.Paused, Is.True);
            Assert.That(secc1.SessionId, Is.EqualTo(sessionId));

            var secc2Task = Task.Run(async () =>
            {
                using var s = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(s, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System) { ResumeSessionId = sessionId };
                await secc.RunAsync(s, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream2 = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream2, ProtocolVariant.Iso15118_20, cts.Token);
            var evcc2 = new Evcc20Dc(evccStream2, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
            {
                ResumeSessionId = sessionId,
            };
            await evcc2.RunAsync(cts.Token);
            var secc2 = await secc2Task;

            Assert.Multiple(() =>
            {
                Assert.That(evcc2.SessionSetupCode,
                    Is.EqualTo(Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated.ResponseCode.OK_OldSessionJoined));
                Assert.That(evcc2.SessionId, Is.EqualTo(sessionId));
                Assert.That(secc2.IsDone, Is.True);
                Assert.That(secc2.Paused, Is.False);
            });
        }

        [Test]
        public async Task AcSession_RunsToCompletion()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);

                var secc = new Secc20Ac(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Ac(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            var secc = await seccTask;
            Assert.That(secc.IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }
    }
}
