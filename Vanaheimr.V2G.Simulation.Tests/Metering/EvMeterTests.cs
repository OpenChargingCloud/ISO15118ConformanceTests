using System.Net;
using System.Security.Cryptography;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Metering;
using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.Metering;

/// <summary>
/// The vehicle's own energy counter, and the thing it exists for: a number to hold a station's
/// signed reading up against.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/CONCEPT.md</c> §4.2 asks for a local model *"so the signature covers the EV's own view,
/// not only an echo of the station's"*, and §4.3 makes it the first of three legs. The point of the
/// loopback tests below is that the two counters are driven by two state machines that never look at
/// each other's totals — one counts what the EV declared at its inlet, the other what the station
/// announced at its outlet — and they still have to land on the same watt-hour.
/// </para>
/// <para>
/// What agreement here does <b>not</b> mean: anything about real meters. It is two implementations
/// of one arithmetic over one declared sample period, which catches a wrong field, a wrong unit or a
/// wrong rounding rule, and nothing else. That limit is the reason this file says it out loud rather
/// than leaving a green tick to imply more.
/// </para>
/// </remarks>
[TestFixture]
public class EvMeterTests
{

    private static SigningMeter StationMeter() =>
        new("VAN*M*4711", ECDsa.Create(ECCurve.NamedCurves.nistP256), TimeProvider.System);


    // ── the model on its own ──────────────────────────────────────────────

    [Test]
    public void ASampleIsPowerTimesTheDeclaredPeriod()
    {
        var meter = new EvMeter(TimeSpan.FromHours(1));

        meter.Sample(1_000);

        Assert.Multiple(() =>
        {
            Assert.That(meter.EnergyWh, Is.EqualTo(1_000), "a kilowatt for an hour is a kilowatt-hour");
            Assert.That(meter.Samples,  Is.EqualTo(1));
            Assert.That(meter.SamplePeriod, Is.EqualTo(TimeSpan.FromHours(1)));
        });
    }

    [Test]
    public void VoltsTimesAmperesIsTheSameSample()
    {
        var byPower = new EvMeter();
        var byVolts = new EvMeter();

        byPower.Sample(400d * 120d);
        byVolts.Sample(400, 120);

        Assert.That(byVolts.Energy, Is.EqualTo(byPower.Energy));
    }

    /// <summary>
    /// Rounding happens per sample, matching a register rather than a mathematician.
    /// </summary>
    /// <remarks>
    /// 22 kW for a minute is 366.67 Wh. Integrating precisely and rounding once gives 1100; a
    /// register that can only hold whole watt-hours takes on 367 three times and reads 1101. The
    /// second is worse arithmetic and the right answer, because <c>MeterReading</c> is exactly such a
    /// register — and this test exists because the first version got it the other way round and an AC
    /// session came out one watt-hour apart from the station it was supposed to agree with.
    /// </remarks>
    [Test]
    public void RoundingHappensPerSample()
    {
        var meter = new EvMeter();

        for (var i = 0; i < 3; i++)
            meter.Sample(22_000);

        Assert.That(meter.EnergyWh, Is.EqualTo(1_101));
    }

    /// <summary>
    /// Export subtracts. Nothing here drives a bidirectional session yet, and a counter that clamped
    /// at zero would report a future V2H session as pure consumption — a decision worth making
    /// deliberately rather than by omission.
    /// </summary>
    [Test]
    public void ExportedEnergySubtracts()
    {
        var meter = new EvMeter(TimeSpan.FromHours(1));

        meter.Sample(1_000);
        meter.Sample(-400);

        Assert.That(meter.EnergyWh, Is.EqualTo(600));
    }

    [Test]
    public void ASamplePeriodOfZeroIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvMeter(TimeSpan.Zero));
    }


    // ── the two counters, over a real session ─────────────────────────────

    /// <summary>
    /// A whole -2 DC session over a socket: the vehicle's counter and the station's signed reading
    /// agree, having been arrived at independently.
    /// </summary>
    /// <remarks>
    /// DC first because it is the case where the two sides derive the figure differently — the EV
    /// multiplies the voltage and current it asked for, the station the ones it announces — so the
    /// agreement is not two copies of one expression.
    /// </remarks>
    [Test]
    public async Task OverADcSessionTheVehicleAndTheStationAgree()
    {
        var (evcc, station) = await RunIso2Async(PowerMode.Dc);

        Assert.Multiple(() =>
        {
            Assert.That(evcc.Meter.Samples, Is.EqualTo(3), "three charge-loop iterations, three samples");
            Assert.That(evcc.Meter.EnergyWh, Is.EqualTo(2_400), "48 kW for three sample periods");
            Assert.That(station, Is.EqualTo(evcc.Meter.EnergyWh),
                        "the station's signed reading and the vehicle's own counter disagree, and "
                      + "they are counting the same three iterations of the same charge loop");
        });
    }

    /// <summary>
    /// And over AC, where neither protocol direction carries a power at all: both sides fall back to
    /// the ChargingProfile the EV committed to and the station accepted, which is the only figure
    /// they share.
    /// </summary>
    [Test]
    public async Task OverAnAcSessionTheVehicleAndTheStationAgree()
    {
        var (evcc, station) = await RunIso2Async(PowerMode.Ac);

        Assert.Multiple(() =>
        {
            Assert.That(evcc.Meter.Samples, Is.EqualTo(3));
            Assert.That(evcc.Meter.EnergyWh, Is.GreaterThan(0),
                        "an AC session counted nothing — the committed profile power never reached the meter");
            Assert.That(station, Is.EqualTo(evcc.Meter.EnergyWh));
        });
    }

    /// <summary>
    /// A station with no meter fitted leaves the vehicle counting on its own.
    /// </summary>
    /// <remarks>
    /// The ordinary case in the field, and the one that decides whether this feature is additive: the
    /// EV's counter must not depend on the station having one. If it did, the app would show nothing
    /// at exactly the stations where an independent number is worth the most.
    /// </remarks>
    [Test]
    public async Task WithoutAStationMeterTheVehicleStillCounts()
    {
        var (evcc, station) = await RunIso2Async(PowerMode.Dc, withStationMeter: false);

        Assert.Multiple(() =>
        {
            Assert.That(evcc.Meter.EnergyWh, Is.EqualTo(2_400));
            Assert.That(station, Is.Null, "a station with no meter reported a reading");
        });
    }

    /// <summary>The same over -20 DC, where the vehicle's figure is half its own and half the
    /// station's: it measures its inlet voltage, the station reports the current.</summary>
    [Test]
    public async Task OverAnIso20DcSessionTheVehicleAndTheStationAgree()
    {
        using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

        var meter = StationMeter();
        var secc  = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System) { InstalledMeter = meter };

        var seccTask = Task.Run(async () =>
        {
            using var seccStream = await listener.AcceptAsync(cts.Token);
            await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token, PowerMode.Dc);
            await secc.RunAsync(seccStream, cts.Token);
        }, cts.Token);

        Evcc20Dc evcc;
        using (var stream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                            (TlsOptions?) null, cts.Token))
        {
            await SapHandshake.RunEvccSideAsync(stream, ProtocolVariant.Iso15118_20, cts.Token, PowerMode.Dc);
            evcc = new Evcc20Dc(stream, TimeProvider.System, new ImmediateAsyncDelay(),
                                LoopbackTimeouts.PerMessage);
            await evcc.RunAsync(cts.Token);
        }
        await seccTask;

        Assert.Multiple(() =>
        {
            Assert.That(evcc.Meter.EnergyWh, Is.GreaterThan(0));
            Assert.That(meter.Read().Wh, Is.EqualTo(evcc.Meter.EnergyWh));
        });
    }


    /// <summary>Runs one -2 session over a loopback socket and returns both counters.</summary>
    private static async Task<(Evcc2 Evcc, ulong? StationReading)> RunIso2Async(
        PowerMode mode, bool withStationMeter = true)
    {

        using var cts      = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

        var meter = withStationMeter ? StationMeter() : null;
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
                             LoopbackTimeouts.PerMessage);
            await evcc.RunAsync(cts.Token);
        }
        await seccTask;

        return (evcc, meter?.Read().Wh);

    }

}
