using System.Security.Cryptography;

using NUnit.Framework;

using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;
using Vanaheimr.V2G.Simulation.Metering;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;

using Ac20 = Vanaheimr.V2G.Iso15118_20.AC.Generated;
using Dc20 = Vanaheimr.V2G.Iso15118_20.DC.Generated;

namespace Vanaheimr.V2G.Simulation.Tests.StateMachines
{
    /// <summary>
    /// A meter-signed reading through a whole -20 session, AC and DC.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The -20 counterpart of <see cref="Secc2SignedMeterTests"/>, and it closes a gap this feature
    /// shipped with: the two charge-loop call sites were checked by <em>reading</em> the code, because
    /// no test drove a -20 session that far. That is exactly the kind of claim that is true when
    /// written and quietly false three commits later.
    /// </para>
    /// <para>
    /// -20 differs from -2 in two ways that are worth testing rather than asserting. The field is
    /// per-message-set — <c>Ac20.MeterInfoType</c> and <c>Dc20.MeterInfoType</c> are distinct CLR
    /// types built at two independent call sites — so AC and DC are separately checked; wiring one
    /// and forgetting the other is the obvious failure and would otherwise pass. And the payload
    /// carries protocol byte <b>20</b>, so a -20 reading must not verify as a -2 one.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Secc20SignedMeterTests
    {
        private static SigningMeter Meter(string id = "VAN*M1") =>
            new(id, ECDsa.Create(ECCurve.NamedCurves.nistP256), TimeProvider.System);

        private static Secc20Ac Ac(SigningMeter? meter = null) =>
            new(TimeSpan.FromSeconds(60), TimeProvider.System) { InstalledMeter = meter };

        private static Secc20Dc Dc(SigningMeter? meter = null) =>
            new(TimeSpan.FromSeconds(60), TimeProvider.System) { InstalledMeter = meter };

        // ── The driver itself ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Before trusting the driver's charge loop, check it drives a <em>whole</em> session.
        /// </summary>
        /// <remarks>
        /// A driver that stops at the first response a test wants leaves the SECC in a state a real
        /// EV never produces, and the sequence guard never gets asked whether the rest would have
        /// worked. Running to <c>IsDone</c> is what makes "the charge loop was reached legitimately"
        /// mean something. Both control modes, because the SECC answers strictly in kind
        /// ([V2G20-1600]) and a driver that only ever ran Scheduled would not notice.
        /// </remarks>
        [Test]
        public void TheDriverRunsACompleteSessionInBothControlModes(
            [Values(Iso20ControlMode.Scheduled, Iso20ControlMode.Dynamic)] Iso20ControlMode mode)
        {
            var ac = Ac();
            var dc = Dc();

            var acDriver = new Ac20SessionDriver(ac, mode: mode);
            acDriver.ToChargeLoop();
            acDriver.ChargeLoop();
            var acStop = acDriver.Stop();

            var dcDriver = new Dc20SessionDriver(dc, mode: mode);
            dcDriver.ToChargeLoop();
            dcDriver.ChargeLoop();
            var dcStop = dcDriver.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(acStop.ResponseCode, Is.EqualTo(ResponseCode.OK));
                Assert.That(ac.IsDone, Is.True, "the AC session did not reach its end");
                Assert.That(dcStop.ResponseCode, Is.EqualTo(ResponseCode.OK));
                Assert.That(dc.IsDone, Is.True, "the DC session did not reach its end");
            });
        }

        // ── Absent, present ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Without a meter the charge loop carries no MeterInfo at all — which is what every station
        /// in the field does, and is what makes the feature additive.
        /// </summary>
        /// <remarks>
        /// Asserted as "no MeterInfo", not "no signature". Its -2 counterpart originally checked
        /// <c>MeterInfo?.MeterSignature is null</c> and passed <b>vacuously</b>, because MeterInfo
        /// itself was null: it could not tell "unsigned" from "absent", and would have gone on
        /// passing after the field stopped being sent.
        /// </remarks>
        [Test]
        public void WithoutAMeterNeitherChargeLoopCarriesMeterInfo()
        {
            var acLoop = new Ac20SessionDriver(Ac()).ToChargeLoop().ChargeLoop();
            var dcLoop = new Dc20SessionDriver(Dc()).ToChargeLoop().ChargeLoop();

            Assert.Multiple(() =>
            {
                Assert.That(acLoop.MeterInfo, Is.Null);
                Assert.That(dcLoop.MeterInfo, Is.Null);
            });
        }

        /// <summary>
        /// And with one installed, both loops carry it — and it counts what the loop delivered.
        /// </summary>
        /// <remarks>
        /// Two things at once, because until 2026-08-03 only the first was true. Two call sites and
        /// two message sets catch wiring AC and forgetting DC. The <em>reading</em> catches the newer
        /// and quieter failure: a meter that is fitted, signs correctly, and never advances. That
        /// version passed every signature check in this file while reporting a constant, and it only
        /// became visible once the vehicle grew a counter to compare against.
        /// <para>
        /// AC announces 22 kW and DC 400 V x 120 A, over one <c>ChargeLoopSample.Period</c> — so a
        /// minute of each is 367 Wh and 800 Wh. The AC figure is rounded up from 366.67, which is
        /// worth having in a test: it is the only place the rounding rule is visible.
        /// </para>
        /// </remarks>
        [Test]
        public void WithAMeterBothChargeLoopsCarryASignedReadingOfWhatTheyDelivered()
        {
            var acLoop = new Ac20SessionDriver(Ac(Meter())).ToChargeLoop().ChargeLoop();
            var dcLoop = new Dc20SessionDriver(Dc(Meter())).ToChargeLoop().ChargeLoop();

            Assert.Multiple(() =>
            {
                Assert.That(acLoop.MeterInfo!.MeterID, Is.EqualTo("VAN*M1"));
                Assert.That(acLoop.MeterInfo!.ChargedEnergyReadingWh, Is.EqualTo(367),
                            "22 kW for one sample period, rounded from 366.67");
                Assert.That(acLoop.MeterInfo!.MeterSignature, Has.Length.EqualTo(64));

                Assert.That(dcLoop.MeterInfo!.MeterID, Is.EqualTo("VAN*M1"));
                Assert.That(dcLoop.MeterInfo!.ChargedEnergyReadingWh, Is.EqualTo(800),
                            "48 kW for one sample period");
                Assert.That(dcLoop.MeterInfo!.MeterSignature, Has.Length.EqualTo(64));
            });
        }

        // ── What the vehicle can do with it ─────────────────────────────────────────────────────

        /// <summary>
        /// The one that matters: the EV verifies what it received, from the meter's public key and
        /// the values on the wire alone.
        /// </summary>
        [Test]
        public void TheVehicleCanVerifyWhatItReceivedOverAc()
        {
            var meter = Meter();
            meter.Add(4_200);
            var secc = Ac(meter);

            var info = new Ac20SessionDriver(secc).ToChargeLoop().ChargeLoop().MeterInfo!;

            Assert.That(SigningMeter.Verify(meter.PublicKey, protocol: 20, secc.SessionId,
                                            info.MeterID, info.ChargedEnergyReadingWh,
                                            (long?) info.MeterTimestamp, info.MeterSignature!),
                        Is.True);
        }

        /// <summary>The same over DC — a separate <c>MeterInfoType</c>, built at a separate call site.</summary>
        [Test]
        public void TheVehicleCanVerifyWhatItReceivedOverDc()
        {
            var meter = Meter();
            meter.Add(4_200);
            var secc = Dc(meter);

            var info = new Dc20SessionDriver(secc).ToChargeLoop().ChargeLoop().MeterInfo!;

            Assert.That(SigningMeter.Verify(meter.PublicKey, protocol: 20, secc.SessionId,
                                            info.MeterID, info.ChargedEnergyReadingWh,
                                            (long?) info.MeterTimestamp, info.MeterSignature!),
                        Is.True);
        }

        /// <summary>
        /// A -20 reading is not a -2 reading. The protocol byte in the payload is the only thing
        /// stopping a signature being carried between the two protocols, and it is invisible on the
        /// wire — so it is checkable only from here.
        /// </summary>
        [Test]
        public void AMinus20ReadingDoesNotVerifyAsAMinus2One()
        {
            var meter = Meter();
            meter.Add(4_200);
            var secc = Ac(meter);

            var info = new Ac20SessionDriver(secc).ToChargeLoop().ChargeLoop().MeterInfo!;

            Assert.That(SigningMeter.Verify(meter.PublicKey, protocol: 2, secc.SessionId,
                                            info.MeterID, info.ChargedEnergyReadingWh,
                                            (long?) info.MeterTimestamp, info.MeterSignature!),
                        Is.False, "the same bytes must not verify under the other protocol");
        }

        /// <summary>
        /// Tampering with the reading in flight is caught — the point of signing it at all.
        /// </summary>
        [Test]
        public void AReadingTamperedWithInFlightDoesNotVerify()
        {
            var meter = Meter();
            meter.Add(4_200);
            var secc = Ac(meter);

            var info = new Ac20SessionDriver(secc).ToChargeLoop().ChargeLoop().MeterInfo!;

            // A CPO shaving a few Wh off the reading between meter and vehicle.
            Assert.That(SigningMeter.Verify(meter.PublicKey, protocol: 20, secc.SessionId,
                                            info.MeterID, info.ChargedEnergyReadingWh - 100,
                                            (long?) info.MeterTimestamp, info.MeterSignature!),
                        Is.False);
        }

        /// <summary>
        /// A reading signed in another session does not verify in this one — the session binding,
        /// checked with two real sessions rather than two byte arrays.
        /// </summary>
        [Test]
        public void AReadingFromAnotherSessionDoesNotVerifyHere()
        {
            var meter = Meter();
            meter.Add(4_200);

            var a = Ac(meter);
            var b = Ac(meter);

            var fromA = new Ac20SessionDriver(a).ToChargeLoop().ChargeLoop().MeterInfo!;
            new Dc20SessionDriver(Dc(meter)).ToChargeLoop().ChargeLoop();
            new Ac20SessionDriver(b).ToChargeLoop().ChargeLoop();

            Assume.That(a.SessionId, Is.Not.EqualTo(b.SessionId), "the two sessions must differ");

            Assert.That(SigningMeter.Verify(meter.PublicKey, protocol: 20, b.SessionId,
                                            fromA.MeterID, fromA.ChargedEnergyReadingWh,
                                            (long?) fromA.MeterTimestamp, fromA.MeterSignature!),
                        Is.False);
        }

        // ── Through the codec ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// The signature has to survive the codec. A value that verifies in memory and not after a
        /// round trip is the failure this whole layer exists to avoid, and <c>base64Binary</c> in EXI
        /// is exactly where a stray copy or a length mistake would show up.
        /// </summary>
        [Test]
        public void TheSignatureSurvivesTheAcCodec()
        {
            var meter = Meter();
            meter.Add(9_001);
            var secc = Ac(meter);

            var sent = new Ac20SessionDriver(secc).ToChargeLoop().ChargeLoop();

            var buffer = new byte[4096];
            Assert.That(Ac20.AcCodec.TryEncode(sent, buffer, out var n), Is.True, "the response did not encode");
            var received = (Ac20.AC_ChargeLoopRes) Ac20.AcCodec.DecodeAny(buffer.AsSpan(0, n), out _);

            Assert.Multiple(() =>
            {
                Assert.That(received.MeterInfo!.MeterSignature, Is.EqualTo(sent.MeterInfo!.MeterSignature));
                Assert.That(received.MeterInfo!.MeterSignature, Has.Length.EqualTo(64));
                // And it still verifies against the values that came off the wire, not the ones we
                // happen to still hold in memory.
                Assert.That(SigningMeter.Verify(meter.PublicKey, protocol: 20, secc.SessionId,
                                                received.MeterInfo!.MeterID,
                                                received.MeterInfo!.ChargedEnergyReadingWh,
                                                (long?) received.MeterInfo!.MeterTimestamp,
                                                received.MeterInfo!.MeterSignature!),
                            Is.True);
            });
        }

        /// <summary>And over DC, whose <c>MeterInfoType</c> is a different type through a different codec.</summary>
        [Test]
        public void TheSignatureSurvivesTheDcCodec()
        {
            var meter = Meter();
            meter.Add(9_001);
            var secc = Dc(meter);

            var sent = new Dc20SessionDriver(secc).ToChargeLoop().ChargeLoop();

            var buffer = new byte[4096];
            Assert.That(Dc20.DcCodec.TryEncode(sent, buffer, out var n), Is.True, "the response did not encode");
            var received = (Dc20.DC_ChargeLoopRes) Dc20.DcCodec.DecodeAny(buffer.AsSpan(0, n), out _);

            Assert.Multiple(() =>
            {
                Assert.That(received.MeterInfo!.MeterSignature, Is.EqualTo(sent.MeterInfo!.MeterSignature));
                Assert.That(SigningMeter.Verify(meter.PublicKey, protocol: 20, secc.SessionId,
                                                received.MeterInfo!.MeterID,
                                                received.MeterInfo!.ChargedEnergyReadingWh,
                                                (long?) received.MeterInfo!.MeterTimestamp,
                                                received.MeterInfo!.MeterSignature!),
                            Is.True);
            });
        }
    }
}
