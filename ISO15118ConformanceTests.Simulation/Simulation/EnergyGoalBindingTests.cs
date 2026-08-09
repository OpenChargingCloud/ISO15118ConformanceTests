/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of ISO15118ConformanceTests
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

using System.Net;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Simulation;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.Transport;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;
using cloud.charging.open.protocols.ISO15118_2;
using cloud.charging.open.protocols.ISO15118_2.Generated;

using ISO15118ConformanceTests.Simulation.Timing;

using Common20     = cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;
using Dc20         = cloud.charging.open.protocols.ISO15118_20.DC.Generated;
using Ac20         = cloud.charging.open.protocols.ISO15118_20.AC.Generated;
using Dc20Rational = cloud.charging.open.protocols.ISO15118_20.DC.RationalNumber;
using Ac20Rational = cloud.charging.open.protocols.ISO15118_20.AC.RationalNumber;
using Rational20   = cloud.charging.open.protocols.ISO15118_20.CommonMessages.RationalNumber;

namespace ISO15118ConformanceTests.Simulation.Simulation
{

    /// <summary>
    /// What the energy goals put on the wire, asserted on the messages the station actually received.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>At the field, unlike its sibling.</b> <see cref="RequestedPowerBindingTests"/> measures
    /// <c>--power</c> at the two meters, because a power that reaches the wire changes what is delivered.
    /// An energy goal changes nothing that is delivered — it is a <i>declaration</i>, the car telling a
    /// station what to plan for — so the only place it can be seen is the request itself. Hence the
    /// recording stations below: they keep every request they were handed, and the assertions are on
    /// values that made the round trip through the codec.
    /// </para>
    /// <para>
    /// <b>The figures shrink, and that is the point.</b> All three -20 energy fields are what is
    /// <i>left</i>: how much the car still wants, how much it can still take, how much it still needs.
    /// They were 30 / 60 / 10 kWh — three constants a Dynamic station scheduled against, unchanged by
    /// anything the session did. The first test asserts the shrink rather than a single value, because a
    /// binding that put the right figure in the first request and then froze would pass on a snapshot.
    /// </para>
    /// <para>
    /// Every case has its no-battery baseline: those literals are what the recorded interop runs and the
    /// session corpus carry, and they must not have moved.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class EnergyGoalBindingTests
    {

        /// <summary>60 kWh at 20 % = 12 000 Wh in the pack, charging to 24 % = 14 400 Wh, so 2 400 Wh
        /// wanted — three iterations of the 48 kW both DC loops default to.</summary>
        private static EvBattery Pack(double? minimumSoC = null)
            => new(capacityKWh: 60, startSoCPercent: 20) { TargetSoC = 24, MinimumSoC = minimumSoC };

        private const double WantedWh     =  2_400;   // to 24 %
        private const double AcceptableWh = 48_000;   // to 100 %
        private const double PerIteration =    800;   // 48 kW for one minute


        // ── ISO 15118-20 ───────────────────────────────────────────────────────

        /// <summary>
        /// The Scheduled DC loop: the triple is there, it is what the pack actually wants, and it falls by
        /// what the station delivered each time round.
        /// </summary>
        [Test]
        public async Task Iso20Dc_TheEnergyTriple_IsWhatIsLeft_AndShrinks()
        {
            var secc = await RunIso20Async(Pack(minimumSoC: 22),
                                           (t, c) => new RecordingSecc20Dc(t, c),
                                           s => new Evcc20Dc(s, TimeProvider.System, new ImmediateAsyncDelay(),
                                                             LoopbackTimeouts.PerMessage));

            var loops = secc.Requests.OfType<Dc20.DC_ChargeLoopReq>()
                            .Select(r => (Dc20.Scheduled_DC_CLReqControlModeType) r.CLReqControlMode)
                            .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(loops, Has.Length.EqualTo(3), "2 400 Wh at 800 Wh an iteration");

                for (var i = 0; i < loops.Length; i++)
                {
                    Assert.That((double) Dc20Rational.ToDecimal(loops[i].EVTargetEnergyRequest!),
                                Is.EqualTo(WantedWh - i * PerIteration).Within(1.0),
                                $"iteration {i}: what is still wanted");
                    Assert.That((double) Dc20Rational.ToDecimal(loops[i].EVMaximumEnergyRequest!),
                                Is.EqualTo(AcceptableWh - i * PerIteration).Within(1.0),
                                $"iteration {i}: what the pack can still take");
                }

                // 22 % of 60 kWh is 13 200 Wh, so 1 200 short at the start and met during the second.
                Assert.That((double) Dc20Rational.ToDecimal(loops[0].EVMinimumEnergyRequest!),
                            Is.EqualTo(1_200).Within(1.0), "what the driver still needs");
                Assert.That((double) Dc20Rational.ToDecimal(loops[2].EVMinimumEnergyRequest!),
                            Is.EqualTo(0).Within(1.0), "the minimum was reached, so nothing more is needed");
            });
        }

        /// <summary>Without a pack the three stay absent — Scheduled leaves them optional, and this car
        /// had nothing to say in them until it grew a battery.</summary>
        [Test]
        public async Task Iso20Dc_WithoutABattery_TheScheduledTripleStaysAbsent()
        {
            var secc = await RunIso20Async(null,
                                           (t, c) => new RecordingSecc20Dc(t, c),
                                           s => new Evcc20Dc(s, TimeProvider.System, new ImmediateAsyncDelay(),
                                                             LoopbackTimeouts.PerMessage));

            var loop = secc.Requests.OfType<Dc20.DC_ChargeLoopReq>()
                           .Select(r => (Dc20.Scheduled_DC_CLReqControlModeType) r.CLReqControlMode)
                           .First();

            Assert.Multiple(() =>
            {
                Assert.That(loop.EVTargetEnergyRequest,  Is.Null);
                Assert.That(loop.EVMaximumEnergyRequest, Is.Null);
                Assert.That(loop.EVMinimumEnergyRequest, Is.Null);
            });
        }

        /// <summary>
        /// Dynamic mode's ScheduleExchange request is the one place in -20 where the state-of-charge goals
        /// are the car's to state — the charge-loop request carries neither — so this is where
        /// <c>--target-soc</c> and <c>--min-soc</c> land as percentages rather than as watt-hours.
        /// </summary>
        [Test]
        public async Task Iso20Dynamic_TheScheduleExchange_CarriesBothSoCGoals()
        {
            var secc = await RunIso20Async(Pack(minimumSoC: 22),
                                           (t, c) => new RecordingSecc20Dc(t, c),
                                           s => new Evcc20Dc(s, TimeProvider.System, new ImmediateAsyncDelay(),
                                                             LoopbackTimeouts.PerMessage)
                                                { PreferDynamicControlMode = true });

            var mode = secc.Requests.OfType<Common20.ScheduleExchangeReq>()
                           .Select(r => r.Dynamic_SEReqControlMode).First(m => m is not null)!;

            Assert.Multiple(() =>
            {
                Assert.That(mode.TargetSOC,  Is.EqualTo((sbyte) 24), "--target-soc, not the old constant 80");
                Assert.That(mode.MinimumSOC, Is.EqualTo((sbyte) 22), "--min-soc, not the old constant 30");
                Assert.That((double) Rational20.ToDecimal(mode.EVTargetEnergyRequest),
                            Is.EqualTo(WantedWh).Within(1.0));
                Assert.That((double) Rational20.ToDecimal(mode.EVMaximumEnergyRequest),
                            Is.EqualTo(AcceptableWh).Within(1.0));
            });
        }

        /// <summary>And the constants without one: 30 / 80 % and 30 / 60 / 10 kWh, which is what every
        /// recorded Dynamic run carries.</summary>
        [Test]
        public async Task Iso20Dynamic_WithoutABattery_KeepsTheRecordedConstants()
        {
            var secc = await RunIso20Async(null,
                                           (t, c) => new RecordingSecc20Dc(t, c),
                                           s => new Evcc20Dc(s, TimeProvider.System, new ImmediateAsyncDelay(),
                                                             LoopbackTimeouts.PerMessage)
                                                { PreferDynamicControlMode = true });

            var mode = secc.Requests.OfType<Common20.ScheduleExchangeReq>()
                           .Select(r => r.Dynamic_SEReqControlMode).First(m => m is not null)!;

            Assert.Multiple(() =>
            {
                Assert.That(mode.TargetSOC,  Is.EqualTo((sbyte) 80));
                Assert.That(mode.MinimumSOC, Is.EqualTo((sbyte) 30));
                Assert.That((double) Rational20.ToDecimal(mode.EVTargetEnergyRequest),  Is.EqualTo(30_000).Within(1.0));
                Assert.That((double) Rational20.ToDecimal(mode.EVMaximumEnergyRequest), Is.EqualTo(60_000).Within(1.0));
                Assert.That((double) Rational20.ToDecimal(mode.EVMinimumEnergyRequest), Is.EqualTo(10_000).Within(1.0));
            });
        }

        /// <summary>-20 AC carries the same triple, and had the same three constants.</summary>
        [Test]
        public async Task Iso20Ac_TheEnergyTriple_IsWhatIsLeft()
        {
            var secc = await RunIso20Async(new EvBattery(60, 20) { TargetSoC = 21 },
                                           (t, c) => new RecordingSecc20Ac(t, c),
                                           s => new Evcc20Ac(s, TimeProvider.System, new ImmediateAsyncDelay(),
                                                             LoopbackTimeouts.PerMessage),
                                           PowerMode.Ac);

            var first = secc.Requests.OfType<Ac20.AC_ChargeLoopReq>()
                            .Select(r => (Ac20.Scheduled_AC_CLReqControlModeType) r.CLReqControlMode).First();

            Assert.Multiple(() =>
            {
                Assert.That((double) Ac20Rational.ToDecimal(first.EVTargetEnergyRequest!),
                            Is.EqualTo(600).Within(1.0), "20 % to 21 % of a 60 kWh pack");
                Assert.That((double) Ac20Rational.ToDecimal(first.EVMaximumEnergyRequest!),
                            Is.EqualTo(48_000).Within(1.0));
            });
        }


        // ── ISO 15118-2 ────────────────────────────────────────────────────────

        /// <summary>
        /// -2 DC states the pack outright: <c>EVEnergyCapacity</c> is the only place in either protocol
        /// where a car says how big its battery is, and <c>EVEnergyRequest</c> beside it says how much of
        /// that it wants. Both were absent until there was a pack to describe.
        /// </summary>
        [Test]
        public async Task Iso2Dc_DiscoveryStatesTheCapacityAndTheRequest()
        {
            var secc = await RunIso2Async(PowerMode.Dc, Pack());

            var dc = secc.Requests.OfType<ChargeParameterDiscoveryReqType>()
                         .Select(r => r.EVChargeParameter).OfType<DC_EVChargeParameterType>().First();

            Assert.Multiple(() =>
            {
                Assert.That((double) dc.EVEnergyCapacity!.ToDecimal(), Is.EqualTo(60_000).Within(1.0));
                Assert.That((double) dc.EVEnergyRequest!.ToDecimal(),  Is.EqualTo(WantedWh).Within(1.0));
            });
        }

        /// <summary>
        /// And the one -2 field that moves during a session. <c>EVRESSSOC</c> rides in every request of the
        /// DC sequence and was a flat 50 % — so a station watching the car charge saw a constant.
        /// </summary>
        [Test]
        public async Task Iso2Dc_ThePresentStateOfCharge_RisesAsThePackFills()
        {
            var secc = await RunIso2Async(PowerMode.Dc, Pack());

            var socs = secc.Requests.OfType<CurrentDemandReqType>()
                           .Select(r => r.DC_EVStatus.EVRESSSOC).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(socs, Has.Length.EqualTo(3));
                Assert.That(socs[0], Is.EqualTo((sbyte) 20), "where it started");
                Assert.That(socs[^1], Is.GreaterThan(socs[0]), "the reported state of charge never moved");
            });
        }

        /// <summary>-2 AC has one energy field, <c>EAmount</c>, and it is the request rather than the pack:
        /// how much this session wants. 22 kWh when nothing asked.</summary>
        [Test]
        public async Task Iso2Ac_EAmountIsTheRequest()
        {
            var withPack = await RunIso2Async(PowerMode.Ac, new EvBattery(60, 20) { TargetSoC = 21 });
            var without  = await RunIso2Async(PowerMode.Ac, null);

            Assert.Multiple(() =>
            {
                Assert.That((double) FirstAc(withPack).EAmount.ToDecimal(), Is.EqualTo(600).Within(1.0),
                            "20 % to 21 % of a 60 kWh pack");
                Assert.That((double) FirstAc(without).EAmount.ToDecimal(), Is.EqualTo(22_000).Within(1.0),
                            "the literal every recorded -2 AC run carries");
            });

            static AC_EVChargeParameterType FirstAc(RecordingSecc2 secc)
                => secc.Requests.OfType<ChargeParameterDiscoveryReqType>()
                       .Select(r => r.EVChargeParameter).OfType<AC_EVChargeParameterType>().First();
        }

        /// <summary>The -2 DC baseline: absent, absent, 50 %.</summary>
        [Test]
        public async Task Iso2Dc_WithoutABattery_KeepsTheRecordedShape()
        {
            var secc = await RunIso2Async(PowerMode.Dc, null);

            var dc = secc.Requests.OfType<ChargeParameterDiscoveryReqType>()
                         .Select(r => r.EVChargeParameter).OfType<DC_EVChargeParameterType>().First();

            Assert.Multiple(() =>
            {
                Assert.That(dc.EVEnergyCapacity, Is.Null);
                Assert.That(dc.EVEnergyRequest,  Is.Null);
                Assert.That(dc.DC_EVStatus.EVRESSSOC, Is.EqualTo((sbyte) 50));
            });
        }


        // ── recording stations, and the harnesses ──────────────────────────────

        private sealed class RecordingSecc20Dc(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Dc(sequenceTimeout, clock)
        {
            public List<object> Requests { get; } = [];

            public override (MessageSet Set, object Response) Handle(MessageSet set, object request)
            {
                Requests.Add(request);
                return base.Handle(set, request);
            }
        }

        private sealed class RecordingSecc20Ac(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Ac(sequenceTimeout, clock)
        {
            public List<object> Requests { get; } = [];

            public override (MessageSet Set, object Response) Handle(MessageSet set, object request)
            {
                Requests.Add(request);
                return base.Handle(set, request);
            }
        }

        private sealed class RecordingSecc2(PowerMode mode, TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc2(mode, sequenceTimeout, clock)
        {
            public List<BodyBaseType> Requests { get; } = [];

            public override V2G_Message Handle(V2G_Message request)
            {
                if (request.Body.BodyElement is { } body)
                    Requests.Add(body);
                return base.Handle(request);
            }
        }

        private static async Task<RecordingSecc2> RunIso2Async(PowerMode mode, EvBattery? battery)
        {
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = new RecordingSecc2(mode, TimeSpan.FromSeconds(60), TimeProvider.System);

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token, mode);
                try { await secc.RunAsync(seccStream, cts.Token); }
                catch { /* the assertions are on what was received */ }
            }, cts.Token);

            using (var stream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                (TlsOptions?) null, cts.Token))
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, cts.Token, mode);
                await new Evcc2(stream, mode, TimeProvider.System, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage) { Battery = battery }.RunAsync(cts.Token);
            }
            await seccTask;

            return secc;
        }

        private static async Task<T> RunIso20Async<T>(EvBattery? battery,
                                                      Func<TimeSpan, TimeProvider, T> makeSecc,
                                                      Func<Stream, Evcc20Base> makeEvcc,
                                                      PowerMode mode = PowerMode.Dc)
            where T : Secc20Base
        {
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var secc = makeSecc(TimeSpan.FromSeconds(60), TimeProvider.System);

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token, mode);
                try { await secc.RunAsync(seccStream, cts.Token); }
                catch { /* the assertions are on what was received */ }
            }, cts.Token);

            using (var stream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                (TlsOptions?) null, cts.Token))
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, cts.Token, mode);
                var evcc = makeEvcc(stream);
                evcc.Battery = battery;
                await evcc.RunAsync(cts.Token);
            }
            await seccTask;

            return secc;
        }

    }

}
