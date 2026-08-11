/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Net;
using System.Runtime.CompilerServices;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.Transport;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;
using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;

using ISO15118ConformanceTests.Simulation.Timing;

using Ac20 = cloud.charging.open.protocols.ISO15118_20.AC.Generated;
using Dc20 = cloud.charging.open.protocols.ISO15118_20.DC.Generated;

#endregion

namespace ISO15118ConformanceTests.Simulation.StateMachines
{

    /// <summary>
    /// ISO 15118-20: <b>a request this phase does not permit, or one that does not carry this session's id,
    /// is answered rather than dropped.</b> `[V2G20-459]` and `[V2G20-460]`.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The `-2` twin of this fixture was written on 2026-08-11 and this one could not be, for a reason worth
    /// keeping: <c>Secc20Base</c> had <b>no table of corresponding responses at all</b>. Its sequence guard
    /// <em>threw</em> — the wildcard arm of the phase switch raised <c>SessionAborted</c> under a comment
    /// naming the code it would have sent — so the station killed the connection instead of answering, and
    /// `[V2G20-460]` had nothing to answer <i>with</i>. One table serves both requirements; building it
    /// across the three generated message sets was the work, and EVerest's <c>d20/context_helper.cpp</c> —
    /// the same table in C++, dispatched over all sixteen of their `-20` types — was the worked example.
    /// </para>
    /// <para>
    /// <b>The `-20` refusal is terminal, and the `-2` one is not.</b> §8.6 makes a <c>FAILED</c> response a
    /// fatal error that both sides end the session on; `-2` §8.8.2 has no such sentence, so its station
    /// stays put and a car that corrects itself charges. Two of the tests below exist to pin that
    /// difference, which is the standards' and not ours.
    /// </para>
    /// <para>
    /// <b>What this cost, and it is the interesting part.</b> Adding the `[V2G20-460]` check turned
    /// <b>32 passing tests red at once</b>. <see cref="SessionContext.SessionId"/> starts as eight zero
    /// bytes, and every `-20` fixture that drove <c>Handle</c> from its own context had therefore been
    /// modelling a car that sends the all-zero SessionID — for its whole existence — against a station with
    /// no check to fail. That is the same value, and the same shape, as the defect this project filed
    /// against EVerest's `-2` station (<c>docs/reports/everest-evsev2g-session-id-zero.md</c>). It is the
    /// second time this month a guard's arrival revealed what the harnesses had been sending;
    /// <see cref="Iso20Handshake"/> is where the one-line fix now lives.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Iso20UnknownSessionTests
    {

        // Not an id any station of ours issues: SessionSetupRes assigns eight random bytes.
        private static readonly Byte[] SomeoneElsesId = [0xDE, 0xAD, 0xBE, 0xEF, 0xDE, 0xAD, 0xBE, 0xEF];
        private static readonly Byte[] TheZeroId      = new Byte[8];


        #region [V2G20-460] — a foreign session id

        /// <summary>
        /// The first request after SessionSetup carries eight bytes this station never issued. It is an
        /// <c>AuthorizationSetupReq</c>, in sequence and valid in every other respect, so the only thing
        /// that can refuse it is `[V2G20-460]`.
        /// </summary>
        [Test]
        public async Task Iso20_ARequestWithAForeignSessionId_IsRefusedAsUnknownSession()
        {

            var (secc, evccError) = await RunAsync(SomeoneElsesId);

            Assert.Multiple(() =>
            {
                Assert.That(secc.UnknownSessionAt, Is.EqualTo("AuthorizationSetupReq"),
                            "the guard fires on the first request after SessionSetup, which is where the car first echoes an id");
                Assert.That(secc.UnknownSessionRefusals, Is.EqualTo(1));
                Assert.That(secc.SequenceErrorAt,        Is.Null,
                            "a wrong id is not a sequence error, and answering it as one would misreport the reason");
                Assert.That(evccError,                   Is.Not.Null, "the car sees a FAILED code and ends the session");
                Assert.That(evccError!.Message,          Does.Contain("FAILED_UnknownSession"));
            });

        }


        /// <summary>
        /// Eight zero bytes — the value ISO reserves for <i>"I have no session"</i> and the one a station is
        /// likeliest to special-case. A test of its own because a real station was measured serving it:
        /// EVerest's <c>EvseV2G</c> guards its `-2` check with <c>received_session_id != 0</c>. It is also
        /// what every `-20` fixture here was quietly sending until this guard arrived.
        /// </summary>
        [Test]
        public async Task Iso20_ARequestWithTheAllZeroSessionId_IsRefusedToo()
        {

            var (secc, evccError) = await RunAsync(TheZeroId);

            Assert.Multiple(() =>
            {
                Assert.That(secc.UnknownSessionAt, Is.EqualTo("AuthorizationSetupReq"),
                            "zero is not equal to the stored id, and [V2G20-460] has no exemption for it");
                Assert.That(evccError,             Is.Not.Null);
                Assert.That(evccError!.Message,    Does.Contain("FAILED_UnknownSession"));
            });

        }


        /// <summary>
        /// With the knob unset the session is what every recorded run carries: the car echoes the id it was
        /// given, the station refuses nothing, and the charge completes. <b>This one passes on the pre-fix
        /// station too</b> — it pins that the guard did not start refusing ordinary sessions, which is the
        /// failure mode a table of twenty arms has.
        /// </summary>
        [Test]
        public async Task Iso20_WithNoSessionIdOverride_TheSessionIsUnchanged()
        {

            var (secc, evccError) = await RunAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(evccError,                   Is.Null, "an ordinary session still completes");
                Assert.That(secc.UnknownSessionAt,       Is.Null);
                Assert.That(secc.UnknownSessionRefusals, Is.Zero);
                Assert.That(secc.SequenceErrorAt,        Is.Null);
                Assert.That(secc.SequenceErrorRefusals,  Is.Zero);
            });

        }

        #endregion

        #region [V2G20-459] — an out-of-sequence request

        /// <summary>
        /// A <c>ScheduleExchangeReq</c> in the AuthorizationSetup phase. Before 2026-08-11 this
        /// <b>threw</b>, so the car got a closed socket and no reason; now it gets the response that pairs
        /// with its own request, carrying <c>FAILED_SequenceError</c>.
        /// </summary>
        [Test]
        public void Iso20_AnOutOfSequenceRequest_IsAnsweredRatherThanThrown()
        {

            var ctx  = new SessionContext(TimeProvider.System);
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            ctx.OpenSession(secc);

            var (set, response) = secc.Handle(MessageSet.Iso20CommonMessages,
                                              new ScheduleExchangeReq(ctx.ToCommonHeader(), 1, null, null));

            Assert.Multiple(() =>
            {
                Assert.That(set,      Is.EqualTo(MessageSet.Iso20CommonMessages));
                Assert.That(response, Is.InstanceOf<ScheduleExchangeRes>(),
                            "a ScheduleExchangeReq is answered by a ScheduleExchangeRes, refusal or not");
                Assert.That(((ScheduleExchangeRes) response).ResponseCode,
                            Is.EqualTo(ResponseCode.FAILED_SequenceError));
                Assert.That(secc.SequenceErrorAt,       Is.EqualTo("ScheduleExchangeReq"));
                Assert.That(secc.SequenceErrorRefusals, Is.EqualTo(1));
                Assert.That(secc.IsDone,                Is.True,
                            "-20 §8.6: a FAILED response is fatal and the station ends the session — unlike -2");
            });

        }


        /// <summary>
        /// The same, one message set over: a DC request refused in DC's own types, with DC's own
        /// <c>ResponseCode</c>. This is the half the base class cannot build and
        /// <c>Secc20Dc.RefuseInEnergyTransferSet</c> exists for.
        /// </summary>
        [Test]
        public void Iso20_AnOutOfSequenceDcRequest_IsAnsweredInTheDcSet()
        {

            var ctx  = new SessionContext(TimeProvider.System);
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            ctx.OpenSession(secc);

            var (set, response) = secc.Handle(MessageSet.Iso20DC,
                                              new Dc20.DC_CableCheckReq(ctx.ToDcHeader()));

            Assert.Multiple(() =>
            {
                Assert.That(set,      Is.EqualTo(MessageSet.Iso20DC), "the answer belongs to the set that asked");
                Assert.That(response, Is.InstanceOf<Dc20.DC_CableCheckRes>());
                Assert.That(((Dc20.DC_CableCheckRes) response).ResponseCode,
                            Is.EqualTo(Dc20.ResponseCode.FAILED_SequenceError));
            });

        }

        #endregion

        #region The table has no hole

        /// <summary>
        /// <b>Every</b> concrete `-20` request type, in all three generated message sets, is answered by the
        /// response type that pairs with it. Twenty types today: thirteen in CommonMessages, two in AC, five
        /// in DC.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The failure mode of a hand-written table of twenty arms is a missing arm, and nothing else here
        /// would find one: an arm that does not exist is only reached by a peer sending that type at the
        /// wrong moment, which no other test does. So the types are enumerated from the assemblies rather
        /// than listed here — a message set gaining a type in a future schema revision fails this test
        /// instead of silently falling through to the <c>NotSupportedException</c>.
        /// </para>
        /// <para>
        /// The instances are uninitialised on purpose (<see cref="RuntimeHelpers.GetUninitializedObject"/>):
        /// what is under test is the dispatch, and building twenty real requests would test the constructors
        /// instead. <see cref="Secc20Base.Refuse"/> reads a field of the request in only three arms, and each
        /// of those tolerates the default — which is itself worth pinning, since a refusal must not depend on
        /// the contents of what it refuses.
        /// </para>
        /// </remarks>
        [Test]
        public void Iso20_EveryRequestType_HasACorrespondingRefusalResponse()
        {

            var dc = new RefusalProbeDc(TimeSpan.FromSeconds(60), TimeProvider.System);
            var ac = new RefusalProbeAc(TimeSpan.FromSeconds(60), TimeProvider.System);

            var cases = new List<(String Name, MessageSet Set, object Request, Secc20Base Station)>();

            foreach (var t in ConcreteRequests(typeof(V2GRequestType)))
                cases.Add((t.Name, MessageSet.Iso20CommonMessages, RuntimeHelpers.GetUninitializedObject(t), dc));

            foreach (var t in ConcreteRequests(typeof(Dc20.V2GRequestType)))
                cases.Add((t.Name, MessageSet.Iso20DC, RuntimeHelpers.GetUninitializedObject(t), dc));

            foreach (var t in ConcreteRequests(typeof(Ac20.V2GRequestType)))
                cases.Add((t.Name, MessageSet.Iso20AC, RuntimeHelpers.GetUninitializedObject(t), ac));

            Assert.That(cases, Has.Count.EqualTo(20),
                        "13 CommonMessages + 5 DC + 2 AC request types; a different count means the schemas moved");

            Assert.Multiple(() =>
            {
                foreach (var (name, set, request, station) in cases)
                {

                    var expected = name[..^3] + "Res";   // XReq → XRes

                    var response = station is RefusalProbeDc probeDc
                                       ? probeDc.RefuseFor(set, request)
                                       : ((RefusalProbeAc) station).RefuseFor(set, request);

                    Assert.That(response.Response.GetType().Name, Is.EqualTo(expected),
                                $"{name} must be refused with a {expected}");
                }
            });

        }


        /// <summary>
        /// The concrete request <b>messages</b> of one generated message set.
        /// </summary>
        /// <remarks>
        /// The <c>Req</c> suffix is doing real work: each set generates a message (<c>SessionSetupReq</c>)
        /// <em>and</em> the schema complex type behind it (<c>SessionSetupReqType</c>), both deriving from
        /// <c>V2GRequestType</c>, which is why the unfiltered count is exactly double. Only the messages
        /// ever reach <see cref="Secc20Base.Handle"/> — the decoder returns those — so only those need an
        /// arm in the table.
        /// </remarks>
        private static IEnumerable<Type> ConcreteRequests(Type requestBase) =>
            requestBase.Assembly
                       .GetTypes()
                       .Where(t => !t.IsAbstract && requestBase.IsAssignableFrom(t) && t.Name.EndsWith("Req"))
                       .OrderBy(t => t.Name);

        #endregion

        #region Harness

        /// <summary>Exposes the protected refusal table; the reason is spelled out at
        /// <see cref="Iso20_EveryRequestType_HasACorrespondingRefusalResponse"/>.</summary>
        private sealed class RefusalProbeDc(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Dc(sequenceTimeout, clock)
        {
            public (MessageSet Set, object Response) RefuseFor(MessageSet set, object request) =>
                Refuse(set, request, Refusal.SequenceError);
        }

        private sealed class RefusalProbeAc(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Ac(sequenceTimeout, clock)
        {
            public (MessageSet Set, object Response) RefuseFor(MessageSet set, object request) =>
                Refuse(set, request, Refusal.SequenceError);
        }


        /// <summary>
        /// One loopback `-20` DC session whose car puts <paramref name="sendSessionId"/> in every request
        /// after SessionSetup. Returns the station and whatever ended the car — a refusal ends the car, and
        /// the station's read then fails on the closed stream, so both are expected here rather than faults.
        /// </summary>
        private static async Task<(Secc20Dc Secc, SessionAborted? EvccError)> RunAsync(Byte[]? sendSessionId)
        {

            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);

            var seccTask = Task.Run(async () =>
            {
                try
                {
                    using var seccStream = await listener.AcceptAsync(cts.Token);
                    await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                    await secc.RunAsync(seccStream, cts.Token);
                }
                catch (Exception)
                {
                    // The car hangs up after a FAILED response, so the next read has nothing to read. The
                    // station's own verdict is in UnknownSessionAt, which is what the tests assert on.
                }
            }, cts.Token);

            SessionAborted? evccError = null;

            using (var stream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(),
                                                                listener.LocalEndpoint.Port, ct: cts.Token))
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, cts.Token);

                var evcc = new Evcc20Dc(stream, TimeProvider.System, new ImmediateAsyncDelay(),
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
