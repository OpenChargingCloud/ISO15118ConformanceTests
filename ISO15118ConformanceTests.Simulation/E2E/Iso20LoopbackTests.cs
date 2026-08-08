/*
 * Copyright (c) 2021-2025 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Net;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using ISO15118ConformanceTests.Simulation.Timing;
using cloud.charging.open.protocols.ISO15118.Transport;

namespace ISO15118ConformanceTests.Simulation.E2E
{
    /// <summary>
    /// Real TCP, loopback-only, end-to-end for ISO 15118-20: SAP negotiates -20, then a full DC or AC
    /// happy path runs to SessionStop across the three interleaved message sets (CommonMessages/DC/AC),
    /// each auto-detected per frame by <see cref="cloud.charging.open.protocols.ISO15118.EXI.Dispatch.V2GTPDispatcher"/>.
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
        /// Stand-ins for the vehicle's TLS leaf certificate. The state machines only ever hash these, so a
        /// test can supply bytes instead of standing up a mutual-TLS handshake — what is under test is the
        /// binding rule, not the transport that normally provides the input.
        /// </summary>
        private static readonly byte[] VehicleA = "vehicle-A-leaf-certificate-DER"u8.ToArray();
        private static readonly byte[] VehicleB = "vehicle-B-leaf-certificate-DER"u8.ToArray();

        /// <summary>Runs one -20 DC session over a fresh loopback connection and hands back both ends.</summary>
        /// <remarks>The SECC's <paramref name="offer"/> is what a paused predecessor left behind, and
        /// <paramref name="evccResume"/> is what the car believes it may resume — kept separate so a test can
        /// make them disagree, which is the whole point of two of the tests below.</remarks>
        private static async Task<(Secc20Dc Secc, Evcc20Dc Evcc)> RunDcSessionAsync(
            TcpV2GListener listener, CancellationToken ct,
            bool pause = false,
            ResumableSession? offer = null, byte[]? seccSeesVehicle = null,
            ResumableSession? evccResume = null)
        {

            var seccTask = Task.Run(async () =>
            {
                using var s = await listener.AcceptAsync(ct);
                await SapHandshake.RunSeccSideAsync(s, ProtocolVariant.Iso15118_20, ct);
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System)
                {
                    VehicleLeafCertificate = seccSeesVehicle,
                };
                secc.OfferResume(offer);
                await secc.RunAsync(s, ct);
                return secc;
            }, ct);

            using var evccStream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: ct);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, ct);
            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage)
            {
                StopMode = pause
                               ? cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.ChargingSession.Pause
                               : cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.ChargingSession.Terminate,
            };
            evcc.ResumeFrom(evccResume);
            await evcc.RunAsync(ct);

            return (await seccTask, evcc);

        }

        /// <summary>
        /// -20 pause/resume across two real TCP connections, with the vehicle presenting the same
        /// certificate both times: the station answers <c>OK_OldSessionJoined</c> and the resumed session
        /// completes.
        /// </summary>
        /// <remarks>
        /// <b>This test could not fail before 2026-08-08, and that was the problem.</b> A resumed `-20`
        /// session opens at ChargeParameterDiscovery — authorization and service negotiation are not
        /// repeated — but our EVCC replayed its whole opening sequence and our SECC accepted it, so the two
        /// were wrong in the same direction and agreed with each other. EVerest disagreed, with
        /// <c>FAILED_SequenceError</c>. Now the station enforces the sequence, so an EVCC that replays
        /// authorization aborts here rather than passing.
        /// </remarks>
        [Test]
        public async Task DcSession_PauseThenResume_SameVehicle_RejoinsOldSession()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var (secc1, evcc1) = await RunDcSessionAsync(listener, cts.Token, pause: true, seccSeesVehicle: VehicleA);

            Assert.Multiple(() =>
            {
                Assert.That(secc1.Paused, Is.True);
                Assert.That(secc1.PausedSession, Is.Not.Null);
                Assert.That(secc1.PausedSession!.SessionId, Is.EqualTo(evcc1.SessionId));
                Assert.That(secc1.PausedSession.Binding, Is.Not.Null,
                    "a session opened with a known vehicle certificate must carry a binding, or nothing can be verified later");
            });

            var (secc2, evcc2) = await RunDcSessionAsync(listener, cts.Token,
                offer: secc1.PausedSession, seccSeesVehicle: VehicleA,
                evccResume: new ResumableSession(evcc1.SessionId, null, evcc1.SelectedEnergyServiceId));

            Assert.Multiple(() =>
            {
                Assert.That(evcc2.SessionSetupCode,
                    Is.EqualTo(cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.ResponseCode.OK_OldSessionJoined));
                Assert.That(secc2.SessionSetupCode,
                    Is.EqualTo(cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.ResponseCode.OK_OldSessionJoined));
                Assert.That(evcc2.SessionId, Is.EqualTo(evcc1.SessionId));
                Assert.That(evcc2.ResumeRefused, Is.False);
                Assert.That(secc2.IsDone, Is.True);
                Assert.That(secc2.Paused, Is.False);
                Assert.That(secc2.SelectedEnergyServiceId, Is.EqualTo(secc1.SelectedEnergyServiceId),
                    "a resumed session keeps the service it settled on — it never renegotiates one");
            });
        }

        /// <summary>
        /// A <em>different</em> vehicle naming the paused session's id does not get it: the station opens a
        /// new session under a new id instead.
        /// </summary>
        /// <remarks>
        /// The security property, and the reason the check is a <i>shall</i> rather than a nicety — an EV
        /// that could claim another's paused session would inherit its authorization and charge on someone
        /// else's contract. Our SECC would have handed it over until 2026-08-08.
        /// </remarks>
        [Test]
        public async Task DcSession_Resume_FromAnotherVehicle_StartsANewSession()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var (secc1, evcc1) = await RunDcSessionAsync(listener, cts.Token, pause: true, seccSeesVehicle: VehicleA);
            Assert.That(secc1.Paused, Is.True);

            // Same session id, different car.
            var (secc2, evcc2) = await RunDcSessionAsync(listener, cts.Token,
                offer: secc1.PausedSession, seccSeesVehicle: VehicleB,
                evccResume: new ResumableSession(evcc1.SessionId, null, 0));

            Assert.Multiple(() =>
            {
                Assert.That(evcc2.SessionSetupCode,
                    Is.EqualTo(cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.ResponseCode.OK_NewSessionEstablished));
                Assert.That(evcc2.SessionId, Is.Not.EqualTo(evcc1.SessionId),
                    "a refused resume must be answered with an id unequal to the one that was asked for");
                Assert.That(evcc2.ResumeRefused, Is.True,
                    "the car has to notice, because everything the paused session carried is now void");
                Assert.That(secc2.IsDone, Is.True);
            });
        }

        /// <summary>
        /// A resume that cannot be verified at all — no certificate on the connection — is refused the same
        /// way as a wrong one.
        /// </summary>
        /// <remarks>
        /// `-20` permits nothing but full-handshake TLS, so a conformant session always has a certificate to
        /// bind to and this case is off-protocol to begin with. The station therefore fails closed: an
        /// unverifiable resume is not the same EV as far as it can tell, and treating "cannot check" as
        /// "check passed" is exactly the hole being closed.
        /// </remarks>
        [Test]
        public async Task DcSession_Resume_WithoutAnyCertificate_StartsANewSession()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var (secc1, evcc1) = await RunDcSessionAsync(listener, cts.Token, pause: true, seccSeesVehicle: null);

            Assert.Multiple(() =>
            {
                Assert.That(secc1.Paused, Is.True);
                Assert.That(secc1.PausedSession!.Binding, Is.Null,
                    "no certificate, no binding — and so nothing a later connection could present");
            });

            var (secc2, evcc2) = await RunDcSessionAsync(listener, cts.Token,
                offer: secc1.PausedSession, seccSeesVehicle: null,
                evccResume: new ResumableSession(evcc1.SessionId, null, 0));

            Assert.Multiple(() =>
            {
                Assert.That(evcc2.SessionSetupCode,
                    Is.EqualTo(cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.ResponseCode.OK_NewSessionEstablished));
                Assert.That(evcc2.ResumeRefused, Is.True);
                Assert.That(secc2.IsDone, Is.True);
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
