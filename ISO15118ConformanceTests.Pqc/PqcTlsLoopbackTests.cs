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
using System.Security.Cryptography;

using NUnit.Framework;

using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Timing;
using Vanaheimr.V2G.Simulation.Transport;
using Vanaheimr.V2G.Simulation.Transport.BouncyCastle;

namespace ISO15118ConformanceTests.Pqc
{
    /// <summary>
    /// EXPERIMENT (wire-non-conformant, loopback only): a complete ISO 15118-20 DC session whose TLS 1.3
    /// key exchange is <b>post-quantum ML-KEM-1024</b> (FIPS 203). Both sides offer ONLY the ML-KEM
    /// group, so a completed handshake proves the ML-KEM exchange was used — the negative control shows
    /// a classical-only peer cannot connect. Note: BC 2.6.2 exposes the pure-ML-KEM draft codepoints
    /// (0x0200-0x0202), not the X25519MLKEM768 hybrid deployed in browsers; and ISO 15118-20 pins
    /// classical groups, so none of this is conformant — that is the experiment's point.
    /// </summary>
    [TestFixture]
    public class PqcTlsLoopbackTests
    {
        private sealed class ImmediateDelay : IAsyncDelay
        {
            public Task Wait(TimeSpan duration, CancellationToken ct = default) => Task.CompletedTask;
        }

        /// <summary>Self-signed P-521 credentials (the -20 certificate suite — certificates stay
        /// classical here; only the KEY EXCHANGE goes post-quantum in this experiment).</summary>
        private static BcTlsCredentials SelfSignedP521()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP521);
            using var cert = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                    "CN=PQC-TLS-EXPERIMENT", key, HashAlgorithmName.SHA512)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

            return new BcTlsCredentials(new[] { cert.RawData },
                                        PrivateKeyFactory.CreateKey(key.ExportPkcs8PrivateKey()),
                                        SignatureScheme.ecdsa_secp521r1_sha512);
        }

        [Test]
        public async Task Iso20DcSession_OverMlKem1024KeyExchange_RunsToCompletion()
        {
            var creds = SelfSignedP521();
            var secc = new BcTlsOptions { OwnCredentials = creds, ExperimentalNamedGroups = new[] { NamedGroup.MLKEM1024 } };
            var evcc = new BcTlsOptions { OwnCredentials = creds, ExperimentalNamedGroups = new[] { NamedGroup.MLKEM1024 } };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0), secc);

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var machine = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await machine.RunAsync(seccStream, cts.Token);
                return machine;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port, evcc, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var machine = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateDelay(), TimeSpan.FromSeconds(5));
            await machine.RunAsync(cts.Token);

            var seccMachine = await seccTask;
            Assert.Multiple(() =>
            {
                Assert.That(seccMachine.IsDone, Is.True,
                    "full -20 DC session over a TLS channel that could ONLY have key-exchanged via ML-KEM-1024");
                Assert.That(machine.Exchanges, Is.GreaterThan(0));
            });
        }

        [Test]
        public void MlKemOnlyClient_AgainstClassicalOnlyServer_FailsTheHandshake()
        {
            var creds = SelfSignedP521();
            var secc = new BcTlsOptions { OwnCredentials = creds };   // BC defaults: classical groups only
            var evcc = new BcTlsOptions { OwnCredentials = creds, ExperimentalNamedGroups = new[] { NamedGroup.MLKEM1024 } };

            Assert.CatchAsync(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0), secc);
                var accept = Task.Run(async () => { using var s = await listener.AcceptAsync(cts.Token); }, cts.Token);
                using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port, evcc, cts.Token);
            }, "no common key-exchange group — the handshake must not silently fall back");
        }
    }
}
