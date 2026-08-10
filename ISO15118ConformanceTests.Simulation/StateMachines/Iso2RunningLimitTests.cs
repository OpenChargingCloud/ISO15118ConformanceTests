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
    /// ISO 15118-2 DC: <b>the car has to read the ceiling the station keeps restating.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// -2 lets the SECC carry <c>EVSEMaximumCurrentLimit</c> and <c>EVSEMaximumPowerLimit</c> in the
    /// ChargeParameterDiscoveryRes <em>and again in every CurrentDemandRes</em>, which is how a station
    /// derates mid-session. Until 2026-08-10 our EVCC read neither: <c>CurrentDemand()</c> computed a
    /// setpoint from a constant and re-sent it for the life of the loop.
    /// </para>
    /// <para>
    /// A live EVerest station found it. It dropped its running limit to 55.2 A while our car went on
    /// asking for 120 A — three times out of three, with their <c>EvseManager</c> clamping and warning
    /// each time, 47 such warnings across the recorded runs before one was read
    /// (<c>docs/interop-runs/2026-08-10-everest-session-log-lengths/</c>).
    /// </para>
    /// <para>
    /// <b>Nothing here could have caught it before</b>, and that is the second half of the fix: our own
    /// station sent all three limit fields as <c>null</c> in every CurrentDemandRes, so a loopback session
    /// never put a running ceiling in front of the car. <see cref="Secc2.DcRunningMaxAmps"/> and
    /// <see cref="Secc2.DcAdvertisedMaxAmps"/> exist so it can — opt-in, so the recorded corpus is
    /// untouched.
    /// </para>
    /// <para>
    /// <b>Two of the three tests below fail on the pre-fix EVCC</b>, both by asking for 120 A where the
    /// station allowed less. The third pins the unchanged default and passes either way; it is here so
    /// that the opt-in cannot quietly become the only path.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Iso2RunningLimitTests
    {

        #region The running limit, revised in the charge loop

        /// <summary>
        /// The station serves 120 A and then says "55 A from here on". The first request was already on
        /// the wire when that arrived, so it stands; **every later one must be inside 55 A**.
        /// </summary>
        [Test]
        public async Task Iso2Dc_ARunningLimitInCurrentDemandRes_ClampsEveryLaterRequest()
        {

            var secc = await RunAsync(runningMaxAmps: 55);

            var asked = secc.Requests.OfType<CurrentDemandReqType>()
                            .Select(r => (double) r.EVTargetCurrent.ToDecimal()).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(asked, Has.Length.EqualTo(3), "the three-iteration loop");
                Assert.That(asked[0], Is.EqualTo(120),
                            "the opening request predates any CurrentDemandRes and is the car's own ask");
                Assert.That(asked.Skip(1), Is.All.LessThanOrEqualTo(55),
                            "after a station states 55 A, asking for 120 A again is the defect this fixes");
            });

        }

        /// <summary>
        /// And the station's own view agrees: once it is serving at its stated ceiling it says so, instead
        /// of reporting <c>EVSECurrentLimitAchieved = false</c> while clamping.
        /// </summary>
        [Test]
        public async Task Iso2Dc_AStationServingAtItsStatedCeiling_SaysSo()
        {

            var secc = await RunAsync(runningMaxAmps: 55);

            Assert.That(secc.Responses.OfType<CurrentDemandResType>().Select(r => r.EVSECurrentLimitAchieved),
                        Is.All.True,
                        "a 120 A ask against a 55 A ceiling is served at the ceiling, in every iteration");

        }

        #endregion

        #region The advertised limit, stated once at discovery

        /// <summary>
        /// The envelope the station announces at ChargeParameterDiscovery binds the **first** request too —
        /// there is no grace iteration, because the car was told before it asked.
        /// </summary>
        [Test]
        public async Task Iso2Dc_AnAdvertisedLimitBelowTheAsk_ClampsFromTheFirstRequest()
        {

            var secc = await RunAsync(advertisedMaxAmps: 60);

            var asked = secc.Requests.OfType<CurrentDemandReqType>()
                            .Select(r => (double) r.EVTargetCurrent.ToDecimal()).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(asked, Is.Not.Empty);
                Assert.That(asked, Is.All.LessThanOrEqualTo(60),
                            "the discovery envelope was known before the loop opened");
            });

        }

        #endregion

        #region The default, unchanged

        /// <summary>
        /// With neither knob set the session is what every recorded run carries: 120 A asked, 120 A served,
        /// and no limit fields on the wire. <b>This one passes on the pre-fix code as well</b> — it is the
        /// guard that the fix did not move the default, not evidence that the fix works.
        /// </summary>
        [Test]
        public async Task Iso2Dc_WithNoLimitsSet_TheWireIsUnchanged()
        {

            var secc = await RunAsync();

            var asked = secc.Requests.OfType<CurrentDemandReqType>()
                            .Select(r => (double) r.EVTargetCurrent.ToDecimal()).ToArray();
            var stated = secc.Responses.OfType<CurrentDemandResType>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(asked,  Is.All.EqualTo(120));
                Assert.That(stated.Select(r => r.EVSEMaximumCurrentLimit), Is.All.Null);
                Assert.That(stated.Select(r => r.EVSEMaximumPowerLimit),   Is.All.Null);
                Assert.That(stated.Select(r => r.EVSEMaximumVoltageLimit), Is.All.Null);
                Assert.That(stated.Select(r => r.EVSECurrentLimitAchieved), Is.All.False);
            });

        }

        #endregion

        #region Harness

        private sealed class LimitedSecc2(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc2(PowerMode.Dc, sequenceTimeout, clock)
        {

            public List<BodyBaseType> Requests  { get; } = [];
            public List<BodyBaseType> Responses { get; } = [];

            public override V2G_Message Handle(V2G_Message request)
            {

                if (request.Body.BodyElement is { } body)
                    Requests.Add(body);

                var response = base.Handle(request);

                if (response.Body.BodyElement is { } answer)
                    Responses.Add(answer);

                return response;

            }

        }

        private static async Task<LimitedSecc2> RunAsync(Double? runningMaxAmps    = null,
                                                         Double? advertisedMaxAmps = null)
        {

            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new LimitedSecc2(TimeSpan.FromSeconds(60), TimeProvider.System) {
                           DcRunningMaxAmps    = runningMaxAmps,
                           DcAdvertisedMaxAmps = advertisedMaxAmps
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
