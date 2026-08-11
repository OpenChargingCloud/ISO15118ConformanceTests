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
using cloud.charging.open.protocols.ISO15118.Session;
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
    /// ISO 15118-2: <b>a request that does not carry this session's id is refused.</b> `[V2G2-460]`.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The requirement is one sentence and admits no exception: the response shall carry
    /// <c>FAILED_UnknownSession</c> if the SessionID in any request message <em>except SessionSetupReq</em>
    /// is not equal to the SessionID stored for the currently active session. Until 2026-08-11 this station
    /// implemented none of it — <c>FAILED_UnknownSession</c> appeared nowhere in the live tree — so any
    /// header at all was served as the session owner's.
    /// </para>
    /// <para>
    /// <b>Nothing here could have caught it before, and that is the second half of the fix.</b> This car had
    /// no way to send a SessionID other than the one it was given, so a loopback session never put a wrong
    /// one in front of the station, and no interop run had ever exercised <em>any</em> station's duty to
    /// refuse one. <see cref="Evcc2.SendSessionId"/> exists for that, opt-in and defaulting to the real id
    /// so every recorded session keeps its bytes — the shape of <c>Evcc20Base.RequestMeterInfo</c> and of the
    /// running-limit clamp before it. Third time this month that a question the car could not ask hid an
    /// answer nobody checked.
    /// </para>
    /// <para>
    /// It was found by reading EVerest's station for the same rule, where the check exists and exempts
    /// exactly one value — zero — which is why the zero case below is a test of its own rather than a
    /// variation (<c>docs/reports/everest-evsev2g-session-id-zero.md</c>).
    /// </para>
    /// <para>
    /// <b>Three of the four tests fail on the pre-fix station.</b> The fourth pins the unchanged default and
    /// passes either way; it is here so the guard cannot quietly start refusing ordinary sessions.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Iso2UnknownSessionTests
    {

        // Not the id any station of ours issues: SessionSetupRes assigns eight random bytes.
        private static readonly Byte[] SomeoneElsesId = [0xDE, 0xAD, 0xBE, 0xEF, 0xDE, 0xAD, 0xBE, 0xEF];
        private static readonly Byte[] TheZeroId      = new Byte[8];


        #region A foreign session id

        /// <summary>
        /// The first request after SessionSetup carries eight bytes this station never issued. It is a
        /// <c>ServiceDiscoveryReq</c>, in sequence, and valid in every other respect — so the only thing
        /// that can refuse it is `[V2G2-460]`.
        /// </summary>
        [Test]
        public async Task Iso2_ARequestWithAForeignSessionId_IsRefusedAsUnknownSession()
        {

            var (secc, evccError) = await RunAsync(SomeoneElsesId);

            Assert.Multiple(() =>
            {
                Assert.That(secc.UnknownSessionAt, Is.EqualTo("ServiceDiscoveryReq"),
                            "the guard fires on the first request after SessionSetup, which is where the car first echoes an id");
                Assert.That(secc.UnknownSessionRefusals, Is.EqualTo(1));
                Assert.That(evccError, Is.Not.Null, "the car sees a FAILED code and ends the session");
                Assert.That(evccError!.Message, Does.Contain("FAILED_UnknownSession"));
            });

        }


        /// <summary>
        /// And the refusal is carried by <b>the response that pairs with the request</b> — `[V2G2-538]` asks
        /// for the corresponding response message, not for a generic error or a closed socket. Closing the
        /// socket is what this station used to do for the sequence guard, and what a car cannot tell from a
        /// dead charger.
        /// </summary>
        [Test]
        public async Task Iso2_TheRefusalIsCarriedByTheCorrespondingResponseType()
        {

            var (secc, _) = await RunAsync(SomeoneElsesId);

            var last = secc.Responses[^1];

            Assert.Multiple(() =>
            {
                Assert.That(last, Is.InstanceOf<ServiceDiscoveryResType>(),
                            "a ServiceDiscoveryReq is answered by a ServiceDiscoveryRes, refusal or not");
                Assert.That(((ServiceDiscoveryResType) last).ResponseCode,
                            Is.EqualTo(ResponseCode.FAILED_UnknownSession));
            });

        }

        #endregion

        #region The zero session id

        /// <summary>
        /// Eight zero bytes, which is the value ISO reserves for <i>"I have no session"</i> and the one a
        /// station is likeliest to special-case. A test of its own because a real station was measured
        /// serving it: EVerest's <c>EvseV2G</c> guards its own `[V2G2-460]` check with
        /// <c>received_session_id != 0</c>, so the one id that needs no knowledge of the session walks past.
        /// </summary>
        [Test]
        public async Task Iso2_ARequestWithTheAllZeroSessionId_IsRefusedToo()
        {

            var (secc, evccError) = await RunAsync(TheZeroId);

            Assert.Multiple(() =>
            {
                Assert.That(secc.UnknownSessionAt, Is.EqualTo("ServiceDiscoveryReq"),
                            "zero is not equal to the stored id, and [V2G2-460] has no exemption for it");
                Assert.That(evccError, Is.Not.Null);
                Assert.That(evccError!.Message, Does.Contain("FAILED_UnknownSession"));
            });

        }

        #endregion

        #region The default, unchanged

        /// <summary>
        /// With the knob unset the session is what every recorded run carries: the car echoes the id it was
        /// given, the station refuses nothing, and the charge completes. <b>This one passes on the pre-fix
        /// station as well</b> — it is the guard that the fix did not move the default, not evidence that
        /// the fix works.
        /// </summary>
        [Test]
        public async Task Iso2_WithNoSessionIdOverride_TheSessionIsUnchanged()
        {

            var (secc, evccError) = await RunAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(evccError,                   Is.Null, "an ordinary session still completes");
                Assert.That(secc.UnknownSessionAt,       Is.Null);
                Assert.That(secc.UnknownSessionRefusals, Is.Zero);
                Assert.That(secc.SequenceErrorAt,        Is.Null, "and the sequence guard is untouched by this");
            });

        }

        #endregion

        #region Harness

        private sealed class RecordingSecc2(TimeSpan sequenceTimeout, TimeProvider clock)
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

        /// <summary>
        /// One loopback session whose car puts <paramref name="sendSessionId"/> in every request after
        /// SessionSetup. Returns the station and whatever ended the car, since a refusal ends the car and
        /// then the station's read fails on the closed stream — both are expected here rather than faults.
        /// </summary>
        private static async Task<(RecordingSecc2 Secc, SessionAborted? EvccError)> RunAsync(Byte[]? sendSessionId)
        {

            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new RecordingSecc2(TimeSpan.FromSeconds(60), TimeProvider.System);

            var seccTask = Task.Run(async () =>
            {
                try
                {
                    using var seccStream = await listener.AcceptAsync(cts.Token);
                    await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Dc);
                    await secc.RunAsync(seccStream, cts.Token);
                }
                catch (Exception)
                {
                    // The car hangs up after a FAILED response, so the next read has nothing to read. The
                    // station's own verdict is in UnknownSessionAt, which is what the tests assert on.
                }
            }, cts.Token);

            SessionAborted? evccError = null;

            using (var stream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                               (TlsOptions?) null, cts.Token))
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Dc);

                var evcc = new Evcc2(stream, PowerMode.Dc, TimeProvider.System, new ImmediateAsyncDelay(),
                                     LoopbackTimeouts.PerMessage) {
                               SendSessionId = sendSessionId
                           };

                try
                {
                    await evcc.RunAsync(cts.Token);
                }
                catch (SessionAborted e)
                {
                    evccError = e;
                }
            }

            await seccTask;

            return (secc, evccError);

        }

        #endregion

    }

}
