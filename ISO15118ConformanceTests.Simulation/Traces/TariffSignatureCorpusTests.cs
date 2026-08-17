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

using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118_2;
using cloud.charging.open.protocols.ISO15118_2.Generated;

namespace ISO15118ConformanceTests.Simulation.Traces;

/// <summary>
/// A corpus of signed SASchedule offers and the verdict an EVCC should reach about them (§7.9.2.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists and a recorded session does not suffice.</b> A session trace carries wire bytes,
/// and a tariff-signature verdict is not on the wire: the EV reads the offer, checks a digest per
/// SalesTariff and one ECDSA signature over the SignedInfo, and says nothing about the result to the
/// station. Replaying a recorded signed offer therefore proves a port can *parse* one; it cannot prove
/// the port *judges* it correctly. For a verifier that is the whole question — a broken one says "ok"
/// silently, which is the failure mode this project spends most of its effort refusing.
/// </para>
/// <para>
/// So the shape here is <c>Certificate.chain.vectors.json</c>'s, not a trace's: named cases, each with
/// the exact bytes and the expected answer, including the negatives no recorded session can contain.
/// A station does not offer a tampered digest or sign with the wrong key, and those are precisely the
/// cases where a verifier that always returns true still looks fine.
/// </para>
/// <para>
/// <b>Both grammars, deliberately.</b> ISO's own form encodes the SignedInfo under the combined
/// <c>V2G_CI_MsgDef</c> fragment grammar; Josev signs the same structure under the standalone xmldsig
/// grammar. Those are different octets, so a verifier must try both and report which one matched —
/// exactly what <c>Iso2TariffResult.SignatureGrammar</c> carries. A port that implements one and not
/// the other passes half the field and fails the other half, quietly.
/// </para>
/// <para>
/// The keys are fixed so the corpus is reproducible. They are test material and never real
/// Mobility-Operator keys.
/// </para>
/// </remarks>
[TestFixture]
public class TariffSignatureCorpusTests
{

    private const string FileName = "Tariff.signature.vectors.json";

    /// <summary>The Mobility Operator's key, as far as this corpus is concerned.</summary>
    private const string TariffKeyD =
        "5a1f3c8e7b2d94061fae83c5d72b0e94183fa6c2b95d07e4318cfa62d5079b13";

    /// <summary>A second, unrelated key — the one the <c>wrong-key</c> case verifies against.</summary>
    private const string OtherKeyD =
        "2c9d70b4e1583fa6027dc94b3e85107fa2c6d3095b74e128fa03d76c5b192e84";

    // The offer the SECC makes when a tariff key is configured (Secc2.TariffSchedules): tuple 1 flat at
    // price levels 2→3, tuple 2 cheaper (1→2) and stepped. A price-aware EV picks tuple 2 — average 1.5
    // against 2.5 — and derives a two-entry ChargingProfile from its PMaxSchedule. Those two numbers are
    // pinned in every case below, so the corpus also holds the half that *was* already ported.
    private const int ThreePhase16A  = 11_040;
    private const int SinglePhase32A =  7_360;
    private const int ThreePhase32A  = 22_080;

    private static PhysicalValueType Watt(int w) => PhysicalValue.Of(w, UnitSymbol.W);
    private static PhysicalValueType Volt(int v) => PhysicalValue.Of(v, UnitSymbol.V);
    private static PhysicalValueType Amp(short a) => new(0, UnitSymbol.A, a);

    private static SalesTariffEntryType TariffEntry(uint start, byte level) =>
        new(new RelativeTimeIntervalType(Start: start, Duration: null), EPriceLevel: level,
            ConsumptionCost: Array.Empty<ConsumptionCostType>());

    private static SAScheduleListType SignedOffer(string description2 = "off-peak boost") =>
        new(new[]
        {
            new SAScheduleTupleType(SAScheduleTupleID: 1,
                new PMaxScheduleType(new[]
                {
                    new PMaxScheduleEntryType(new RelativeTimeIntervalType(Start: 0, Duration: 3600), PMax: Watt(ThreePhase16A)),
                }),
                new SalesTariffType(Id: "salesTariff1", SalesTariffID: 1, SalesTariffDescription: "standard",
                    NumEPriceLevels: 3, SalesTariffEntry: new[] { TariffEntry(0, level: 2), TariffEntry(1800, level: 3) })),
            new SAScheduleTupleType(SAScheduleTupleID: 2,
                new PMaxScheduleType(new[]
                {
                    new PMaxScheduleEntryType(new RelativeTimeIntervalType(Start: 0, Duration: 1800), PMax: Watt(SinglePhase32A)),
                    new PMaxScheduleEntryType(new RelativeTimeIntervalType(Start: 1800, Duration: 1800), PMax: Watt(ThreePhase32A)),
                }),
                new SalesTariffType(Id: "salesTariff2", SalesTariffID: 2, SalesTariffDescription: description2,
                    NumEPriceLevels: 3, SalesTariffEntry: new[] { TariffEntry(0, level: 1), TariffEntry(1800, level: 2) })),
        });

    /// <summary>The plain single unsigned tuple — what the station offers with no tariff key.</summary>
    private static SAScheduleListType PlainOffer() =>
        new(new[]
        {
            new SAScheduleTupleType(SAScheduleTupleID: 1,
                new PMaxScheduleType(new[]
                {
                    new PMaxScheduleEntryType(new RelativeTimeIntervalType(Start: 0, Duration: 3600), PMax: Watt(ThreePhase16A)),
                }),
                SalesTariff: null),
        });

    private static EVSEChargeParameterType AcChargeParameter() =>
        new AC_EVSEChargeParameterType(
            new AC_EVSEStatusType(NotificationMaxDelay: 0, EVSENotification.None, RCD: false),
            EVSENominalVoltage: Volt(230), EVSEMaxCurrent: Amp(32));

    /// <summary>One header signature over every SalesTariff in the offer — one reference per Id, in the
    /// order the tuples are offered. <paramref name="standalone"/> picks which grammar the SignedInfo is
    /// signed under; the SignedInfo that travels in the message is identical either way, which is exactly
    /// why a verifier cannot tell them apart without trying both.</summary>
    private static SignatureType SignTariffs(SAScheduleListType offer, ECDsa key, bool standalone)
    {
        var references = new List<(string ReferenceId, byte[] Digest)>();
        var buf = new byte[2048];

        foreach (var tuple in offer.SAScheduleTuple)
        {
            if (tuple.SalesTariff?.Id is not { } id) continue;
            if (!Iso2Codec.EncodeFragment_SalesTariff(tuple.SalesTariff, buf, out int n))
                throw new InvalidOperationException("SalesTariff fragment encode failed.");
            references.Add((id, V2GSignature.Digest(buf.AsSpan(0, n))));
        }

        var signedInfo = V2GSignature.BuildSignedInfo(references, includeExiTransform: true);

        var value = standalone
            ? key.SignData(XmlDsigInterop2.StandaloneOctets(signedInfo)
                               ?? throw new InvalidOperationException("standalone encode failed."),
                           HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            : V2GSignature.Sign(signedInfo, key);

        return V2GSignature.BuildSignature(signedInfo, value);
    }

    private static string EncodeMessage(SAScheduleListType offer, SignatureType? signature)
    {
        var message = new V2G_Message(
            new MessageHeaderType(SessionID: Convert.FromHexString("00a1b2c3d4e5f607"),
                                  Notification: null, Signature: signature),
            new BodyType(new ChargeParameterDiscoveryResType(
                ResponseCode.OK, EVSEProcessing.Finished, offer, AcChargeParameter())));

        var buf = new byte[8192];
        if (!message.TryEncode(buf, out int n))
            throw new InvalidOperationException("ChargeParameterDiscoveryRes encode failed.");

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

    private static ECDsa KeyFrom(string d)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        key.ImportParameters(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = Convert.FromHexString(d) });
        return key;
    }

    /// <summary>
    /// Regenerates the corpus. <see cref="ExplicitAttribute"/> because the file is an oracle for two other
    /// languages: it must change when someone means it to, never as a side effect of a run.
    /// </summary>
    [Test, Explicit("Regenerates vectors/Tariff.signature.vectors.json — run deliberately")]
    public void RegenerateTheCorpus()
    {

        using var tariffKey = KeyFrom(TariffKeyD);
        using var otherKey  = KeyFrom(OtherKeyD);

        var cases = new List<object>();

        // (1) the ordinary signed offer, ISO's own grammar, verified with the right key.
        {
            var offer = SignedOffer();
            cases.Add(new
            {
                name = "signed-msgdef",
                what = "Two signed tariffs, SignedInfo signed under the combined V2G_CI_MsgDef fragment "
                     + "grammar (ISO's own form, what cbV2G produces). Everything checks out.",
                frame = EncodeMessage(offer, SignTariffs(offer, tariffKey, standalone: false)),
                verifyKey = PublicKeyOf(tariffKey),
                expected = new
                {
                    tuplesOffered = 2, signaturePresent = true, digestOk = true,
                    signatureOk = true, signatureGrammar = "iso2-msgdef",
                    chosenTupleId = 2, profileEntries = 2,
                },
            });
        }

        // (2) the same offer signed the way a live Josev peer signs it. Same SignedInfo on the wire,
        //     different octets underneath — the case that makes the dual-grammar attempt necessary.
        {
            var offer = SignedOffer();
            cases.Add(new
            {
                name = "signed-standalone",
                what = "Identical offer, but the SignedInfo is signed over its STANDALONE xmldsig encoding "
                     + "(Josev's form). A verifier that only knows ISO's grammar reports signatureOk=false "
                     + "here and rejects a peer that is in the field today.",
                frame = EncodeMessage(offer, SignTariffs(offer, tariffKey, standalone: true)),
                verifyKey = PublicKeyOf(tariffKey),
                expected = new
                {
                    tuplesOffered = 2, signaturePresent = true, digestOk = true,
                    signatureOk = true, signatureGrammar = "xmldsig-standalone",
                    chosenTupleId = 2, profileEntries = 2,
                },
            });
        }

        // (3) no signature at all — the plain offer. Not a failure: most stations send this.
        cases.Add(new
        {
            name = "unsigned",
            what = "The plain single unsigned tuple. signaturePresent=false is the honest answer, and "
                 + "digestOk/signatureOk are false because there was nothing to check — not because "
                 + "something failed.",
            frame = EncodeMessage(PlainOffer(), signature: null),
            verifyKey = (object?) null,
            expected = new
            {
                tuplesOffered = 1, signaturePresent = false, digestOk = false,
                signatureOk = false, signatureGrammar = "none",
                chosenTupleId = 1, profileEntries = 1,
            },
        });

        // (4) the signature is over the offer as signed, but a tariff changed afterwards. The ECDSA
        //     signature still verifies — it covers the SignedInfo, not the tariffs — and only the digest
        //     catches it. A verifier that checks the signature and skips the digests passes this.
        {
            var signedOffer  = SignedOffer();
            var tamperedOffer = SignedOffer(description2: "off-peak boost (altered)");
            cases.Add(new
            {
                name = "digest-tampered",
                what = "Signed correctly, then one SalesTariff's description was edited. The SignedInfo "
                     + "signature still verifies; the per-tariff digest does not. A verifier that trusts "
                     + "the ECDSA check alone reports this offer as good.",
                frame = EncodeMessage(tamperedOffer, SignTariffs(signedOffer, tariffKey, standalone: false)),
                verifyKey = PublicKeyOf(tariffKey),
                expected = new
                {
                    tuplesOffered = 2, signaturePresent = true, digestOk = false,
                    signatureOk = true, signatureGrammar = "iso2-msgdef",
                    chosenTupleId = 2, profileEntries = 2,
                },
            });
        }

        // (5) properly signed, wrong verify key: neither grammar matches, so the grammar stays "none".
        {
            var offer = SignedOffer();
            cases.Add(new
            {
                name = "wrong-key",
                what = "A sound signature verified against an unrelated key. Both grammars are tried and "
                     + "both fail, so signatureGrammar stays \"none\" — the field says which grammar "
                     + "MATCHED, and none did.",
                frame = EncodeMessage(offer, SignTariffs(offer, tariffKey, standalone: false)),
                verifyKey = PublicKeyOf(otherKey),
                expected = new
                {
                    tuplesOffered = 2, signaturePresent = true, digestOk = true,
                    signatureOk = false, signatureGrammar = "none",
                    chosenTupleId = 2, profileEntries = 2,
                },
            });
        }

        // (6) signed, digests fine, but the EV holds no tariff key. digestOk is still a real answer;
        //     signatureOk is not, and must not be reported as one.
        {
            var offer = SignedOffer();
            cases.Add(new
            {
                name = "no-verify-key",
                what = "Signed offer, correct digests, but the EV was given no tariff verify key. The "
                     + "digest half is still checkable and reported; the signature half is not attempted, "
                     + "and false here means \"not established\" rather than \"failed\".",
                frame = EncodeMessage(offer, SignTariffs(offer, tariffKey, standalone: false)),
                verifyKey = (object?) null,
                expected = new
                {
                    tuplesOffered = 2, signaturePresent = true, digestOk = true,
                    signatureOk = false, signatureGrammar = "none",
                    chosenTupleId = 2, profileEntries = 2,
                },
            });
        }

        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            generator = "ISO15118ConformanceTests.Simulation.Traces.TariffSignatureCorpusTests.RegenerateTheCorpus",
            generatorNote =
                "Each case is a whole ChargeParameterDiscoveryRes frame (header Signature included), the "
              + "verify key the EV is given, and the Iso2TariffResult an EVCC must arrive at. Unlike the "
              + "meter corpus this IS a conformance statement for the digest half — ISO 15118-2 §7.9.2.5 "
              + "fixes the digest as SHA-256 over the SalesTariff EXI fragment. The signature half is "
              + "two-grammar by field reality rather than by the standard: ISO's own form signs the "
              + "SignedInfo under the combined MsgDef fragment grammar, Josev under the standalone "
              + "xmldsig one, and a verifier meeting both has to try both. Keys are fixed for "
              + "reproducibility and are test material, never real Mobility-Operator keys.",
            tariffKeyD = TariffKeyD,
            otherKeyD  = OtherKeyD,
            cases,
        }, new JsonSerializerOptions { WriteIndented = true });

        var path = SourceVectorPath();
        File.WriteAllText(path, json, new UTF8Encoding(false));
        TestContext.Out.WriteLine($"wrote {cases.Count} cases to {path}");

    }

    /// <summary>Where the regenerator writes it: <c>libs/EVSimulatorApp/vectors/</c>, the submodule's source
    /// tree, beside every other corpus the ports are held to.</summary>
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

    /// <summary>The corpus still describes what it was built to describe. Guards against a regeneration
    /// that quietly drops the cases only a corpus can carry — the negatives.</summary>
    [Test]
    public void TheCorpusCoversTheCasesItWasBuiltFor()
    {
        Assert.That(File.Exists(VectorPath), Is.True, $"corpus missing: {VectorPath}");

        var root  = JsonDocument.Parse(File.ReadAllText(VectorPath)).RootElement;
        var names = root.GetProperty("cases").EnumerateArray()
                        .Select(c => c.GetProperty("name").GetString()).ToArray();

        Assert.Multiple(() =>
        {
            foreach (var required in new[] { "signed-msgdef", "signed-standalone", "unsigned",
                                             "digest-tampered", "wrong-key", "no-verify-key" })
                Assert.That(names, Does.Contain(required), $"the {required} case is gone");
        });
    }

    /// <summary>The C# EVCC reaches the verdict the corpus records. Without this the corpus would be an
    /// oracle nothing on this side is held to — and the ports would be checked against a claim no
    /// implementation had ever satisfied.</summary>
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

                var decoded = (V2G_Message) Iso2Codec.DecodeAny(frame, out _);
                var body    = (ChargeParameterDiscoveryResType) decoded.Body.BodyElement!;

                using var verifyKey = c.GetProperty("verifyKey").ValueKind == JsonValueKind.Null
                    ? null
                    : PublicKeyFrom(c.GetProperty("verifyKey"));

                var verdict  = Iso2TariffCheck.Evaluate(body.SASchedules as SAScheduleListType,
                                                        decoded.Header.Signature, verifyKey);
                var expected = c.GetProperty("expected");

                Assert.That(verdict.SignaturePresent, Is.EqualTo(expected.GetProperty("signaturePresent").GetBoolean()), $"{name}: signaturePresent");
                Assert.That(verdict.DigestOk,         Is.EqualTo(expected.GetProperty("digestOk").GetBoolean()),         $"{name}: digestOk");
                Assert.That(verdict.SignatureOk,      Is.EqualTo(expected.GetProperty("signatureOk").GetBoolean()),      $"{name}: signatureOk");
                Assert.That(verdict.SignatureGrammar, Is.EqualTo(expected.GetProperty("signatureGrammar").GetString()),  $"{name}: signatureGrammar");
            }
        });
    }

    private static ECDsa PublicKeyFrom(JsonElement key) =>
        ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Convert.FromHexString(key.GetProperty("x").GetString()!),
                Y = Convert.FromHexString(key.GetProperty("y").GetString()!),
            },
        });

}
