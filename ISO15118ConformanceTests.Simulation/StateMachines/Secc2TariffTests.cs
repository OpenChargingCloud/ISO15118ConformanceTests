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
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{
    /// <summary>
    /// The -2 smart-charging SECC path, direct (no socket): with a <c>TariffSignKey</c> the SASchedule
    /// offer becomes two tuples whose SalesTariffs are digitally signed into ONE response-header signature
    /// (§7.9.2.5, one reference per tariff), and the <c>PowerDeliveryReq(Start)</c> is validated — unknown
    /// tuple id → <c>FAILED_TariffSelectionInvalid</c>, ChargingProfile above PMax →
    /// <c>FAILED_ChargingProfileInvalid</c> ([V2G2-761]), both without advancing the phase.
    /// The honest validation ledger (live 2026-07-22, docs/interop-runs/2026-07-22-tariff): a Josev EVCC
    /// consumed our signed two-tuple offer, chose the cheaper tuple, and sent a PMax-shaped profile our
    /// validation accepted; and our EVCC live-verified a REAL MO-Sub-CA2-signed Josev SalesTariff. What
    /// has NO external checker is our combined-grammar signing form itself — Josev's EVCC carries tariff
    /// verification only as a code TODO, so these tests are its only verifier.
    /// </summary>
    [TestFixture]
    public class Secc2TariffTests
    {
        private static V2G_Message Msg(BodyBaseType body) =>
            new(new MessageHeaderType(new byte[8], Notification: null, Signature: null), new BodyType(body));

        /// <summary>EIM session up to (and including) ChargeParameterDiscovery; returns the full response
        /// message — the tariff signature sits in its header.</summary>
        private static V2G_Message RunToChargeParams(Secc2 secc)
        {
            secc.Handle(Msg(new SessionSetupReqType(new byte[] { 1, 2, 3, 4, 5, 6 })));
            secc.Handle(Msg(new ServiceDiscoveryReqType(null, null)));
            secc.Handle(Msg(new PaymentServiceSelectionReqType(PaymentOption.ExternalPayment,
                new SelectedServiceListType(new[] { new SelectedServiceType(1, null) }))));
            secc.Handle(Msg(new AuthorizationReqType(null, null)));
            return secc.Handle(Msg(new ChargeParameterDiscoveryReqType(null, EnergyTransferMode.AC_three_phase_core,
                new AC_EVChargeParameterType(null, PhysicalValue.Of(22_000, UnitSymbol.Wh),
                    new PhysicalValueType(0, UnitSymbol.V, 400), new PhysicalValueType(0, UnitSymbol.A, 32),
                    new PhysicalValueType(0, UnitSymbol.A, 6)))));
        }

        [Test]
        public void TariffOffer_TwoTuples_OneHeaderSignature_DigestsAndEcdsaVerify()
        {
            using var tariffKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System) { TariffSignKey = tariffKey };

            var reply = RunToChargeParams(secc);
            var cpd = (ChargeParameterDiscoveryResType)reply.Body.BodyElement!;
            var offer = (SAScheduleListType)cpd.SASchedules!;

            Assert.That(offer.SAScheduleTuple, Has.Count.EqualTo(2));
            Assert.That(reply.Header.Signature, Is.Not.Null, "the tariff signature rides in the response header");

            var sig = reply.Header.Signature!;
            Assert.That(sig.SignedInfo.Reference, Has.Count.EqualTo(2), "one reference per SalesTariff");

            Assert.Multiple(() =>
            {
                // Each reference digest must match its tariff's re-encoded EXI fragment …
                foreach (var tuple in offer.SAScheduleTuple)
                {
                    var tariff = tuple.SalesTariff!;
                    var reference = sig.SignedInfo.Reference.Single(r => r.URI == "#" + tariff.Id);
                    var buf = new byte[2048];
                    Assert.That(Iso2Codec.EncodeFragment_SalesTariff(tariff, buf, out int n), Is.True);
                    Assert.That(V2GSignature.VerifyReference(reference, buf.AsSpan(0, n)), Is.True,
                        $"digest of {tariff.Id} must match");
                }
                // … and the SignedInfo signature must verify under the combined -2 grammar.
                Assert.That(V2GSignature.Verify(sig.SignedInfo, sig.SignatureValue.Value, tariffKey), Is.True);
            });
        }

        [Test]
        public void PowerDelivery_UnknownTupleId_FailsTariffSelection_ThenValidChoicePasses()
        {
            using var tariffKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System) { TariffSignKey = tariffKey };
            RunToChargeParams(secc);

            var bad = (PowerDeliveryResType)secc.Handle(
                Msg(new PowerDeliveryReqType(ChargeProgress.Start, SAScheduleTupleID: 9, null, null))).Body.BodyElement!;
            Assert.That(bad.ResponseCode, Is.EqualTo(ResponseCode.FAILED_TariffSelectionInvalid));
            Assert.That(secc.ChargingProfileCheck!.TupleIdOk, Is.False);

            // The phase did not advance — a corrected choice still works.
            var good = (PowerDeliveryResType)secc.Handle(
                Msg(new PowerDeliveryReqType(ChargeProgress.Start, SAScheduleTupleID: 2, null, null))).Body.BodyElement!;
            Assert.That(good.ResponseCode, Is.EqualTo(ResponseCode.OK));
            Assert.That(secc.ChargingProfileCheck!.TupleIdOk, Is.True);
        }

        [Test]
        public void PowerDelivery_ProfileAbovePMax_FailsChargingProfile_ThenShapedProfilePasses()
        {
            using var tariffKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System) { TariffSignKey = tariffKey };
            RunToChargeParams(secc);

            // Tuple 2 caps the first 30 min at 7.4 kW — 11 kW at t=0 violates it.
            var greedy = new ChargingProfileType(new[]
            {
                new ProfileEntryType(0, PhysicalValue.Of(11_000, UnitSymbol.W), null),
            });
            var bad = (PowerDeliveryResType)secc.Handle(
                Msg(new PowerDeliveryReqType(ChargeProgress.Start, 2, greedy, null))).Body.BodyElement!;
            Assert.That(bad.ResponseCode, Is.EqualTo(ResponseCode.FAILED_ChargingProfileInvalid));
            Assert.That(secc.ChargingProfileCheck!.WithinPMax, Is.False);

            // A profile shaped to the PMax steps (7.4 kW, then 22 kW) is accepted.
            var shaped = new ChargingProfileType(new[]
            {
                new ProfileEntryType(0,    PhysicalValue.Of(7_400,  UnitSymbol.W), null),
                new ProfileEntryType(1800, PhysicalValue.Of(22_000, UnitSymbol.W), null),
            });
            var good = (PowerDeliveryResType)secc.Handle(
                Msg(new PowerDeliveryReqType(ChargeProgress.Start, 2, shaped, null))).Body.BodyElement!;
            Assert.That(good.ResponseCode, Is.EqualTo(ResponseCode.OK));
            Assert.That(secc.ChargingProfileCheck!.WithinPMax, Is.True);
        }

        [Test]
        public void NoTariffKey_KeepsPlainSingleTupleOffer_Unsigned()
        {
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
            var reply = RunToChargeParams(secc);
            var offer = (SAScheduleListType)((ChargeParameterDiscoveryResType)reply.Body.BodyElement!).SASchedules!;

            Assert.Multiple(() =>
            {
                Assert.That(offer.SAScheduleTuple, Has.Count.EqualTo(1));
                Assert.That(offer.SAScheduleTuple[0].SalesTariff, Is.Null);
                Assert.That(reply.Header.Signature, Is.Null);
            });
        }
    }
}
