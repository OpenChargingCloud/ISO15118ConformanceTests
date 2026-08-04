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

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.StateMachines
{
    /// <summary>
    /// ISO 15118-20 <b>MCS</b> (Megawatt Charging System) coverage.
    /// <para>
    /// MCS adds no message set and no schema: it is the DC message set advertised under service ids
    /// <b>8 (MCS)</b> / <b>9 (MCS_BPT)</b> with a megawatt power envelope. These tests therefore check the
    /// two things that actually distinguish it — the catalogue and the limits — and that a whole session
    /// still runs over the unchanged DC state machines.
    /// </para>
    /// <para>
    /// <b>No external validation.</b> The service ids and connector values come from EVerest's libiso15118,
    /// whose neighbouring values (AC=1, DC=2, DC_BPT=6) match ours exactly; nothing here has been
    /// byte-diffed or run against a live MCS counterpart. See <c>docs/roadmap.md</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    public class Secc20McsTests
    {
        [Test]
        public async Task McsSession_RunsToCompletion_OverTheDcMessageSet()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Mcs(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port, (TlsOptions?) null, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Mcs(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            await Task.WhenAll(evcc.RunAsync(cts.Token), seccTask);

            var secc = await seccTask;
            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True, "the MCS SECC did not reach its terminal phase");
                Assert.That(evcc.Exchanges, Is.GreaterThan(0));
                // The whole point: an MCS session negotiated an MCS service id, not a DC one.
                Assert.That(evcc.SelectedEnergyServiceId, Is.EqualTo(8).Or.EqualTo(9),
                            "the EVCC should have selected an MCS service (8 / 9), not DC's 2 / 6");
            });
        }

        [Test]
        public async Task McsEvcc_FallsBackToWhateverAPlainDcSeccOffers()
        {
            // A megawatt truck at an ordinary DC charger: no MCS service in the catalogue, so the base
            // selection logic falls back rather than aborting. Documents that MCS support is additive.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);   // plain DC charger
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port, (TlsOptions?) null, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Mcs(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            await Task.WhenAll(evcc.RunAsync(cts.Token), seccTask);

            var secc = await seccTask;
            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(evcc.SelectedEnergyServiceId, Is.EqualTo(2).Or.EqualTo(6),
                            "with no MCS service offered the EVCC should take the DC one");
            });
        }
    }
}
