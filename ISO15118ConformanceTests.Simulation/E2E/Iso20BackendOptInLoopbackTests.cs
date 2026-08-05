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
using System.Security.Authentication;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Transport;

using ISO15118ConformanceTests.Simulation.TestData;
using ISO15118ConformanceTests.Simulation.Timing;

namespace ISO15118ConformanceTests.Simulation.E2E
{
    /// <summary>
    /// ISO 15118-20 mutual TLS driven entirely from <see cref="TlsOptions"/> — .NET certificate material,
    /// .NET validation callbacks — but carried by the <b>managed</b> backend because
    /// <see cref="TlsOptions.Backend"/> says so. This is the combination the other two fixtures leave open:
    /// <see cref="MutualTlsLoopbackTests"/> runs <c>TlsOptions</c> on <c>SslStream</c> and pays for it with
    /// P-256 certificates, <see cref="BcMutualTlsLoopbackTests"/> runs the strict-20 profile but configures
    /// BouncyCastle directly, bypassing <c>TlsOptions</c> altogether.
    /// <para>
    /// <b>Why it matters here.</b> Finding 4 of the 2026-08-05 EVerest 2026.02.1 run: on Windows the -20
    /// TLS client could not be exercised at all, because Schannel builds the client chain locally before
    /// the handshake and refuses to present one whose root the machine does not trust — and installing a
    /// throwaway test root machine-wide is not an acceptable price for a test run. Selecting the managed
    /// backend removes the trust store from the path; this fixture is the offline proof of that route,
    /// end to end, on the same P-521 hierarchy the live run uses. It also gets the -20 suite pin actually
    /// enforced, which <c>CipherSuitesPolicy</c> cannot do on Windows at all.
    /// </para>
    /// </summary>
    [TestFixture]
    public class Iso20BackendOptInLoopbackTests
    {

        // The strict-20 profile: ECDSA P-521 / secp521r1, the curve Schannel cannot use for TLS in either
        // direction. Reaching this from TlsOptions is the whole point of the fixture.
        private static V2GTestPki NewPki() => V2GTestPki.CreateStrict20();

        private static TlsOptions SeccTls(V2GTestPki pki) => new()
        {
            ServerCertificate           = pki.SeccServerCert,
            RequireClientCertificate    = true,
            ClientCertificateValidation = pki.ValidateVehicleClient,
            EnabledSslProtocols         = SslProtocols.Tls13,
            CipherSuites                = TlsProfiles.Iso20CipherSuites,
            Backend                     = TlsBackend.BouncyCastle,
        };

        private static TlsOptions EvccTls(V2GTestPki pki) => new()
        {
            ClientCertificate           = pki.VehicleClientCert,
            ClientCertificateChain      = pki.VehicleIntermediates,
            ServerCertificateValidation = pki.ValidateSeccServer,
            EnabledSslProtocols         = SslProtocols.Tls13,
            CipherSuites                = TlsProfiles.Iso20CipherSuites,
            Backend                     = TlsBackend.BouncyCastle,
        };

        // Stated as a test rather than assumed by the ones below: if this ever resolves to SslStream again,
        // the session tests would still pass on Linux/Windows while quietly testing the other backend.
        [Test]
        public void TheOptInReachesTheManagedBackendOnThisPlatform()
        {
            using var pki = NewPki();

            Assert.Multiple(() =>
            {
                Assert.That(TlsPlatform.ResolveBackend(SeccTls(pki)), Is.EqualTo(TlsBackend.BouncyCastle));
                Assert.That(TlsPlatform.ResolveBackend(EvccTls(pki)), Is.EqualTo(TlsBackend.BouncyCastle));
            });
        }

        [Test]
        public async Task Iso20DcSession_RunsToCompletion_OnP521_ViaTlsOptions()
        {
            using var pki      = NewPki();
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var       seccTls  = SeccTls(pki);
            var       evccTls  = EvccTls(pki);
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0), seccTls);

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                TlsAssert.NegotiatedVersion(seccStream, SslProtocols.Tls13, seccTls);
                TlsAssert.NegotiatedCipherSuite(seccStream, TlsProfiles.Iso20CipherSuites);
                TlsAssert.MutuallyAuthenticated(seccStream,
                    "the EVCC must have presented and passed client-certificate authentication",
                    seccTls);

                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                "localhost", listener.LocalEndpoint.Port, evccTls, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc     = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            Assert.That((await seccTask).IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }

        // Same route, AC: the backend is transport-level, so the -20 message set it carries is irrelevant —
        // worth one run to say so rather than to assume it.
        [Test]
        public async Task Iso20AcSession_RunsToCompletion_OnP521_ViaTlsOptions()
        {
            using var pki      = NewPki();
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var       seccTls  = SeccTls(pki);
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0), seccTls);

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Ac(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                "localhost", listener.LocalEndpoint.Port, EvccTls(pki), cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc     = new Evcc20Ac(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            var evccTask = evcc.RunAsync(cts.Token);
            await Task.WhenAll(evccTask, seccTask);

            Assert.That((await seccTask).IsDone, Is.True);
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }

    }
}
