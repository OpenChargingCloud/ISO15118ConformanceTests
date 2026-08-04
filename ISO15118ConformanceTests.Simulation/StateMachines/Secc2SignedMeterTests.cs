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

using System.Security.Cryptography;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118_2;
using cloud.charging.open.protocols.ISO15118_2.Generated;
using Vanaheimr.V2G.Simulation.Metering;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{
    /// <summary>
    /// A meter-signed reading, all the way through a -2 session and back out of the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Metering.SigningMeterTests"/> checks the meter in isolation. What it cannot check
    /// is the part that decides whether any of this is useful: that the signature survives being
    /// EXI-encoded into <c>MeterInfo.SigMeterReading</c> and decoded again, and that the session id
    /// the SECC signed under is the one the EV sees. Those are the two ways a correct signature
    /// still arrives unverifiable.
    /// </para>
    /// <para>
    /// The field is a standard one and holds 64 bytes — exactly a raw P-256 <c>r‖s</c> pair. What
    /// goes *into* the signature is our own convention, because ISO 15118 does not define one
    /// (<see cref="MeterSigningPayload"/>). These tests pin our end of it; a conforming peer would
    /// have to be told the layout.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Secc2SignedMeterTests
    {
        private static V2G_Message Msg(BodyBaseType body) =>
            new(new MessageHeaderType(new byte[8], Notification: null, Signature: null), new BodyType(body));

        private static SigningMeter Meter(string id = "VAN*M1") =>
            new(id, ECDsa.Create(ECCurve.NamedCurves.nistP256), TimeProvider.System);

        /// <summary>An EIM AC session up to the first ChargingStatus, which is what carries MeterInfo.</summary>
        private static ChargingStatusResType RunToChargingStatus(Secc2 secc)
        {
            secc.Handle(Msg(new SessionSetupReqType(new byte[] { 1, 2, 3, 4, 5, 6 })));
            secc.Handle(Msg(new ServiceDiscoveryReqType(null, null)));
            secc.Handle(Msg(new PaymentServiceSelectionReqType(PaymentOption.ExternalPayment,
                new SelectedServiceListType(new[] { new SelectedServiceType(1, null) }))));
            secc.Handle(Msg(new AuthorizationReqType(null, null)));
            secc.Handle(Msg(new ChargeParameterDiscoveryReqType(null, EnergyTransferMode.AC_three_phase_core,
                new AC_EVChargeParameterType(null, PhysicalValue.Of(22_000, UnitSymbol.Wh),
                    new PhysicalValueType(0, UnitSymbol.V, 400), new PhysicalValueType(0, UnitSymbol.A, 32),
                    new PhysicalValueType(0, UnitSymbol.A, 6)))));
            secc.Handle(Msg(new PowerDeliveryReqType(ChargeProgress.Start, SAScheduleTupleID: 1, null, null)));
            return (ChargingStatusResType) secc.Handle(Msg(new ChargingStatusReqType())).Body.BodyElement!;
        }

        /// <summary>
        /// Without a meter installed, an EIM session carries no MeterInfo at all — unchanged from
        /// before this feature, which is what makes the feature additive.
        /// </summary>
        /// <remarks>
        /// Asserted as "no MeterInfo" rather than "no signature": the first version of this test
        /// checked `MeterInfo?.SigMeterReading is null` and passed **vacuously**, because MeterInfo
        /// itself was null. A test that cannot tell "unsigned" from "absent" would have gone on
        /// passing after the field stopped being sent at all.
        /// </remarks>
        [Test]
        public void WithoutAMeterAnEimSessionCarriesNoMeterInfo()
        {
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);

            Assert.That(RunToChargingStatus(secc).MeterInfo, Is.Null);
        }

        /// <summary>
        /// And with one installed it does — a station that measures reports what it measured, rather
        /// than only when it wants a receipt signed back (<c>docs/CONCEPT.md</c> §4.3).
        /// </summary>
        [Test]
        public void AnInstalledMeterIsReportedEvenWithoutAReceipt()
        {
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System)
                       { InstalledMeter = Meter() };

            var info = RunToChargingStatus(secc);

            Assert.Multiple(() =>
            {
                Assert.That(info.MeterInfo, Is.Not.Null);
                Assert.That(info.ReceiptRequired, Is.Null, "an EIM session still asks for no receipt");
            });
        }

        [Test]
        public void WithAMeterTheReadingCarriesASixtyFourByteSignature()
        {
            var meter = Meter();
            meter.Add(4_200);
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System)
                       { InstalledMeter = meter };

            var meterInfo = RunToChargingStatus(secc).MeterInfo!;

            Assert.Multiple(() =>
            {
                Assert.That(meterInfo.MeterID, Is.EqualTo("VAN*M1"));
                Assert.That(meterInfo.MeterReading, Is.EqualTo(4_200));
                Assert.That(meterInfo.SigMeterReading, Has.Length.EqualTo(64));
            });
        }

        /// <summary>
        /// The one that matters: the EV can verify what it received, using only the meter's public
        /// key and the values on the wire.
        /// </summary>
        [Test]
        public void TheVehicleCanVerifyWhatItReceived()
        {
            var meter = Meter();
            meter.Add(4_200);
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System)
                       { InstalledMeter = meter };

            var info = RunToChargingStatus(secc).MeterInfo!;

            Assert.That(SigningMeter.Verify(meter.PublicKey, protocol: 2, secc.SessionId,
                                            info.MeterID, info.MeterReading!.Value, info.TMeter,
                                            info.SigMeterReading!),
                        Is.True);
        }

        /// <summary>
        /// The signature has to survive the codec. A value that verifies in memory and not after a
        /// round trip is the failure this whole layer exists to avoid, and base64Binary in EXI is
        /// exactly where a stray copy or a length mistake would show up.
        /// </summary>
        [Test]
        public void TheSignatureSurvivesEncodingAndDecoding()
        {
            var meter = Meter();
            meter.Add(9_001);
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System)
                       { InstalledMeter = meter };

            // Drive a real session, then put the whole response through the codec and back.
            secc.Handle(Msg(new SessionSetupReqType(new byte[] { 1, 2, 3, 4, 5, 6 })));
            secc.Handle(Msg(new ServiceDiscoveryReqType(null, null)));
            secc.Handle(Msg(new PaymentServiceSelectionReqType(PaymentOption.ExternalPayment,
                new SelectedServiceListType(new[] { new SelectedServiceType(1, null) }))));
            secc.Handle(Msg(new AuthorizationReqType(null, null)));
            secc.Handle(Msg(new ChargeParameterDiscoveryReqType(null, EnergyTransferMode.AC_three_phase_core,
                new AC_EVChargeParameterType(null, PhysicalValue.Of(22_000, UnitSymbol.Wh),
                    new PhysicalValueType(0, UnitSymbol.V, 400), new PhysicalValueType(0, UnitSymbol.A, 32),
                    new PhysicalValueType(0, UnitSymbol.A, 6)))));
            secc.Handle(Msg(new PowerDeliveryReqType(ChargeProgress.Start, SAScheduleTupleID: 1, null, null)));
            var response = secc.Handle(Msg(new ChargingStatusReqType()));

            var buffer = new byte[4096];
            Assert.That(response.TryEncode(buffer, out var n), Is.True, "the response did not encode");
            var decoded = (V2G_Message) Iso2Codec.DecodeAny(buffer.AsSpan(0, n), out _);

            var sent     = ((ChargingStatusResType) response.Body.BodyElement!).MeterInfo!;
            var received = ((ChargingStatusResType) decoded.Body.BodyElement!).MeterInfo!;

            Assert.Multiple(() =>
            {
                Assert.That(received.SigMeterReading, Is.EqualTo(sent.SigMeterReading));
                Assert.That(received.SigMeterReading, Has.Length.EqualTo(64));
                // And it still verifies against the values that came off the wire, not the ones we
                // happen to still hold in memory.
                Assert.That(SigningMeter.Verify(meter.PublicKey, protocol: 2, secc.SessionId,
                                                received.MeterID, received.MeterReading!.Value,
                                                received.TMeter, received.SigMeterReading!),
                            Is.True);
            });
        }

        /// <summary>
        /// Tampering with the reading in flight is caught. §6.3 asks this of the SECC; this is the
        /// same question from the vehicle's side, and it is the point of signing at all.
        /// </summary>
        [Test]
        public void AReadingTamperedWithInFlightDoesNotVerify()
        {
            var meter = Meter();
            meter.Add(4_200);
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System)
                       { InstalledMeter = meter };

            var info = RunToChargingStatus(secc).MeterInfo!;

            // A CPO shaving a few Wh off the reading between meter and vehicle.
            Assert.That(SigningMeter.Verify(meter.PublicKey, protocol: 2, secc.SessionId,
                                            info.MeterID, info.MeterReading!.Value - 100, info.TMeter,
                                            info.SigMeterReading!),
                        Is.False);
        }

        /// <summary>
        /// A reading signed in another session does not verify in this one — the session binding,
        /// checked where it actually matters, with two real sessions rather than two byte arrays.
        /// </summary>
        [Test]
        public void AReadingFromAnotherSessionDoesNotVerifyHere()
        {
            var meter = Meter();
            meter.Add(4_200);

            var a = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System)
                    { InstalledMeter = meter };
            var b = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System)
                    { InstalledMeter = meter };

            var fromA = RunToChargingStatus(a).MeterInfo!;
            RunToChargingStatus(b);

            Assume.That(a.SessionId, Is.Not.EqualTo(b.SessionId), "the two sessions must differ");

            Assert.That(SigningMeter.Verify(meter.PublicKey, protocol: 2, b.SessionId,
                                            fromA.MeterID, fromA.MeterReading!.Value, fromA.TMeter,
                                            fromA.SigMeterReading!),
                        Is.False);
        }
    }
}
