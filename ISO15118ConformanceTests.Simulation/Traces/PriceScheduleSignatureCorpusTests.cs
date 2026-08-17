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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118_20.CommonMessages;
using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;

using CommonHeader = cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated.MessageHeaderType;

namespace ISO15118ConformanceTests.Simulation.Traces;

/// <summary>
/// A corpus of signed <c>AbsolutePriceSchedule</c> offers and the verdict an EVCC should reach.
/// </summary>
/// <remarks>
/// <para>
/// The -20 counterpart of <c>TariffSignatureCorpusTests</c>, and it exists for the same reason: the
/// verdict never reaches the wire. The EV checks the price schedule and tells the station nothing, so a
/// recorded session can pin the bytes and never the conclusion — and for a verifier the conclusion is
/// the whole question, because a broken one answers "fine" in exactly the voice of a working one.
/// </para>
/// <para>
/// <b>Two control modes, one check, and that is a case of its own.</b> Scheduled mode hangs the price
/// schedule off a schedule tuple; Dynamic mode carries one directly on the control mode. A verifier
/// that looks in only one place reports "unsigned" for half the sessions in the field, and an unsigned
/// offer is exactly what an honest station that does not sign looks like — so the mistake is invisible
/// from the outside. <c>signed-scheduled</c> and <c>signed-dynamic</c> are the same schedule, the same
/// key and the same verdict, differing only in where it hangs.
/// </para>
/// <para>
/// <b>And one case that is deliberately not a verdict.</b> Most stations never sign, and Josev's SECC
/// emits no <c>AbsolutePriceSchedule</c> at all. <c>no-price-schedule</c> expects <c>null</c> rather
/// than three falses: "there was nothing to check" and "I checked and found nothing good" are
/// different answers, and a screen that cannot tell them apart accuses every ordinary station.
/// </para>
/// <para>
/// P-521/SHA-512 throughout, which is -20's signature suite. The key is fixed for reproducibility and
/// is test material, never a real eMSP key.
/// </para>
/// </remarks>
[TestFixture]
public class PriceScheduleSignatureCorpusTests
{

    private const string FileName = "PriceSchedule.signature.vectors.json";

    /// <summary>The eMSP's key, as far as this corpus is concerned. P-521, because -20 signs with it.</summary>
    private const string TariffKeyD =
        "01d4f2a3c95b8e70612fa4c8d35b90e7418fa2c6d095b74e128fa03d76c5b192e8437ca6"
      + "05d29b4718fe30c6a95d472b8103fae62d59b0748125fac396d0b7e5c418";

    /// <summary>A second, unrelated key — the one the <c>wrong-key</c> case verifies against.</summary>
    private const string OtherKeyD =
        "00b7e5c418fa2c6d095b74e128fa03d76c5b192e8437ca605d29b4718fe30c6a95d472b8"
      + "103fae62d59b0748125fac396d0b7e5c41d4f2a3c95b8e70612fa4c8d35b";

    private static ECDsa KeyFrom(string d)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP521);
        key.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP521,
            D     = Convert.FromHexString(d),
        });
        return key;
    }

    private static PriceRuleType PriceRule(short feeCents, short powerRangeStartKw) =>
        new(EnergyFee: new RationalNumberType(-2, feeCents),
            ParkingFee: null, ParkingFeePeriod: null,
            CarbonDioxideEmission: null, RenewableGenerationPercentage: null,
            PowerRangeStart: new RationalNumberType(3, powerRangeStartKw));

    /// <summary>The station's rich tariff — the same one <c>Secc20Base</c> offers with a tariff key set,
    /// so this corpus and a recorded session describe one offer rather than two.</summary>
    private static AbsolutePriceScheduleType PriceSchedule(string description = "off-peak first") =>
        new(Id: "absolutePriceSchedule1", TimeAnchor: 0, PriceScheduleID: 1,
            PriceScheduleDescription: description,
            Currency: "EUR", Language: "en",
            PriceAlgorithm: "urn:iso:std:iso:15118:-20:PriceAlgorithm:1-Power",
            MinimumCost: null, MaximumCost: null, TaxRules: null,
            PriceRuleStacks: new PriceRuleStackListType(new[]
            {
                new PriceRuleStackType(Duration: 1800, new[]
                {
                    PriceRule(feeCents: 25, powerRangeStartKw: 0),
                    PriceRule(feeCents: 35, powerRangeStartKw: 11),
                }),
                new PriceRuleStackType(Duration: 1800, new[]
                {
                    PriceRule(feeCents: 30, powerRangeStartKw: 0),
                    PriceRule(feeCents: 45, powerRangeStartKw: 11),
                }),
            }),
            OverstayRules: null, AdditionalSelectedServices: null);

    private static PowerScheduleType PowerSchedule() =>
        new(TimeAnchor: 0, AvailableEnergy: null, PowerTolerance: null,
            new PowerScheduleEntryListType(new[]
            {
                new PowerScheduleEntryType(Duration: 3600, Power: new RationalNumberType(0, 100), null, null),
            }));

    /// <summary>The compact price form a station without a tariff key offers: no AbsolutePriceSchedule at
    /// all, so nothing to verify.</summary>
    private static PriceLevelScheduleType PriceLevelSchedule() =>
        new(Id: null, TimeAnchor: 0, PriceScheduleID: 1,
            PriceScheduleDescription: null, NumberOfPriceLevels: 1,
            new PriceLevelScheduleEntryListType(new[]
            {
                new PriceLevelScheduleEntryType(Duration: 3600, PriceLevel: 0),
            }));

    private static SignatureType Sign(AbsolutePriceScheduleType priceSchedule, ECDsa key)
    {
        var buf = new byte[4096];
        if (!CommonMessagesCodec.EncodeFragment_AbsolutePriceSchedule(priceSchedule, buf, out int n))
            throw new InvalidOperationException("AbsolutePriceSchedule fragment encode failed.");

        var signedInfo = V2GSignature.BuildSignedInfo(priceSchedule.Id!,
                             V2GSignature.Digest(buf.AsSpan(0, n)), includeExiTransform: true);
        return V2GSignature.BuildSignature(signedInfo, V2GSignature.Sign(signedInfo, key));
    }

    private static CommonHeader Header(SignatureType? signature) =>
        new(Convert.FromHexString("00a1b2c3d4e5f607"), 1_700_000_000, signature);

    /// <summary>Scheduled mode: the schedule hangs off a tuple's ChargingSchedule.</summary>
    private static ScheduleExchangeRes Scheduled(AbsolutePriceScheduleType? price, SignatureType? signature) =>
        new(Header(signature), ResponseCode.OK, Processing.Finished, GoToPause: false,
            Dynamic_SEResControlMode: null,
            Scheduled_SEResControlMode: new Scheduled_SEResControlModeType(new[]
            {
                new ScheduleTupleType(ScheduleTupleID: 1,
                    ChargingSchedule: new ChargingScheduleType(
                        PowerSchedule(), price, price is null ? PriceLevelSchedule() : null),
                    DischargingSchedule: null),
            }));

    /// <summary>Dynamic mode: no tuples at all, the schedule sits on the control mode.</summary>
    private static ScheduleExchangeRes Dynamic(AbsolutePriceScheduleType price, SignatureType? signature) =>
        new(Header(signature), ResponseCode.OK, Processing.Finished, GoToPause: false,
            Dynamic_SEResControlMode: new Dynamic_SEResControlModeType(
                DepartureTime: null, MinimumSOC: null, TargetSOC: null,
                AbsolutePriceSchedule: price, PriceLevelSchedule: null),
            Scheduled_SEResControlMode: null);

    private static string Encode(ScheduleExchangeRes res)
    {
        var buf = new byte[16384];
        if (!CommonMessagesCodec.TryEncodeAny(res, buf, out int n))
            throw new InvalidOperationException("ScheduleExchangeRes encode failed.");
        return Convert.ToHexString(buf.AsSpan(0, n)).ToLowerInvariant();
    }

    private static object PublicKeyOf(ECDsa key)
    {
        var q = key.ExportParameters(false).Q;
        return new
        {
            x = Convert.ToHexString(q.X!).ToLowerInvariant(),
            y = Convert.ToHexString(q.Y!).ToLowerInvariant(),
        };
    }

    private static object Expected(bool present, bool digest, bool signature) =>
        new { signaturePresent = present, digestOk = digest, signatureOk = signature };

    /// <summary>
    /// Regenerates the corpus. <see cref="ExplicitAttribute"/> because the file is an oracle for two other
    /// languages: it must change when someone means it to, never as a side effect of a run.
    /// </summary>
    [Test, Explicit("Regenerates vectors/PriceSchedule.signature.vectors.json — run deliberately")]
    public void RegenerateTheCorpus()
    {

        using var tariffKey = KeyFrom(TariffKeyD);
        using var otherKey  = KeyFrom(OtherKeyD);

        var schedule = PriceSchedule();
        var signature = Sign(schedule, tariffKey);

        var cases = new List<object>
        {
            new
            {
                name = "signed-scheduled",
                what = "Scheduled mode: the signed AbsolutePriceSchedule hangs off a schedule tuple's "
                     + "ChargingSchedule. The ordinary case, and what our own station offers with a "
                     + "tariff key set.",
                frame = Encode(Scheduled(schedule, signature)),
                verifyKey = PublicKeyOf(tariffKey),
                expected = Expected(true, true, true),
            },
            new
            {
                name = "signed-dynamic",
                what = "Dynamic mode: no schedule tuples exist, so the same schedule sits directly on the "
                     + "control mode. Same key, same signature, same verdict — a verifier that only looks "
                     + "in the Scheduled tuples reports this signed offer as unsigned, which is exactly "
                     + "what an honest unsigning station looks like.",
                frame = Encode(Dynamic(schedule, signature)),
                verifyKey = PublicKeyOf(tariffKey),
                expected = Expected(true, true, true),
            },
            new
            {
                name = "unsigned",
                what = "An AbsolutePriceSchedule with no header signature over it. There is something to "
                     + "check and nothing checking it, which is a different answer from having nothing to "
                     + "check at all — see no-price-schedule.",
                frame = Encode(Scheduled(schedule, signature: null)),
                verifyKey = (object?) null,
                expected = Expected(false, false, false),
            },
            new
            {
                name = "digest-tampered",
                what = "Signed correctly, then the schedule's description was edited. The SignedInfo "
                     + "signature still verifies — it covers the SignedInfo, never the schedule — and only "
                     + "the digest catches it. A verifier that trusts the ECDSA check alone prices the "
                     + "session off a tariff nobody signed.",
                frame = Encode(Scheduled(PriceSchedule("off-peak first (altered)"), signature)),
                verifyKey = PublicKeyOf(tariffKey),
                expected = Expected(true, false, true),
            },
            new
            {
                name = "wrong-key",
                what = "A sound signature verified against an unrelated key. The digest half still holds, "
                     + "because it needs no key at all.",
                frame = Encode(Scheduled(schedule, signature)),
                verifyKey = PublicKeyOf(otherKey),
                expected = Expected(true, true, false),
            },
            new
            {
                name = "no-verify-key",
                what = "Signed schedule, correct digest, but the EV holds no eMSP key. signatureOk false "
                     + "here means \"not established\" rather than \"failed\" — and unlike -2 there is no "
                     + "grammar field to say which, so the distinction lives in this corpus alone.",
                frame = Encode(Scheduled(schedule, signature)),
                verifyKey = (object?) null,
                expected = Expected(true, true, false),
            },
            new
            {
                name = "no-price-schedule",
                what = "The compact PriceLevelSchedule a station without a tariff key offers. There is no "
                     + "AbsolutePriceSchedule, so there is no verdict — expected is null, NOT three "
                     + "falses. Reporting an unsigned verdict here would accuse every ordinary station, "
                     + "Josev's included, of failing a check it was never asked to pass.",
                frame = Encode(Scheduled(price: null, signature: null)),
                verifyKey = (object?) null,
                expected = (object?) null,
            },
        };

        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            generator = "ISO15118ConformanceTests.Simulation.Traces.PriceScheduleSignatureCorpusTests.RegenerateTheCorpus",
            generatorNote =
                "Each case is a whole ScheduleExchangeRes frame (header Signature included), the verify "
              + "key the EV is given, and the Iso20TariffResult an EVCC must arrive at — or null where "
              + "there is nothing to verify. The digest half is a conformance statement: ISO 15118-20 "
              + "fixes it as SHA-512 over the AbsolutePriceSchedule EXI fragment. The signature half is "
              + "single-grammar here, unlike the -2 tariff check, because the counterparty that signs "
              + "under Josev's standalone grammar emits no price schedule at all. Keys are fixed for "
              + "reproducibility and are test material, never real eMSP keys.",
            tariffKeyD = TariffKeyD,
            otherKeyD  = OtherKeyD,
            cases,
        }, new JsonSerializerOptions { WriteIndented = true });

        var path = SourceVectorPath();
        File.WriteAllText(path, json, new UTF8Encoding(false));
        TestContext.Out.WriteLine($"wrote {cases.Count} cases to {path}");

    }

    private static string SourceVectorPath()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ISO15118ConformanceTests.Simulation.csproj")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not find the test project directory");
        var vectors = Path.Combine(dir!.FullName, "..", "libs", "EVSimulatorApp", "vectors");
        Directory.CreateDirectory(vectors);
        return Path.Combine(vectors, FileName);
    }

    private static string VectorPath =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors", FileName);

    /// <summary>The corpus still describes what it was built to describe — including the two cases that
    /// only a corpus can hold: the other control mode, and the offer with nothing to verify.</summary>
    [Test]
    public void TheCorpusCoversTheCasesItWasBuiltFor()
    {
        Assert.That(File.Exists(VectorPath), Is.True, $"corpus missing: {VectorPath}");

        var root  = JsonDocument.Parse(File.ReadAllText(VectorPath)).RootElement;
        var names = root.GetProperty("cases").EnumerateArray()
                        .Select(c => c.GetProperty("name").GetString()).ToArray();

        Assert.Multiple(() =>
        {
            foreach (var required in new[] { "signed-scheduled", "signed-dynamic", "unsigned",
                                             "digest-tampered", "wrong-key", "no-verify-key",
                                             "no-price-schedule" })
                Assert.That(names, Does.Contain(required), $"the {required} case is gone");
        });
    }

    /// <summary>The C# EVCC reaches the verdict the corpus records. Without this the corpus would be an
    /// oracle nothing on this side is held to.</summary>
    [Test]
    public void TheCSharpVerdictMatchesTheCorpus()
    {
        Assert.That(File.Exists(VectorPath), Is.True, $"corpus missing: {VectorPath}");
        var root = JsonDocument.Parse(File.ReadAllText(VectorPath)).RootElement;

        Assert.Multiple(() =>
        {
            foreach (var c in root.GetProperty("cases").EnumerateArray())
            {
                var name  = c.GetProperty("name").GetString()!;
                var frame = Convert.FromHexString(c.GetProperty("frame").GetString()!);

                var res = (ScheduleExchangeRes) CommonMessagesCodec.DecodeAny(frame, out _);

                using var verifyKey = c.GetProperty("verifyKey").ValueKind == JsonValueKind.Null
                    ? null
                    : PublicKeyFrom(c.GetProperty("verifyKey"));

                var verdict  = Iso20PriceScheduleCheck.Evaluate(res, res.Header.Signature, verifyKey);
                var expected = c.GetProperty("expected");

                if (expected.ValueKind == JsonValueKind.Null)
                {
                    Assert.That(verdict, Is.Null, $"{name}: nothing to verify, so no verdict");
                    continue;
                }

                Assert.That(verdict, Is.Not.Null, $"{name}: a verdict was expected");
                Assert.That(verdict!.SignaturePresent, Is.EqualTo(expected.GetProperty("signaturePresent").GetBoolean()), $"{name}: signaturePresent");
                Assert.That(verdict.DigestOk,          Is.EqualTo(expected.GetProperty("digestOk").GetBoolean()),         $"{name}: digestOk");
                Assert.That(verdict.SignatureOk,       Is.EqualTo(expected.GetProperty("signatureOk").GetBoolean()),      $"{name}: signatureOk");
            }
        });
    }

    private static ECDsa PublicKeyFrom(JsonElement key) =>
        ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP521,
            Q = new ECPoint
            {
                X = Convert.FromHexString(key.GetProperty("x").GetString()!),
                Y = Convert.FromHexString(key.GetProperty("y").GetString()!),
            },
        });

}
