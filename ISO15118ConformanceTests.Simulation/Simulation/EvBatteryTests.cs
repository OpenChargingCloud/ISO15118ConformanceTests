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

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Metering;
using cloud.charging.open.protocols.ISO15118.Simulation;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.Timing;

using Dc20Rational = cloud.charging.open.protocols.ISO15118_20.DC.RationalNumber;

namespace ISO15118ConformanceTests.Simulation.Simulation
{

    /// <summary>
    /// The battery, its goals, and the watts-to-rational conversion the charge loop asks for.
    /// </summary>
    /// <remarks>
    /// Written after a review found two defects here that a green session-level run could not: a silent
    /// <c>short</c> overflow in <see cref="Evcc20Dc"/>'s watt conversion, and a goal list that read as
    /// nonsense with more than two goals. Both live below the level any loopback E2E can see — the
    /// session simply charges and stops — which is what this file exists for.
    /// </remarks>
    [TestFixture]
    public class EvBatteryTests
    {

        /// <summary>Reaches the two protected members the charge loop derives from a battery.</summary>
        private sealed class Probe : Evcc20Dc
        {
            public Probe(EvBattery? battery)
                : base(Stream.Null, TimeProvider.System, new TaskAsyncDelay(), TimeSpan.FromSeconds(1))
            {
                Battery = battery;
            }

            public static decimal WattsAsDecimal(double watts) => Dc20Rational.ToDecimal(Watts(watts));
            public decimal TargetCurrent => Dc20Rational.ToDecimal(LoopTargetCurrent);
            public decimal LoopPower     => Dc20Rational.ToDecimal(LoopMaxPower);
            public sbyte   DeclaredSoC   => DeclaredTargetSoC;
        }

        private static Probe ProbeWith(EvBattery? battery) => new(battery);


        // ── the pack itself ────────────────────────────────────────────────────

        [Test]
        public void StateOfCharge_FollowsWhatWentIn()
        {
            var b = new EvBattery(capacityKWh: 60, startSoCPercent: 20);
            Assert.That(b.SoC, Is.EqualTo(20).Within(0.001));

            b.Add(6_000);   // 6 kWh into a 60 kWh pack is ten points

            Assert.Multiple(() =>
            {
                Assert.That(b.SoC,          Is.EqualTo(30).Within(0.001));
                Assert.That(b.DeliveredWh,  Is.EqualTo(6_000).Within(0.001));
                Assert.That(b.Iterations,   Is.EqualTo(1));
                Assert.That(b.Elapsed,      Is.EqualTo(ChargeLoopSample.Period), "one iteration is one sample period");
            });
        }

        /// <summary>A pack does not take more than it holds, and the surplus is not counted as delivered.</summary>
        [Test]
        public void Charge_IsClampedAtCapacity()
        {
            var b = new EvBattery(10, 90);      // 9 kWh in a 10 kWh pack
            b.Add(5_000);                       // room for 1 kWh only

            Assert.Multiple(() =>
            {
                Assert.That(b.SoC,         Is.EqualTo(100).Within(0.001));
                Assert.That(b.DeliveredWh, Is.EqualTo(1_000).Within(0.001), "the station may push; the pack still only took 1 kWh");
                Assert.That(b.Stop,        Is.EqualTo(ChargeStop.Full));
            });
        }

        /// <summary>Negative energy is a bidirectional session exporting, and it empties rather than fills.</summary>
        [Test]
        public void Export_TakesEnergyBackOut_AndStopsAtEmpty()
        {
            var b = new EvBattery(10, 10);      // 1 kWh in the pack
            b.Add(-5_000);

            Assert.Multiple(() =>
            {
                Assert.That(b.EnergyWh, Is.EqualTo(0).Within(0.001), "a pack cannot export what it does not have");
                Assert.That(b.SoC,      Is.EqualTo(0).Within(0.001));
            });
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        public void ACapacityOfNothing_IsRefused(double kWh)
            => Assert.That(() => new EvBattery(kWh, 50), Throws.InstanceOf<ArgumentOutOfRangeException>());

        [TestCase(-0.1)]
        [TestCase(100.1)]
        public void AStateOfChargeOutsideTheScale_IsRefused(double soc)
            => Assert.That(() => new EvBattery(60, soc), Throws.InstanceOf<ArgumentOutOfRangeException>());


        // ── the goals ──────────────────────────────────────────────────────────

        [Test]
        public void NoGoal_RunsUntilTheCeiling_RatherThanForever()
        {
            var b = new EvBattery(60, 50) { MaxIterations = 3 };
            for (int i = 0; i < 3; i++)
            {
                Assert.That(b.Stop, Is.EqualTo(ChargeStop.Running), $"iteration {i} should not end it");
                b.Add(1);
            }
            Assert.That(b.Stop, Is.EqualTo(ChargeStop.LoopLimit));
        }

        [Test]
        public void TargetSoC_EndsTheLoop()
        {
            var b = new EvBattery(60, 20) { TargetSoC = 25 };
            Assert.That(b.Stop, Is.EqualTo(ChargeStop.Running));
            b.Add(3_000);                       // 20 % + 5 points
            Assert.That(b.Stop, Is.EqualTo(ChargeStop.TargetSoC));
        }

        [Test]
        public void TargetEnergy_CountsWhatWasDelivered_NotWhatThePackHolds()
        {
            var b = new EvBattery(60, 50) { TargetEnergyWh = 2_000 };
            b.Add(1_000);
            Assert.That(b.Stop, Is.EqualTo(ChargeStop.Running), "half of it is not it");
            b.Add(1_000);
            Assert.That(b.Stop, Is.EqualTo(ChargeStop.TargetEnergy));
        }

        [Test]
        public void TimeLimitAndDeparture_BothEndIt_OnSimulatedTime()
        {
            var time = new EvBattery(60, 50) { MaxDuration = 2 * ChargeLoopSample.Period };
            var trip = new EvBattery(60, 50) { DepartureIn = 2 * ChargeLoopSample.Period };

            time.Add(1); trip.Add(1);
            Assert.Multiple(() =>
            {
                Assert.That(time.Stop, Is.EqualTo(ChargeStop.Running));
                Assert.That(trip.Stop, Is.EqualTo(ChargeStop.Running));
            });

            time.Add(1); trip.Add(1);
            Assert.Multiple(() =>
            {
                Assert.That(time.Stop, Is.EqualTo(ChargeStop.TimeLimit));
                Assert.That(trip.Stop, Is.EqualTo(ChargeStop.Departure));
            });
        }

        /// <summary>
        /// A full pack ends the session whatever else was asked for — the order a driver would care about,
        /// and the reason <c>Stop</c> checks capacity before every named goal.
        /// </summary>
        [Test]
        public void AFullPack_OutranksEveryOtherGoal()
        {
            var b = new EvBattery(10, 95)
            {
                TargetSoC      = 50,                        // already passed
                TargetEnergyWh = 1,                         // already passed
                MaxDuration    = ChargeLoopSample.Period,   // about to pass
            };
            b.Add(1_000);   // fills it

            Assert.That(b.Stop, Is.EqualTo(ChargeStop.Full));
        }


        // ── the minimum, which is not a goal ───────────────────────────────────

        /// <summary>
        /// The floor never ends a session and never prolongs one: it is a verdict on the session that the
        /// other goals ended. Charging past a departure time is not something a car can do.
        /// </summary>
        [Test]
        public void MinimumSoC_DoesNotStopTheLoop_AndDoesNotExtendIt()
        {
            var b = new EvBattery(60, 20) { MinimumSoC = 80, DepartureIn = ChargeLoopSample.Period };

            b.Add(600);   // one point, nowhere near the minimum

            Assert.Multiple(() =>
            {
                Assert.That(b.Stop, Is.EqualTo(ChargeStop.Departure), "departure still ends it");
                Assert.That(b.MinimumSoCMissed, Is.True);
                Assert.That(b.Describe(b.Stop), Does.Contain("NOT ENOUGH"),
                            "and the run says the car left without enough");
            });
        }

        [Test]
        public void MinimumSoC_Met_IsSaidSo()
        {
            var b = new EvBattery(60, 70) { MinimumSoC = 60, MaxDuration = ChargeLoopSample.Period };
            b.Add(0);

            Assert.Multiple(() =>
            {
                Assert.That(b.MinimumSoCMissed, Is.False);
                Assert.That(b.Describe(b.Stop), Does.Contain("minimum was met"));
                Assert.That(b.Describe(b.Stop), Does.Not.Contain("NOT ENOUGH"));
            });
        }

        [Test]
        public void NoMinimumAsked_SaysNothingAboutOne()
        {
            var b = new EvBattery(60, 70) { MaxDuration = ChargeLoopSample.Period };
            b.Add(0);

            Assert.That(b.Describe(b.Stop), Does.Not.Contain("minimum").IgnoreCase);
        }


        // ── watts as a -20 rational ────────────────────────────────────────────

        [TestCase(9_000.0,    9_000.0, TestName = "Watts_9kW")]
        [TestCase(11_000.0,  11_000.0, TestName = "Watts_11kW")]
        [TestCase(0.0,             0.0, TestName = "Watts_zero")]
        [TestCase(32_767.0,   32_767.0, TestName = "Watts_atTheSignedLimit")]
        public void Watts_KeepsTheValue_WhileItFits(double watts, double expected)
            => Assert.That((double) Probe.WattsAsDecimal(watts), Is.EqualTo(expected).Within(1.0));

        /// <summary>Above the 16-bit value the exponent absorbs the difference, at three digits' cost.</summary>
        [Test]
        public void Watts_ShiftsIntoTheExponent_WhenItNoLongerFits()
        {
            var mw = (double) Probe.WattsAsDecimal(3_750_000);   // MCS scale
            Assert.That(mw, Is.EqualTo(3_750_000).Within(10_000), "3.75 MW, to the precision an exponent leaves");
        }

        /// <summary>
        /// The one this file was written for. Beyond 32767×10³ W the exponent has run out, and the cast
        /// used to wrap: a very large power became a <em>negative</em> one on the wire — schema-valid and
        /// nonsense. It saturates instead.
        /// </summary>
        [Test]
        public void Watts_Saturates_RatherThanWrappingNegative()
        {
            var absurd = (double) Probe.WattsAsDecimal(1e12);

            Assert.Multiple(() =>
            {
                Assert.That(absurd, Is.GreaterThan(0), "a huge power must never come out negative");
                Assert.That(absurd, Is.EqualTo(32_767e3).Within(1.0), "it saturates at the largest value the type holds");
            });
        }

        [Test]
        public void Watts_Saturates_TheOtherWayToo()
            => Assert.That((double) Probe.WattsAsDecimal(-1e12), Is.EqualTo(-32_767e3).Within(1.0));


        // ── what the loop derives from the battery ─────────────────────────────

        [Test]
        public void WithoutABattery_TheLoopKeepsItsOwnDefaults()
        {
            var p = ProbeWith(null);

            Assert.Multiple(() =>
            {
                Assert.That((double) p.TargetCurrent, Is.EqualTo(120).Within(0.001), "the Scheduled setpoint it always had");
                Assert.That((double) p.LoopPower,     Is.EqualTo(50_000).Within(1.0));
                Assert.That(p.DeclaredSoC,            Is.EqualTo((sbyte) 80), "the conventional DC knee");
            });
        }

        /// <summary>
        /// <c>--power</c> reaching the Scheduled setpoint is the whole of why it is felt at all: the first
        /// attempt bound it only in the Dynamic arms and charged at 48 kW when 9 was asked for.
        /// </summary>
        [Test]
        public void RequestedPower_BecomesTheScheduledSetpoint_AtTheLoopsOwnVoltage()
        {
            var p = ProbeWith(new EvBattery(60, 20) { RequestedPowerW = 9_000 });

            Assert.Multiple(() =>
            {
                Assert.That((double) p.TargetCurrent, Is.EqualTo(23).Within(1.0), "9 kW at 400 V, to the nearest amp");
                Assert.That((double) p.LoopPower,     Is.EqualTo(9_000).Within(1.0));
            });
        }

        [Test]
        public void RequestedPower_IsCappedByWhatTheVehicleDeclared()
        {
            var p = ProbeWith(new EvBattery(60, 20) { RequestedPowerW = 10_000_000 });

            Assert.That((double) p.TargetCurrent, Is.EqualTo(200).Within(0.001),
                        "a car cannot ask for more current than the envelope it declared");
        }

        [Test]
        public void TargetSoC_IsDeclaredToTheStation()
        {
            Assert.Multiple(() =>
            {
                Assert.That(ProbeWith(new EvBattery(60, 20) { TargetSoC = 55 }).DeclaredSoC, Is.EqualTo((sbyte) 55));
                Assert.That(ProbeWith(new EvBattery(60, 20)).DeclaredSoC, Is.EqualTo((sbyte) 80),
                            "no target named: the value this stood at unconditionally");
            });
        }

    }

}
