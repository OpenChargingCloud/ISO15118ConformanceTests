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
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using ISO15118ConformanceTests.Simulation.TestData;
using ISO15118ConformanceTests.Simulation.Timing;
using cloud.charging.open.protocols.ISO15118.Transport;

namespace ISO15118ConformanceTests.Simulation.E2E
{
    /// <summary>
    /// Proves the exact same state-machine/framing code that runs over plain TCP in
    /// <see cref="Iso2LoopbackTests"/>/<see cref="Iso20LoopbackTests"/> also runs unchanged over
    /// <see cref="SslStream"/> — one -2 flow (TLS optional per spec) and one -20 flow (TLS "vorgesehen").
    /// A self-signed test certificate is generated fresh for the run (see <see cref="TestCertificate"/>);
    /// the client validates it by thumbprint rather than blanket-accepting anything, so the callback
    /// still exercises real validation logic.
    /// </summary>
    [TestFixture]
    public class TlsLoopbackTests
    {
        // The TLS version is part of the protocol's profile, not a free choice: docs/pki-model.md pins the
        // mapping to -2 <-> TLS 1.2 and -20 <-> TLS 1.3 and explicitly rules out "-2 over TLS 1.3", so each
        // test states its version rather than inheriting a library default that would suit only one of them.
        private static TlsOptions ServerTls(X509Certificate2 cert, SslProtocols protocols, IReadOnlyList<TlsCipherSuite>? suites = null) => new()
        {
            ServerCertificate   = cert,
            EnabledSslProtocols = protocols,
            CipherSuites        = suites,
        };

        private static TlsOptions ClientTls(X509Certificate2 serverCert, SslProtocols protocols, IReadOnlyList<TlsCipherSuite>? suites = null) => new()
        {
            ServerCertificateValidation = (_, presented, _, _) =>
                presented is X509Certificate2 p && p.Thumbprint == serverCert.Thumbprint,
            EnabledSslProtocols = protocols,
            CipherSuites        = suites,
        };

        [Test]
        public async Task Iso2AcSession_RunsToCompletionOverTls()
        {
            using var cert = TestCertificate.CreateSelfSigned();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0),
                                                    ServerTls(cert, SslProtocols.Tls12, TlsProfiles.Iso2CipherSuites));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                TlsAssert.NegotiatedVersion(seccStream, SslProtocols.Tls12);
                TlsAssert.NegotiatedCipherSuite(seccStream, TlsProfiles.Iso2CipherSuites);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token);

                var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                "localhost", listener.LocalEndpoint.Port,
                ClientTls(cert, SslProtocols.Tls12, TlsProfiles.Iso2CipherSuites), cts.Token);
            TlsAssert.NegotiatedVersion(evccStream, SslProtocols.Tls12);
            TlsAssert.NegotiatedCipherSuite(evccStream, TlsProfiles.Iso2CipherSuites);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token);

            var evcc = new Evcc2(evccStream, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            Assert.That((await seccTask).IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }

        [Test]
        public async Task Iso20DcSession_RunsToCompletionOverTls()
        {
            using var cert = TestCertificate.CreateSelfSigned();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0),
                                                    ServerTls(cert, SslProtocols.Tls13, TlsProfiles.Iso20CipherSuites));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                TlsAssert.NegotiatedVersion(seccStream, SslProtocols.Tls13);
                TlsAssert.NegotiatedCipherSuite(seccStream, TlsProfiles.Iso20CipherSuites);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);

                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                "localhost", listener.LocalEndpoint.Port,
                ClientTls(cert, SslProtocols.Tls13, TlsProfiles.Iso20CipherSuites), cts.Token);
            TlsAssert.NegotiatedVersion(evccStream, SslProtocols.Tls13);
            TlsAssert.NegotiatedCipherSuite(evccStream, TlsProfiles.Iso20CipherSuites);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            Assert.That((await seccTask).IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }
    }
}
