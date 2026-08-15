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
    /// <c>SupportedServiceIDs</c> — the optional filter an EV may put in <c>ServiceDiscoveryReq</c>
    /// (Table 38 of `[V2G20-1248]`), and what our station makes of one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written on 2026-08-15, because until that day <c>Evcc20Base</c> passed the literal <c>null</c> and
    /// this car could not send the element at all. What made it worth building was a station that cannot
    /// be asked the ordinary question: eVDriveFlow's <c>process_service_discovery_request.py</c>
    /// dereferences the optional element unconditionally, so every forward session this project had ever
    /// driven against their SECC ended at the fifth message
    /// (<c>docs/reports/evdriveflow-service-discovery-filter.md</c>). With the filter, the same session
    /// reached their charge loop — which is how ten of their thirteen `[V2G20-460]` handlers got measured
    /// rather than three (<c>docs/interop-runs/2026-08-15-edf-session-id-460/</c>).
    /// </para>
    /// <para>
    /// <b>Both settings are conformant, and that is the point of the third test.</b> Table 38 marks the
    /// element optional and describes it as a filter the EV <i>can</i> use, with omission meaning *all
    /// services*; the filtered-list sentence in Table 39 (`[V2G20-1249]`) is attached to <c>VASList</c>,
    /// which this station does not send. So a station that answers with its whole energy-transfer
    /// catalogue regardless is not thereby wrong, and ours is not changed into filtering.
    /// </para>
    /// <para>
    /// <b>Which of these fail when the plumbing is reverted:</b> the first two.
    /// <see cref="DefaultSession_SendsNoFilter"/> pins the opt-in, so a later default flip is noticed
    /// rather than absorbed — every recorded session and every vector was taken without the element.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Iso20ServiceFilterTests
    {

        /// <summary>The filter reaches the station, with the ids the car put in it.</summary>
        [Test]
        public async Task AFilterTheCarSets_ReachesTheStation()
        {
            var (evcc, secc) = await RunDcSessionAsync([2, 6]);

            Assert.That(secc.SupportedServiceIdsRequestedByEv, Is.EqualTo(new ushort[] { 2, 6 }),
                        "ServiceDiscoveryReq has to carry SupportedServiceIDs with exactly the ids given; "
                      + "if this fails, Evcc20Base is passing the literal null again");
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }


        /// <summary>A filter naming one id is still a filter — the list is sent as given, not normalised
        /// into the station's catalogue.</summary>
        [Test]
        public async Task AFilterOfOne_ArrivesAsOne()
        {
            var (_, secc) = await RunDcSessionAsync([6]);

            Assert.That(secc.SupportedServiceIdsRequestedByEv, Is.EqualTo(new ushort[] { 6 }));
        }


        /// <summary>The default sends no element at all, which is what every recorded session contains and
        /// what Table 38 defines as asking for everything.</summary>
        [Test]
        public async Task DefaultSession_SendsNoFilter()
        {
            var (_, secc) = await RunDcSessionAsync(null);

            Assert.That(secc.SupportedServiceIdsRequestedByEv, Is.Null,
                        "the filter is opt-in; a default that started sending one would change the wire "
                      + "output of every session in docs/interop-runs/ and every vector under Vectors/");
        }


        /// <summary>And the station still answers with its whole catalogue, filter or not — the behaviour
        /// the requirement leaves to the SECC, pinned so that a later change to it is a decision.</summary>
        [Test]
        public async Task AFilteredSession_StillGetsTheWholeCatalogue()
        {
            var (filtered, _) = await RunDcSessionAsync([6]);
            var (plain,    _) = await RunDcSessionAsync(null);

            Assert.That(filtered.SelectedEnergyServiceId, Is.EqualTo(plain.SelectedEnergyServiceId),
                        "our station does not narrow its offer to the filter, so the same service is "
                      + "negotiated either way ([V2G20-1249]'s filtering sentence is about VASList)");
        }


        private static async Task<(Evcc20Dc Evcc, Secc20Dc Secc)> RunDcSessionAsync(ushort[]? serviceIds)
        {
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token, PowerMode.Dc);
                await secc.RunAsync(seccStream, cts.Token);
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(),
                                                                   listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token, PowerMode.Dc);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(),
                                    LoopbackTimeouts.PerMessage)
            {
                SupportedServiceIds = serviceIds
            };

            await Task.WhenAll(evcc.RunAsync(cts.Token), seccTask);

            return (evcc, secc);
        }

    }

}
