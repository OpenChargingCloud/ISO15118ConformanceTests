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
using System.Security.Cryptography;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Metering;
using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Simulation;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.Transport;

using ISO15118ConformanceTests.Simulation.Timing;

namespace ISO15118ConformanceTests.Simulation.Simulation
{

    /// <summary>
    /// What <c>--power</c> does to a wire, in all four places there is a wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured at the two meters, not at the field.</b> Each protocol and power mode carries the ask in
    /// a different field — a Scheduled setpoint in -20 DC, a present active power in -20 AC, a target
    /// current in -2 DC, a committed ChargingProfile in -2 AC — and asserting on the field would mean four
    /// tests that each restate the code beside them. The two counters are the honest question instead: the
    /// vehicle's is derived from what it asked for and the station's from what it announced, by two state
    /// machines that never read each other's totals, so agreeing on a figure that <em>moved because of a
    /// flag</em> is evidence the flag reached the wire and was understood at the far end.
    /// </para>
    /// <para>
    /// <b>Why it exists.</b> <c>--power</c> was bound in -20 DC first and only there, and a session asked
    /// for 9 kW charged at 48. The three that followed are here so the same silence cannot happen again in
    /// the modes nobody was looking at, and so does the no-battery baseline beside each of them: the
    /// recorded interop runs and the session corpus are all taken without a battery, and the figures below
    /// are what "unchanged" means for them.
    /// </para>
    /// <para>
    /// One charge-loop iteration is one <see cref="ChargeLoopSample.Period"/> — a minute — so a watt-hour
    /// figure here is the power divided by sixty, rounded per sample. Each session is bounded by a target
    /// state of charge a few hundred watt-hours above where it starts, which is why they finish in three
    /// iterations rather than the several hundred a real goal would take.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class RequestedPowerBindingTests
    {

        /// <summary>A pack that stops after 450 Wh — three iterations at 9 kW, or three at 12 kW if the
        /// caller asks for 200 Wh worth. Starts at 20 %, well below the taper knee, so these sessions
        /// measure the binding and not the curve (<c>EvBatteryTests</c> measures the curve).</summary>
        private static EvBattery Pack(double requestedPowerW, double wattHoursWanted)
            => new(capacityKWh: 60, startSoCPercent: 20)
               {
                   RequestedPowerW = requestedPowerW,
                   TargetSoC       = 20 + 100.0 * wattHoursWanted / 60_000,
               };


        // ── ISO 15118-2 ────────────────────────────────────────────────────────

        /// <summary>
        /// -2 DC: the ask is <c>EVTargetCurrent</c>, at the 400 V the same request reports. The station
        /// serves it rather than the flat 120 A it used to announce whatever it was sent — which is the
        /// half of this that makes the two counters agree at anything other than 48 kW.
        /// </summary>
        [Test]
        public async Task Iso2Dc_ChargesAtTheRequestedPower()
        {
            var (evcc, station) = await RunIso2Async(PowerMode.Dc, Pack(12_000, 600));

            Assert.Multiple(() =>
            {
                Assert.That(evcc.Meter.Samples,  Is.EqualTo(3), "600 Wh at 200 Wh an iteration");
                Assert.That(evcc.Meter.EnergyWh, Is.EqualTo(600), "12 kW is 30 A at 400 V, and 200 Wh a minute");
                Assert.That(station, Is.EqualTo(evcc.Meter.EnergyWh),
                            "the station announced a current the vehicle did not ask for");
                Assert.That(evcc.BatteryStop, Is.EqualTo(ChargeStop.TargetSoC));
            });
        }

        /// <summary>And without one, the 120 A every recorded -2 DC run was taken at.</summary>
        [Test]
        public async Task Iso2Dc_WithoutAPower_KeepsTheFigureEveryRecordedRunCarries()
        {
            var (evcc, station) = await RunIso2Async(PowerMode.Dc, battery: null);

            Assert.Multiple(() =>
            {
                Assert.That(evcc.Meter.EnergyWh, Is.EqualTo(2_400), "400 V x 120 A, three iterations");
                Assert.That(station, Is.EqualTo(evcc.Meter.EnergyWh));
            });
        }

        /// <summary>
        /// -2 AC: there is no charge-loop request to put a power in — <c>ChargingStatusReq</c> is empty —
        /// so the ask is the ChargingProfile the EV commits to at <c>PowerDeliveryReq(Start)</c>, capped
        /// below the station's PMax. Both sides then meter that profile, which is the only power either of
        /// them ever sees.
        /// </summary>
        [Test]
        public async Task Iso2Ac_CommitsToTheRequestedPower_BelowTheStationsOffer()
        {
            var (evcc, station) = await RunIso2Async(PowerMode.Ac, Pack(9_000, 450));

            Assert.Multiple(() =>
            {
                Assert.That(evcc.Meter.Samples,  Is.EqualTo(3));
                Assert.That(evcc.Meter.EnergyWh, Is.EqualTo(450), "9 kW is 150 Wh a minute");
                Assert.That(station, Is.EqualTo(evcc.Meter.EnergyWh));
            });
        }

        /// <summary>Asking for more than the station offers changes nothing: the profile follows PMax, and
        /// [V2G2-761] would refuse it at PowerDelivery if it did not.</summary>
        [Test]
        public async Task Iso2Ac_AboveTheStationsOffer_StaysAtPMax()
        {
            var (evcc, _) = await RunIso2Async(PowerMode.Ac, Pack(20_000, 552));

            Assert.That(evcc.Meter.EnergyWh, Is.EqualTo(552),
                        "11.04 kW — the station's three-phase 16 A offer, which 20 kW cannot exceed");
        }

        /// <summary>
        /// A power below what the contactor can switch is raised to it, not honoured. 6 A per phase is the
        /// minimum this EV declares at discovery, and a profile promising less than the current beside it
        /// would be the vehicle contradicting itself in one message pair.
        /// </summary>
        [Test]
        public async Task Iso2Ac_BelowTheContactorsMinimum_ChargesAtTheMinimum()
        {
            var (evcc, station) = await RunIso2Async(PowerMode.Ac, Pack(1_000, 207));

            Assert.Multiple(() =>
            {
                Assert.That(evcc.Meter.EnergyWh, Is.EqualTo(207),
                            "6 A on three phases at 400 V is 4157 W, so 69 Wh a minute — not the 17 Wh 1 kW would be");
                Assert.That(station, Is.EqualTo(evcc.Meter.EnergyWh));
            });
        }

        /// <summary>And without a battery, the station's full offer, as before.</summary>
        [Test]
        public async Task Iso2Ac_WithoutAPower_DrawsTheWholeOffer()
        {
            var (evcc, _) = await RunIso2Async(PowerMode.Ac, battery: null);

            Assert.That(evcc.Meter.EnergyWh, Is.EqualTo(552), "11.04 kW for three iterations");
        }


        // ── ISO 15118-20 ───────────────────────────────────────────────────────

        /// <summary>
        /// -20 AC: the ask is <c>EVPresentActivePower</c>, which the vehicle sends every iteration and the
        /// station now meters. That is AC's control model and not a workaround — the station offers an
        /// envelope, the car decides what to draw inside it — and it is why AC needs no setpoint field.
        /// </summary>
        [Test]
        public async Task Iso20Ac_DrawsTheRequestedPower()
        {
            var (evcc, station) = await RunIso20AcAsync(Pack(9_000, 450));

            Assert.Multiple(() =>
            {
                Assert.That(evcc.Meter.Samples,  Is.EqualTo(3));
                Assert.That(evcc.Meter.EnergyWh, Is.EqualTo(450), "9 kW is 150 Wh a minute");
                Assert.That(station, Is.EqualTo(evcc.Meter.EnergyWh),
                            "the station metered its own 22 kW instead of what the vehicle reported drawing");
                Assert.That(evcc.BatteryStop, Is.EqualTo(ChargeStop.TargetSoC));
            });
        }

        /// <summary>More than the inlet takes is held at the inlet, and the station meters what it is
        /// given rather than more.</summary>
        [Test]
        public async Task Iso20Ac_AboveTheInlet_IsHeldAtTheInlet()
        {
            var (evcc, station) = await RunIso20AcAsync(Pack(100_000, 1_101));

            Assert.Multiple(() =>
            {
                Assert.That(evcc.Meter.EnergyWh, Is.EqualTo(1_101), "22 kW, this vehicle's declared envelope");
                Assert.That(station, Is.EqualTo(evcc.Meter.EnergyWh));
            });
        }

        /// <summary>And without one, the 22 kW the corpus recorded.</summary>
        [Test]
        public async Task Iso20Ac_WithoutAPower_KeepsTheRecordedFigure()
        {
            var (evcc, station) = await RunIso20AcAsync(battery: null);

            Assert.Multiple(() =>
            {
                Assert.That(evcc.Meter.EnergyWh, Is.EqualTo(1_101), "367 Wh a minute, three iterations");
                Assert.That(station, Is.EqualTo(evcc.Meter.EnergyWh));
            });
        }

        /// <summary>
        /// -20 DC, the one that was bound first — here for the same end-to-end reason as the other three.
        /// <c>EvBatteryTests</c> checks the derivation; this checks that it survives a session.
        /// </summary>
        [Test]
        public async Task Iso20Dc_ChargesAtTheRequestedPower()
        {
            var (evcc, station) = await RunIso20DcAsync(Pack(12_000, 600));

            Assert.Multiple(() =>
            {
                Assert.That(evcc.Meter.EnergyWh, Is.EqualTo(600), "12 kW is 30 A at 400 V");
                Assert.That(station, Is.EqualTo(evcc.Meter.EnergyWh));
            });
        }


        // ── harnesses ──────────────────────────────────────────────────────────

        private static SigningMeter StationMeter() =>
            new("VAN*M*4711", ECDsa.Create(ECCurve.NamedCurves.nistP256), TimeProvider.System);

        /// <summary>One -2 session over a loopback socket, with the given pack; both counters back.</summary>
        private static async Task<(Evcc2 Evcc, ulong? StationReading)> RunIso2Async(
            PowerMode mode, EvBattery? battery)
        {
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var meter = StationMeter();
            var secc  = new Secc2(mode, TimeSpan.FromSeconds(60), TimeProvider.System) { InstalledMeter = meter };

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token, mode);
                await secc.RunAsync(seccStream, cts.Token);
            }, cts.Token);

            Evcc2 evcc;
            using (var stream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                (TlsOptions?) null, cts.Token))
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_2, cts.Token, mode);
                evcc = new Evcc2(stream, mode, TimeProvider.System, new ImmediateAsyncDelay(),
                                 LoopbackTimeouts.PerMessage) { Battery = battery };
                await evcc.RunAsync(cts.Token);
            }
            await seccTask;

            return (evcc, meter.Read().Wh);
        }

        private static Task<(Evcc20Base Evcc, ulong? StationReading)> RunIso20AcAsync(EvBattery? battery)
            => RunIso20Async(PowerMode.Ac, battery,
                             m => new Secc20Ac(TimeSpan.FromSeconds(60), TimeProvider.System) { InstalledMeter = m },
                             s => new Evcc20Ac(s, TimeProvider.System, new ImmediateAsyncDelay(),
                                               LoopbackTimeouts.PerMessage));

        private static Task<(Evcc20Base Evcc, ulong? StationReading)> RunIso20DcAsync(EvBattery? battery)
            => RunIso20Async(PowerMode.Dc, battery,
                             m => new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System) { InstalledMeter = m },
                             s => new Evcc20Dc(s, TimeProvider.System, new ImmediateAsyncDelay(),
                                               LoopbackTimeouts.PerMessage));

        /// <summary>The same for -20, where the two power modes are two classes on each side.</summary>
        private static async Task<(Evcc20Base Evcc, ulong? StationReading)> RunIso20Async(
            PowerMode mode, EvBattery? battery,
            Func<SigningMeter, Secc20Base> makeSecc, Func<Stream, Evcc20Base> makeEvcc)
        {
            using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var meter = StationMeter();
            var secc  = makeSecc(meter);

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token, mode);
                await secc.RunAsync(seccStream, cts.Token);
            }, cts.Token);

            Evcc20Base evcc;
            using (var stream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                (TlsOptions?) null, cts.Token))
            {
                await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, cts.Token, mode);
                evcc = makeEvcc(stream);
                evcc.Battery = battery;
                await evcc.RunAsync(cts.Token);
            }
            await seccTask;

            return (evcc, meter.Read().Wh);
        }

    }

}
