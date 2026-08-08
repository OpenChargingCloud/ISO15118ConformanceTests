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
using cloud.charging.open.protocols.ISO15118.Transport;

namespace ISO15118ConformanceTests.Simulation.Interop
{
    /// <summary>
    /// Tier-2 interop against <b>Josev</b> (SwitchEV/iso15118 or the EVerest fork). These are
    /// <see cref="ExplicitAttribute">[Explicit]</see> and env-var-gated, so they never run in the standard
    /// <c>dotnet test</c> suite (which must stay green offline). Bring a Josev endpoint up per
    /// <c>tools/interop-josev/README.md</c>, then run this fixture by category:
    /// <code>dotnet test --filter TestCategory=Interop</code>
    /// <para>The environment variables are the shared ones — see <see cref="InteropEnvironment"/>.</para>
    /// <para>
    /// Josev's value among the counterparties is that its EXI comes from EXIficient, which shares no
    /// lineage with our cbV2G-generated corpus. A byte disagreement here is a genuinely independent
    /// finding, which is not true of the cbexigen-based stacks (see <see cref="TuxEvseInteropTests"/>).
    /// </para>
    /// </summary>
    [TestFixture]
    [Category("Interop")]
    [Explicit("Requires a running Josev endpoint (see tools/interop-josev/README.md); never part of the offline CI run.")]
    public class JosevInteropTests
    {

        [Test]
        public async Task OurEvcc_AgainstJosevSecc_RunsToCompletion()
        {

            var endpoint         = InteropEnvironment.SeccEndpointOrIgnore("a running Josev SECC");
            var (protocol, mode) = InteropEnvironment.ProtocolAndMode();
            var (protocolName, modeName) = InteropEnvironment.ProtocolAndModeNames();

            var recording = InteropRecording.FromEnvironment($"josev-{modeName}-forward");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

            using var socket = await TcpV2GClient.ConnectAsync(endpoint.ConnectHost, endpoint.Port,
                                                               InteropEnvironment.DevTlsOrNull(protocol), cts.Token);

            var stream = recording?.Tap(socket) ?? socket;

            try
            {
                await SapHandshake.RunEvccSideAsync(stream, protocol, cts.Token);

                var outcome = await InteropSession.RunEvccAsync(stream, protocol, mode, cts.Token,
                                                                InteropEnvironment.PreferDynamic(),
                                                                InteropEnvironment.ContractCredentialsOrNull(),
                                                                mcs: InteropEnvironment.Mcs());

                TestContext.Out.WriteLine($"Authorization: {outcome.AuthorizationMode}" +
                                          (outcome.MeteringReceiptsSent > 0
                                               ? $", {outcome.MeteringReceiptsSent} signed metering receipt(s)" : ""));

                Assert.That(outcome.Exchanges, Is.GreaterThan(0),
                            "our EVCC exchanged at least one message with Josev's SECC");
            }
            finally
            {
                Report(recording?.Save(protocolName, modeName,
                                       "live interop: our EVCC against Josev's SECC", weAreTheEvcc: true));
            }

        }


        /// <remarks>
        /// The listener binds <see cref="IPAddress.IPv6Any"/>. It used to bind <see cref="IPAddress.Any"/>,
        /// which is IPv4-only and cannot accept the link-local IPv6 connection a real EVCC makes — the
        /// recorded reverse runs under <c>docs/interop-runs/</c> all went through the CLI, which binds
        /// IPv6, so this path was never the one that worked.
        /// </remarks>
        [Test]
        public async Task JosevEvcc_AgainstOurSecc_RunsToCompletion()
        {

            var listenPort       = InteropEnvironment.ListenPortOrIgnore("point a Josev EVCC at it");
            var (protocol, mode) = InteropEnvironment.ProtocolAndMode();
            var (protocolName, modeName) = InteropEnvironment.ProtocolAndModeNames();

            var recording = InteropRecording.FromEnvironment($"josev-{modeName}-reverse");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.IPv6Any, listenPort));

            await using var sdp = await InteropSdp.AdvertiseOrNullAsync(listener.LocalEndpoint.Port,
                                                                        tls: false, cts.Token);

            TestContext.Out.WriteLine($"Waiting for a Josev EVCC to connect on [::]:{listenPort} ...");

            using var socket = await listener.AcceptAsync(cts.Token);

            var stream = recording?.Tap(socket) ?? socket;

            try
            {
                await SapHandshake.RunSeccSideAsync(stream, protocol, cts.Token);

                var outcome = await InteropSession.RunSeccAsync(stream, protocol, mode, cts.Token,
                                                                mcs: InteropEnvironment.Mcs());

                if (outcome.SelectedEnergyServiceId is { } serviceId)
                    TestContext.Out.WriteLine($"Energy transfer service: {serviceId} — their EVCC's pick.");

                Assert.That(outcome.IsDone, Is.True, "our SECC drove Josev's EVCC to the terminal session state");
            }
            finally
            {
                Report(recording?.Save(protocolName, modeName,
                                       "live interop: Josev's EVCC against our SECC", weAreTheEvcc: false));
            }

        }


        private static void Report(IReadOnlyList<String>? written)
        {
            if (written is null)
            {
                TestContext.Out.WriteLine("Nothing was recorded — set V2G_INTEROP_RECORD=<dir> to keep the session.");
                return;
            }

            TestContext.Out.WriteLine("Recorded:");
            foreach (var path in written)
                TestContext.Out.WriteLine($"  {path}");
        }

    }
}
