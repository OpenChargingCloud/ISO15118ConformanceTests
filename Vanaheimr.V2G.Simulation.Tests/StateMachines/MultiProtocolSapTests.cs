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
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.StateMachines
{

    /// <summary>
    /// The multi-protocol SupportedAppProtocol offer: the EV announces everything it can run in one
    /// handshake and then runs whichever the station picked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until 2026-08-03 our EVCC announced exactly the one protocol it was constructed for, because the
    /// state machine was chosen before the handshake ran — the case EVerest's <c>IsoMux</c> exists for
    /// was the one thing the run against it could not exercise. Same shape as the Dynamic gap: a
    /// capability that reads as present because both halves exist separately.
    /// </para>
    /// <para>
    /// The station side carries its own half of the fix: the SECC used to answer SchemaID <b>1</b> as a
    /// literal rather than echoing the id of the entry it accepted — indistinguishable from correct for
    /// as long as every EVCC assigned SchemaID 1, which ours did. The first test is the one that sees
    /// it: the -2 entry it accepts is SchemaID <b>2</b>, and an EVCC told "1" would run -20 against a
    /// -2 station.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class MultiProtocolSapTests
    {

        /// <summary>-20 first, -2 second: a real car's preference order.</summary>
        private static SapOffer[] Both(PowerMode mode) =>
            [new(ProtocolVariant.Iso15118_20, mode), new(ProtocolVariant.Iso15118_2, mode)];


        [Test]
        public async Task AnIso2OnlyStationPicksTheSecondEntry_AndTheIso2SessionRuns()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                var settled = await SapHandshake.RunSeccSideAsync(seccStream,
                    [new SapOffer(ProtocolVariant.Iso15118_2, PowerMode.Ac)], cts.Token);
                Assert.That(settled.Protocol, Is.EqualTo(ProtocolVariant.Iso15118_2));
                await secc.RunAsync(seccStream, cts.Token);
            }, cts.Token);

            using (var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                     (TlsOptions?) null, cts.Token))
            {
                var accepted = await SapHandshake.RunEvccSideAsync(evccStream, Both(PowerMode.Ac), cts.Token);

                Assert.That(accepted, Is.EqualTo(new SapOffer(ProtocolVariant.Iso15118_2, PowerMode.Ac)),
                            "the station accepted the priority-2 entry, and its answered SchemaID (2) is "
                          + "the only thing that says so — a SECC echoing a literal 1 fails here");

                await new Evcc2(evccStream, accepted.Mode, TimeProvider.System, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage).RunAsync(cts.Token);
            }
            await seccTask;

            Assert.That(secc.IsDone, Is.True, "the negotiated -2 session ran to its terminal state");
        }


        [Test]
        public async Task AnIso20StationPicksTheFirstEntry_AndTheIso20SessionRuns()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream,
                    [new SapOffer(ProtocolVariant.Iso15118_20, PowerMode.Dc)], cts.Token);
                await secc.RunAsync(seccStream, cts.Token);
            }, cts.Token);

            using (var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                     (TlsOptions?) null, cts.Token))
            {
                var accepted = await SapHandshake.RunEvccSideAsync(evccStream, Both(PowerMode.Dc), cts.Token);

                Assert.That(accepted.Protocol, Is.EqualTo(ProtocolVariant.Iso15118_20));

                await new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(),
                                   LoopbackTimeouts.PerMessage).RunAsync(cts.Token);
            }
            await seccTask;

            Assert.That(secc.IsDone, Is.True, "the negotiated -20 session ran to its terminal state");
        }


        /// <summary>A station that supports both follows the EV's ranking, not its own — the EV putting
        /// -2 first is unusual but legal. Our station honours it; EVerest's <c>IsoMux</c>, measured on
        /// 2026-08-03, does not (it routes to -20 whenever -20 appears anywhere in the offer), which is
        /// the case this test would have caught had they been the peer.</summary>
        [Test]
        public async Task ABothCapableStationFollowsTheEvsPriority()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);

                // The station supports both; which one runs is the EV's call.
                var settled = await SapHandshake.RunSeccSideAsync(seccStream,
                    [new SapOffer(ProtocolVariant.Iso15118_20, PowerMode.Ac),
                     new SapOffer(ProtocolVariant.Iso15118_2,  PowerMode.Ac)], cts.Token);

                Assert.That(settled.Protocol, Is.EqualTo(ProtocolVariant.Iso15118_2),
                            "the EV ranked -2 first, so a both-capable station settles on -2");
                await secc.RunAsync(seccStream, cts.Token);
            }, cts.Token);

            using (var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                     (TlsOptions?) null, cts.Token))
            {
                var accepted = await SapHandshake.RunEvccSideAsync(evccStream,
                    [new SapOffer(ProtocolVariant.Iso15118_2,  PowerMode.Ac),    // priority 1
                     new SapOffer(ProtocolVariant.Iso15118_20, PowerMode.Ac)],   // priority 2
                    cts.Token);

                Assert.That(accepted.Protocol, Is.EqualTo(ProtocolVariant.Iso15118_2));

                await new Evcc2(evccStream, accepted.Mode, TimeProvider.System, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage).RunAsync(cts.Token);
            }
            await seccTask;

            Assert.That(secc.IsDone, Is.True);
        }


        /// <summary>Nothing in common refuses on both sides, each naming what it saw.</summary>
        [Test]
        public async Task NothingInCommonIsRefusedOnBothSides()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                var thrown = Assert.ThrowsAsync<SessionAborted>(async () =>
                    await SapHandshake.RunSeccSideAsync(seccStream,
                        [new SapOffer(ProtocolVariant.Iso15118_20, PowerMode.Dc)], cts.Token));
                Assert.That(thrown!.Message, Does.Contain("offered none of"));
            }, cts.Token);

            using (var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                     (TlsOptions?) null, cts.Token))
            {
                var thrown = Assert.ThrowsAsync<SessionAborted>(async () =>
                    await SapHandshake.RunEvccSideAsync(evccStream,
                        [new SapOffer(ProtocolVariant.Iso15118_2, PowerMode.Ac)], cts.Token));
                Assert.That(thrown!.Message, Does.Contain("Failed_NoNegotiation"));
            }
            await seccTask;
        }

    }

}
