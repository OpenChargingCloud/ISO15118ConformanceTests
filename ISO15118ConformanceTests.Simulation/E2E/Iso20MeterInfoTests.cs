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

using cloud.charging.open.protocols.ISO15118.Metering;
using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using ISO15118ConformanceTests.Simulation.Timing;
using cloud.charging.open.protocols.ISO15118.Transport;

namespace ISO15118ConformanceTests.Simulation.E2E
{

    /// <summary>
    /// `[V2G20-1081]` / `[V2G20-1082]` — the EV asks to be told the meter reading by setting
    /// <c>MeterInfoRequested</c> in the charge-loop request, and the station, having been asked,
    /// <em>shall</em> answer with the <c>MeterInfo</c> element.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written on 2026-08-10, because until that day <c>Evcc20Dc</c> and <c>Evcc20Ac</c> both passed the
    /// literal <c>false</c>, so this EVCC could not exercise `[V2G20-1081]` at all — which also meant no
    /// run of this suite had ever put a counterparty's `[V2G20-1082]` to the test. The first station asked
    /// was EVerest's <c>Evse15118D20</c>, and it answered nothing:
    /// <c>docs/reports/everest-d20-meter-info.md</c>.
    /// </para>
    /// <para>
    /// <b>Which of these fail when the change is reverted</b>, said out loud because it is the only thing
    /// that makes a fixture worth reading: the first two.
    /// <see cref="DefaultSession_DoesNotAskForMeterInfo"/> pins the opt-in — it passes before and after,
    /// and exists so a later default flip is noticed rather than absorbed.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Iso20MeterInfoTests
    {

        /// <summary>Asking is visible at the station: our SECC records the request field.</summary>
        [Test]
        public async Task RequestingMeterInfo_ReachesTheStation()
        {
            var (evcc, secc) = await RunDcSessionAsync(requestMeterInfo: true);

            Assert.That(secc.MeterInfoRequestedByEv, Is.True,
                        "the charge-loop request has to carry MeterInfoRequested = true ([V2G20-1081]); "
                      + "if this fails, Evcc20Dc is passing the literal false again");
            Assert.That(evcc.Exchanges, Is.GreaterThan(0));
        }


        /// <summary>And the station answers it: `[V2G20-1082]`, over our own loopback, which is the
        /// reference behaviour the EVerest run was measured against.</summary>
        [Test]
        public async Task StationWithAMeter_AnswersEveryChargeLoop()
        {
            var (evcc, _) = await RunDcSessionAsync(requestMeterInfo: true, withMeter: true);

            Assert.That(evcc.MeterInfoResponses, Is.EqualTo(3),
                        "all three charge-loop responses have to carry MeterInfo ([V2G20-1082]); "
                      + "our station sends it whenever it has a reading");
        }


        /// <summary>The default keeps the field <c>false</c>, so every recorded session and every vector
        /// still describes the bytes it was recorded with.</summary>
        [Test]
        public async Task DefaultSession_DoesNotAskForMeterInfo()
        {
            var (_, secc) = await RunDcSessionAsync(requestMeterInfo: false);

            Assert.That(secc.MeterInfoRequestedByEv, Is.False,
                        "MeterInfoRequested is opt-in; a default that starts asking would change the wire "
                      + "output of every session in docs/interop-runs/ and every vector under Vectors/");
        }


        /// <summary>A station with no meter fitted answers nothing, and that is not a defect — it is
        /// `[V2G20-1833]`'s antecedent failing. Kept so the EVerest finding cannot be explained that way:
        /// their SIL has a power meter wired into <c>EvseManager</c>.</summary>
        [Test]
        public async Task StationWithoutAMeter_AnswersNothing_AndThatIsAllowed()
        {
            var (evcc, secc) = await RunDcSessionAsync(requestMeterInfo: true, withMeter: false);

            Assert.That(secc.MeterInfoRequestedByEv, Is.True);
            Assert.That(evcc.MeterInfoResponses, Is.Zero,
                        "no meter, no reading, no element — the case a station without metering technology "
                      + "is in, and the one EVerest's is not");
        }


        private static async Task<(Evcc20Dc Evcc, Secc20Dc Secc)> RunDcSessionAsync(bool requestMeterInfo,
                                                                                    bool withMeter = false)
        {
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System)
            {
                InstalledMeter = withMeter
                                     ? new SigningMeter("VAN*M*4711",
                                                        ECDsa.Create(ECCurve.NamedCurves.nistP256),
                                                        TimeProvider.System)
                                     : null
            };

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
                RequestMeterInfo = requestMeterInfo
            };

            await Task.WhenAll(evcc.RunAsync(cts.Token), seccTask);

            return (evcc, secc);
        }

    }

}
