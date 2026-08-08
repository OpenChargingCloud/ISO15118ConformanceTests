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

using cloud.charging.open.protocols.ISO15118_2.Generated;
using cloud.charging.open.protocols.ISO15118.Framing;
using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using ISO15118ConformanceTests.Simulation.Timing;
using cloud.charging.open.protocols.ISO15118.Timing;
using cloud.charging.open.protocols.ISO15118.Transport;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{

    /// <summary>
    /// What our EVCCs do when a station answers promptly and never finishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until 2026-08-02 the answer was: poll for ever. Against EVerest's <c>EvseV2G</c> that meant 1 170
    /// <c>AuthorizationReq</c> in three minutes — their station answering <c>OK</c> with
    /// <c>EVSEProcessing = Ongoing</c> every time, correctly, because nothing had authorized the session
    /// (<c>docs/interop-runs/2026-08-02-everest-iso2-dc-notls/</c>).
    /// </para>
    /// <para>
    /// The gap sat between two timeouts that each looked like it covered the case: the per-message
    /// timeout fires when a response is <i>late</i>, and all 1 170 were fast; the cancellation token ends
    /// the whole session rather than one phase. This fixture is the station our own SECC can never be —
    /// one that keeps saying <c>Ongoing</c>.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class OngoingTimeoutTests
    {

        /// <summary>Short enough that the test is instant, long enough that a few polls happen first.</summary>
        private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(150);


        [Test]
        public void TheGuardNamesThePhaseAndHowLongItWaited()
        {
            var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            var guard = new OngoingGuard(clock, TimeSpan.FromSeconds(60), "Authorization");

            guard.Tick();                                   // nothing has elapsed
            clock.Advance(TimeSpan.FromSeconds(59));
            guard.Tick();                                   // still inside the limit

            clock.Advance(TimeSpan.FromSeconds(2));
            var thrown = Assert.Throws<SessionAborted>(guard.Tick);

            Assert.Multiple(() =>
            {
                Assert.That(thrown!.Message, Does.Contain("Authorization"));
                Assert.That(thrown!.Message, Does.Contain("61"), "the message says how long it actually waited");
                Assert.That(thrown!.Message, Does.Contain("60"), "and what the limit was");
            });
        }


        /// <summary>A -2 station that authorizes for ever, which is what EVerest legitimately did.</summary>
        private static async Task RunNeverAuthorizingStationAsync(Stream stream, CancellationToken ct)
        {
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
            var buffer = new byte[8192];

            while (!ct.IsCancellationRequested)
            {
                var (_, message) = await V2GTPStream.ReadFrameAsync(stream, ct);
                var request = (V2G_Message) message;

                // The poll is answered here rather than by Secc2, whose sequence guard rejects a second
                // AuthorizationReq — it has moved on, as a station that authorizes normally would. What
                // is being reproduced is a station that never moves on.
                var reply = request.Body.BodyElement is AuthorizationReqType
                                ? new V2G_Message(request.Header,
                                      new BodyType(new AuthorizationResType(ResponseCode.OK,
                                                                            EVSEProcessing.Ongoing)))
                                : secc.Handle(request);

                if (!reply.TryEncode(buffer, out var length))
                    throw new InvalidOperationException("encode failed");

                await V2GTPStream.WriteFrameAsync(stream, MessageSet.Iso15118_2, buffer.AsMemory(0, length), ct);
            }
        }


        [Test]
        public async Task Iso2_AnEndlessAuthorizationPhaseEndsTheSession()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Ac);
                try { await RunNeverAuthorizingStationAsync(seccStream, cts.Token); }
                catch { /* the EV hangs up; that is the point */ }
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                   (TlsOptions?) null, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token, PowerMode.Ac);

            var evcc = new Evcc2(evccStream, PowerMode.Ac, TimeProvider.System, new ImmediateAsyncDelay(),
                                 LoopbackTimeouts.PerMessage)
                       { OngoingTimeout = Limit };

            var aborted = Assert.ThrowsAsync<SessionAborted>(async () => await evcc.RunAsync(cts.Token));

            Assert.Multiple(() =>
            {
                Assert.That(aborted!.Message, Does.Contain("Authorization"));
                Assert.That(aborted!.Message, Does.Contain("Ongoing"));

                // It polled — this is a phase deadline, not a refusal to poll at all.
                Assert.That(evcc.Exchanges, Is.GreaterThan(4), "the EV should poll before giving up");
            });

            evccStream.Dispose();
            await seccTask;
        }


        /// <summary>The -20 half of the same hole.</summary>
        private sealed class NeverAuthorizingStation20(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Dc(sequenceTimeout, clock)
        {
            public override (MessageSet Set, object Response) Handle(MessageSet set, object request)
            {
                // Answered before the base state machine sees it, for the same reason as the -2 station:
                // its sequence guard rejects a second AuthorizationReq, because a station that authorizes
                // normally has moved on by then.
                if (request is cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.AuthorizationReq)
                    return (MessageSet.Iso20CommonMessages,
                            new cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.AuthorizationRes(
                                SessionCtx.ToCommonHeader(),
                                cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.ResponseCode.OK,
                                cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.Processing.Ongoing));

                return base.Handle(set, request);
            }
        }


        [Test]
        public async Task Iso20_AnEndlessAuthorizationPhaseEndsTheSession()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new NeverAuthorizingStation20(TimeSpan.FromSeconds(60), TimeProvider.System);
                try { await secc.RunAsync(seccStream, cts.Token); }
                catch { /* as above */ }
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                   (TlsOptions?) null, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(),
                                    LoopbackTimeouts.PerMessage)
                       { OngoingTimeout = Limit };

            var aborted = Assert.ThrowsAsync<SessionAborted>(async () => await evcc.RunAsync(cts.Token));

            Assert.That(aborted!.Message, Does.Contain("Authorization").And.Contain("Ongoing"));

            evccStream.Dispose();
            await seccTask;
        }

    }

}
