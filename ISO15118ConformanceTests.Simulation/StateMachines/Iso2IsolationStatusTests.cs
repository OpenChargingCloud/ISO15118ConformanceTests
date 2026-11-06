/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of ISO15118ConformanceTests <https://github.com/OpenChargingCloud/ISO15118ConformanceTests>
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

#region Usings

using System.Net;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118.Transport;
using cloud.charging.open.protocols.ISO15118_2;
using cloud.charging.open.protocols.ISO15118_2.Generated;

using ISO15118ConformanceTests.Simulation.Timing;

#endregion

namespace ISO15118ConformanceTests.Simulation.StateMachines
{

    /// <summary>
    /// ISO 15118-2 DC: <b>a station that says its isolation test finished says what it found.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>EVSEIsolationStatus</c> is optional in every <c>DC_EVSEStatus</c>, and until 2026-11-06 our
    /// station left it out of all of them — including the <c>CableCheckRes</c> that answers
    /// <c>Finished</c>. EVerest's next car, <c>Ev15118</c> (<c>everest-core#3031</c>), refuses an absent
    /// result on purpose and stopped after <c>CableCheck</c>, 2 runs of 2; with <c>Valid</c> stated it
    /// charged to <c>SessionStop</c>, 2 of 2 (<c>docs/interop-runs/2026-11-06-everest-ev15118/</c>).
    /// </para>
    /// <para>
    /// <b>The second and third tests fail on the station before that day</b>, which stated nothing anywhere.
    /// The first passes either way: it pins the other half of the decision, that nothing is claimed before
    /// a test has run. The fourth pins that the result stands across a renegotiation and is stated again by
    /// the second test.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Iso2IsolationStatusTests
    {

        /// <summary>
        /// Before <c>CableCheck</c> no test has run, so the <c>ChargeParameterDiscoveryRes</c> states no
        /// result — <c>Invalid</c> there would be a measurement the station never made.
        /// </summary>
        [Test]
        public async Task Iso2Dc_BeforeCableCheck_NoResultIsStated()
        {

            var secc = await RunAsync();

            var discovery = secc.Responses.OfType<ChargeParameterDiscoveryResType>().Single();

            Assert.That(DcStatus(discovery)!.EVSEIsolationStatus, Is.Null);

        }

        /// <summary>
        /// The <c>CableCheckRes</c> that reports the test <c>Finished</c> states its result, <c>Valid</c>.
        /// </summary>
        [Test]
        public async Task Iso2Dc_CableCheckRes_StatesValid()
        {

            var secc = await RunAsync();

            var cableCheck = secc.Responses.OfType<CableCheckResType>().Single();

            Assert.Multiple(() =>
            {
                Assert.That(cableCheck.EVSEProcessing,                    Is.EqualTo(EVSEProcessing.Finished));
                Assert.That(cableCheck.DC_EVSEStatus.EVSEIsolationStatus, Is.EqualTo(IsolationLevel.Valid));
            });

        }

        /// <summary>
        /// And keeps stating it: <c>PreChargeRes</c>, both <c>PowerDeliveryRes</c>, every
        /// <c>CurrentDemandRes</c> and the <c>WeldingDetectionRes</c> — the way a station with an insulation
        /// monitor reports its latest reading in every status it sends.
        /// </summary>
        [Test]
        public async Task Iso2Dc_EveryStatusAfterCableCheck_StatesValid()
        {

            var secc = await RunAsync();

            var after = secc.Responses.SkipWhile(r => r is not CableCheckResType)
                                      .Select(r => (Name: r.GetType().Name.Replace("Type", ""), Status: DcStatus(r)))
                                      .Where(r => r.Status is not null)
                                      .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(after.Select(r => r.Name), Does.Contain("PreChargeRes")
                                                         .And.Contain("PowerDeliveryRes")
                                                         .And.Contain("CurrentDemandRes")
                                                         .And.Contain("WeldingDetectionRes"),
                            "the whole DC path after the test");
                Assert.That(after.Where(r => r.Status!.EVSEIsolationStatus != IsolationLevel.Valid)
                                 .Select(r => r.Name),
                            Is.Empty,
                            "a status after CableCheck without the result it found");
            });

        }

        /// <summary>
        /// A renegotiation sends the car through <c>CableCheck</c> again. The first result stands until the
        /// second test restates it, so the second <c>ChargeParameterDiscoveryRes</c> carries <c>Valid</c>.
        /// </summary>
        [Test]
        public async Task Iso2Dc_AcrossARenegotiation_TheResultStandsAndIsRestated()
        {

            var secc = await RunAsync(requestRenegotiation: true);

            var discoveries = secc.Responses.OfType<ChargeParameterDiscoveryResType>()
                                            .Select(r => DcStatus(r)!.EVSEIsolationStatus).ToArray();
            var cableChecks = secc.Responses.OfType<CableCheckResType>()
                                            .Select(r => r.DC_EVSEStatus.EVSEIsolationStatus).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsolationSequences, Is.EqualTo(2));
                Assert.That(discoveries,             Is.EqualTo(new IsolationLevel?[] { null, IsolationLevel.Valid }));
                Assert.That(cableChecks,             Is.EqualTo(new IsolationLevel?[] { IsolationLevel.Valid, IsolationLevel.Valid }));
            });

        }

        #region Harness

        /// <summary>The <c>DC_EVSEStatus</c> a response carries, wherever its type keeps it; null for one that
        /// carries none.</summary>
        private static DC_EVSEStatusType? DcStatus(BodyBaseType response) => response switch
        {
            ChargeParameterDiscoveryResType r => (r.EVSEChargeParameter as DC_EVSEChargeParameterType)?.DC_EVSEStatus,
            CableCheckResType r               => r.DC_EVSEStatus,
            PreChargeResType r                => r.DC_EVSEStatus,
            PowerDeliveryResType r            => r.EVSEStatus as DC_EVSEStatusType,
            CurrentDemandResType r            => r.DC_EVSEStatus,
            MeteringReceiptResType r          => r.EVSEStatus as DC_EVSEStatusType,
            WeldingDetectionResType r         => r.DC_EVSEStatus,
            _                                 => null
        };

        private sealed class RecordingSecc2(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc2(PowerMode.Dc, sequenceTimeout, clock)
        {

            public List<BodyBaseType> Responses { get; } = [];

            public override V2G_Message Handle(V2G_Message request)
            {

                var response = base.Handle(request);

                if (response.Body.BodyElement is { } answer)
                    Responses.Add(answer);

                return response;

            }

        }

        private static async Task<RecordingSecc2> RunAsync(Boolean requestRenegotiation = false)
        {

            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new RecordingSecc2(TimeSpan.FromSeconds(60), TimeProvider.System) {
                           RequestRenegotiation = requestRenegotiation
                       };

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Dc);
                await secc.RunAsync(seccStream, cts.Token);
            }, cts.Token);

            using (var stream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                (TlsOptions?) null, cts.Token))
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Dc);
                await new Evcc2(stream, PowerMode.Dc, TimeProvider.System, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage).RunAsync(cts.Token);
            }

            await seccTask;

            return secc;

        }

        #endregion

    }

}
