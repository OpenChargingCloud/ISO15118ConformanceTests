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
using cloud.charging.open.protocols.ISO15118.Transport;

using ISO15118ConformanceTests.Simulation.Interop;
using ISO15118ConformanceTests.Simulation.Timing;

namespace ISO15118ConformanceTests.Simulation.E2E
{

    /// <summary>
    /// A charge loop that ends when the car is done rather than after three iterations — the difference
    /// between a session that proves the message sequence and one that simulates charging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three-iteration loop is the harness default and every recorded vector was taken at it. It makes
    /// a "complete" session three <c>CurrentDemand</c> pairs — seconds — which is right for a conformance
    /// run and wrong for anything that wants to watch a station deliver energy. <c>EvBattery</c> has always
    /// been able to end the loop on a goal instead; until 2026-08-16 nothing outside the EVCC's own CLI
    /// could reach it, so no interop run had ever used it.
    /// </para>
    /// <para>
    /// <b>Two clocks, and they are not the same one.</b> One iteration stands for a minute of *simulated*
    /// charging (<c>ChargeLoopSample.Period</c>), while on the wire it costs whatever the exchange costs
    /// plus <see cref="Evcc2.ChargeLoopInterval"/>. A physically sensible charge is tens of iterations and
    /// is over in about a second of wall clock at the default 50 ms; the interval is what pulls the two
    /// apart. These tests pin the simulated side, which is the one with a defined answer — the wall-clock
    /// side is a property of the peer and belongs in a run note.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Iso2BatteryChargeLoopTests
    {

        private static async Task<(Evcc2 Evcc, Secc2 Secc)> RunDcSession(Action<Evcc2> configure)
        {
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var s = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(s, ProtocolVariant.Iso15118_2, cts.Token);
                var secc = new Secc2(PowerMode.Dc, TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(s, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                                       IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token);

            var evcc = new Evcc2(evccStream, PowerMode.Dc, TimeProvider.System,
                                 new ImmediateAsyncDelay(), LoopbackTimeouts.PerMessage);
            configure(evcc);

            await evcc.RunAsync(cts.Token);
            return (evcc, await seccTask);
        }

        /// <summary>Without a battery the loop is three iterations — the shape every recorded run has.</summary>
        [Test]
        public async Task WithoutABattery_TheLoopIsStillThreeIterations()
        {
            var (evcc, secc) = await RunDcSession(_ => { });

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone,      Is.True);
                Assert.That(evcc.Battery,     Is.Null);
                Assert.That(evcc.BatteryStop, Is.Null, "no battery, so nothing decides the loop is over");
            });
        }

        /// <summary>
        /// With one, the loop runs until the goal is met — and the goal, not the iteration ceiling, is what
        /// ended it. A fix that merely ran longer would satisfy the first assertion and fail the second.
        /// </summary>
        [Test]
        public async Task WithABattery_TheLoopEndsOnTheTargetStateOfCharge()
        {
            var (evcc, secc) = await RunDcSession(e => e.Battery = new EvBattery(60.0, 20.0)
                                                                  {
                                                                      TargetSoC     = 30.0,
                                                                      MaxIterations = 400,
                                                                  });

            Assert.That(secc.IsDone, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(evcc.BatteryStop, Is.EqualTo(ChargeStop.TargetSoC),
                            "the goal ended the loop, not the ceiling and not a fixed count");
                Assert.That(evcc.Battery!.SoC, Is.GreaterThanOrEqualTo(30.0));
                Assert.That(evcc.Battery.Iterations, Is.GreaterThan(3),
                            "a 10 % window cannot be crossed in the three iterations of the default loop");
            });
        }

        /// <summary>
        /// A goal that cannot be reached ends the arm instead of hanging on the counterparty, and says so.
        /// The ceiling is a guard, and a guard that fires silently is worse than none.
        /// </summary>
        [Test]
        public async Task AnUnreachableGoal_StopsAtTheCeilingAndReportsIt()
        {
            var (evcc, _) = await RunDcSession(e => e.Battery = new EvBattery(60.0, 20.0)
                                                               {
                                                                   TargetSoC     = 100.0,
                                                                   MaxIterations = 5,
                                                               });

            Assert.Multiple(() =>
            {
                Assert.That(evcc.BatteryStop, Is.EqualTo(ChargeStop.LoopLimit));
                Assert.That(evcc.Battery!.Describe(ChargeStop.LoopLimit),
                            Does.Contain("the goal was not reachable"));
            });
        }

        /// <summary>
        /// The interval is the car's own pacing and nothing else's: raising it must not be readable as the
        /// station having been slow, so it is a separate property from the shared poll interval.
        /// </summary>
        [Test]
        public async Task TheChargeLoopIntervalIsSeparateFromTheOtherPolls()
        {
            var (evcc, secc) = await RunDcSession(e =>
            {
                e.Battery            = new EvBattery(60.0, 20.0) { TargetSoC = 22.0, MaxIterations = 100 };
                e.ChargeLoopInterval = TimeSpan.FromMilliseconds(1);
            });

            Assert.Multiple(() =>
            {
                Assert.That(secc.IsDone, Is.True);
                Assert.That(evcc.ChargeLoopInterval, Is.EqualTo(TimeSpan.FromMilliseconds(1)));
                Assert.That(evcc.BatteryStop, Is.EqualTo(ChargeStop.TargetSoC),
                            "pacing changes when the requests go out, never whether the goal is reached");
            });
        }

    }


    /// <summary>
    /// The environment knobs behind the fixture, and the guard that a typo in one is refused rather than
    /// quietly leaving the three-iteration default in place.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class InteropBatteryEnvironmentTests
    {

        private string? battery, interval;

        [SetUp]
        public void Save()
        {
            battery  = Environment.GetEnvironmentVariable("V2G_INTEROP_BATTERY");
            interval = Environment.GetEnvironmentVariable("V2G_INTEROP_CHARGE_INTERVAL");
        }

        [TearDown]
        public void Restore()
        {
            Environment.SetEnvironmentVariable("V2G_INTEROP_BATTERY",         battery);
            Environment.SetEnvironmentVariable("V2G_INTEROP_CHARGE_INTERVAL", interval);
        }

        private static void Set(string? spec)
            => Environment.SetEnvironmentVariable("V2G_INTEROP_BATTERY", spec);

        [Test]
        public void UnsetIsNoBattery()
        {
            Set(null);
            Assert.That(InteropEnvironment.Battery(), Is.Null);
        }

        [Test]
        public void TheKeysReachTheBattery()
        {
            Set("kwh=42,soc=15,target=55,power=11,minsoc=40,taper=90,loops=250");

            var pack = InteropEnvironment.Battery();

            Assert.That(pack, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(pack!.CapacityWh,      Is.EqualTo(42_000.0));
                Assert.That(pack.StartSoC,         Is.EqualTo(15.0));
                Assert.That(pack.TargetSoC,        Is.EqualTo(55.0));
                Assert.That(pack.RequestedPowerW,  Is.EqualTo(11_000.0));
                Assert.That(pack.MinimumSoC,       Is.EqualTo(40.0));
                Assert.That(pack.TaperFromSoC,     Is.EqualTo(90.0));
                Assert.That(pack.MaxIterations,    Is.EqualTo(250));
            });
        }

        /// <summary>Naming no goal charges to full — the same rule the EVCC CLI applies.</summary>
        [Test]
        public void NamingNoGoalChargesToFull()
        {
            Set("soc=30");
            Assert.That(InteropEnvironment.Battery()!.TargetSoC, Is.EqualTo(100.0));
        }

        /// <summary>…and naming one of the other goals does not silently add a full-charge goal beside it.</summary>
        [Test]
        public void AnEnergyGoalIsNotJoinedByAFullCharge()
        {
            Set("energy=5");

            var pack = InteropEnvironment.Battery()!;
            Assert.Multiple(() =>
            {
                Assert.That(pack.TargetEnergyWh, Is.EqualTo(5_000.0));
                Assert.That(pack.TargetSoC,      Is.Null);
            });
        }

        /// <summary>
        /// The guard. A run whose battery spec was misspelled and silently ignored produces a session that
        /// looks complete, took three iterations, and measured something the operator did not ask for.
        /// </summary>
        [Test]
        public void AMisspelledKeyIsRefusedRatherThanIgnored()
        {
            Set("kwh=60,startsoc=20");

            Assert.That(() => InteropEnvironment.Battery(),
                        Throws.TypeOf<ArgumentException>().With.Message.Contains("startsoc"));
        }

        [Test]
        public void AValueThatIsNotANumberIsRefused()
        {
            Set("kwh=sixty");

            Assert.That(() => InteropEnvironment.Battery(), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// The budget is derived from the two knobs rather than left at 180 s. A paced battery session that
        /// ran into our own deadline would read as a station that stopped answering — the failure the
        /// `V2G_INTEROP_ONGOING` note describes, one layer out.
        /// </summary>
        [Test]
        public void TheSessionBudgetCoversTheWholePacedLoop()
        {
            Set("soc=20,target=70,loops=200");
            Environment.SetEnvironmentVariable("V2G_INTEROP_CHARGE_INTERVAL", "1000");

            Assert.That(InteropEnvironment.ChargeSessionBudget(),
                        Is.EqualTo(TimeSpan.FromSeconds(200 + 180)));
        }

        [Test]
        public void WithoutABatteryThereIsNoDerivedBudget()
        {
            Set(null);
            Environment.SetEnvironmentVariable("V2G_INTEROP_CHARGE_INTERVAL", "1000");

            Assert.That(InteropEnvironment.ChargeSessionBudget(), Is.Null);
        }

        /// <summary>
        /// The knobs reaching the <em>session</em>, not merely being readable — driven through
        /// <c>InteropSession.RunEvccAsync</c> against our own station, which is the only place the wiring
        /// exists.
        /// </summary>
        /// <remarks>
        /// <b>This is the test the SessionID knob did not have.</b> `Evcc2.SendSessionId` was added on
        /// 2026-08-11 and the object initializer in <c>RunEvccAsync</c> did not carry it, so every `-2`
        /// caller had it silently discarded and the one counterparty whose `[V2G2-460]` behaviour needed
        /// measuring had to be probed with raw Python instead. An environment reader that returns the right
        /// value proves nothing about the session; a variable that is read and then dropped is the shape of
        /// bug this file exists to prevent from recurring.
        /// </remarks>
        [Test]
        public async Task TheVariablesReachTheSessionAndNotJustTheReader()
        {
            Set("kwh=60,soc=20,target=24,loops=200");
            Environment.SetEnvironmentVariable("V2G_INTEROP_CHARGE_INTERVAL", "1");

            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var s = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(s, ProtocolVariant.Iso15118_2, cts.Token);
                var secc = new Secc2(PowerMode.Dc, TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(s, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync(
                                       IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token);

            var outcome = await InteropSession.RunEvccAsync(evccStream, ProtocolVariant.Iso15118_2,
                                                            PowerMode.Dc, cts.Token);

            Assert.That((await seccTask).IsDone, Is.True);
            Assert.That(outcome.Exchanges, Is.GreaterThan(0));
            Assert.That(outcome.BatteryStop, Is.EqualTo(ChargeStop.TargetSoC),
                        "V2G_INTEROP_BATTERY has to reach Evcc2, not merely parse — a knob that is read "
                      + "and then dropped in the object initializer is invisible to every other test here");
        }

    }

}
