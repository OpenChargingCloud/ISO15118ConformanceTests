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

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Metering;

namespace ISO15118ConformanceTests.Simulation.Metering;

/// <summary>
/// The meter-signing corpus: the C# implementation's own output, checked in so the Swift and Kotlin
/// verifiers can be held to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a corpus at all.</b> The app has to verify what the station signs, so the payload layout
/// exists three times over. Writing it three times and testing each against itself is the mirrored
/// bug this project keeps running into — every side agrees, and every side is wrong together. A
/// corpus produced by one implementation and consumed by the others is what breaks that symmetry.
/// </para>
/// <para>
/// <b>What it is not.</b> Unlike the cbV2G vectors these are not a reference encoder's bytes — there
/// is no reference encoder, because ISO 15118 does not define this payload
/// (<see cref="MeterSigningPayload"/>). This is one implementation's output, and its value is
/// cross-language agreement plus a pin against drift, not evidence of conformance. The distinction
/// is the same one <c>AcDerCorpusTests</c> guards for the DER sets, and it is worth restating here
/// because "there is a vector file" reads like conformance to anyone who does not check.
/// </para>
/// <para>
/// The key is fixed rather than generated, so the <b>payloads</b> are reproducible: anyone can
/// regenerate them and get identical bytes. The <b>signatures</b> are not, and cannot be — ECDSA
/// picks its nonce at random, so every regeneration writes 64 fresh bytes per vector. That is why
/// <see cref="EveryVectorMatchesThisImplementation"/> compares payloads byte for byte but *verifies*
/// signatures against the public key instead of comparing them. Worth stating, because a
/// regeneration's diff is mostly noise and someone reviewing one needs to know which half is which.
/// (Corrected 2026-07-31; this paragraph previously claimed the whole file was reproducible.)
/// </para>
/// </remarks>
[TestFixture]
public class MeterVectorTests
{
    /// <summary>A fixed P-256 key, so the corpus is reproducible. Test material, never a real key.</summary>
    private const string PrivateKeyD =
        "c9afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721";

    private static readonly (string MeterId, int Protocol, string SessionId, ulong Reading, long? Timestamp)[]
        Cases =
        [
            ("VAN*M1",  2, "0102030405060708",       4200, 1_700_000_000),
            ("VAN*M1", 20, "0102030405060708",       4200, 1_700_000_000),
            ("VAN*M1",  2, "0102030405060709",       4200, 1_700_000_000),   // session binding
            ("A1",      2, "0000000000000000",         23, 0),               // length-prefix collision pair
            ("A",       2, "0000000000000000",        123, 0),
            ("",        2, "0000000000000000",          0, null),            // empty id, absent timestamp
            ("VAN*M1",  2, "ffffffffffffffff", ulong.MaxValue, long.MaxValue),
            ("Zähler-Ü", 2, "0102030405060708",       999, 1),               // non-ASCII id, UTF-8
        ];

    private static ECDsa Key()
    {
        var d = Convert.FromHexString(PrivateKeyD);
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        key.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = d,
            // Q is derived below by a round trip through the private key.
            Q = default,
        });
        return key;
    }

    private const string FileName = "Meter.signing.vectors.json";

    /// <summary>Where the tests read it from — copied next to the assembly by the csproj.</summary>
    private static string VectorPath =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors", FileName);

    /// <summary>
    /// Where the regenerator writes it: the source tree, found by walking up to the project. Writing
    /// to the output directory would produce a corpus that vanishes on the next clean, and one the
    /// Swift and Kotlin suites — which read it out of the source tree — would never see.
    /// </summary>
    private static string SourceVectorPath()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ISO15118ConformanceTests.Simulation.csproj")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not find the test project directory");
        var vectors = Path.Combine(dir!.FullName, "Vectors");
        Directory.CreateDirectory(vectors);
        return Path.Combine(vectors, FileName);
    }

    /// <summary>
    /// Regenerates the corpus. <see cref="ExplicitAttribute"/> because the file is an oracle for two
    /// other languages: it must change when someone means it to, never as a side effect of a run.
    /// </summary>
    [Test, Explicit("Regenerates Vectors/Meter.signing.vectors.json — run deliberately")]
    public void RegenerateTheCorpus()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        key.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D     = Convert.FromHexString(PrivateKeyD),
        });

        var q = key.ExportParameters(false).Q;

        var vectors = Cases.Select(c =>
        {
            var session = Convert.FromHexString(c.SessionId);
            var payload = MeterSigningPayload.Build(c.Protocol, session, c.MeterId, c.Reading, c.Timestamp);
            var signature = key.SignData(payload, HashAlgorithmName.SHA256,
                                         DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return new
            {
                meterId   = c.MeterId,
                protocol  = c.Protocol,
                sessionId = c.SessionId,
                reading   = c.Reading.ToString(CultureInfo.InvariantCulture),
                timestamp = c.Timestamp?.ToString(CultureInfo.InvariantCulture),
                payload   = Convert.ToHexString(payload).ToLowerInvariant(),
                signature = Convert.ToHexString(signature).ToLowerInvariant(),
            };
        }).ToArray();

        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            generator     = "ISO15118ConformanceTests.Simulation.Metering.MeterVectorTests.RegenerateTheCorpus",
            generatorNote =
                "One implementation's output, NOT a reference encoder's: ISO 15118 defines the "
              + "SigMeterReading/MeterSignature field and not its content, so no reference exists. "
              + "Its value is cross-language agreement between the C#, Swift and Kotlin verifiers, "
              + "plus a pin against drift — it is not evidence of wire conformance. The key is fixed "
              + "so the corpus is reproducible; it is test material and never a real meter key.",
            publicKeyX = Convert.ToHexString(q.X!).ToLowerInvariant(),
            publicKeyY = Convert.ToHexString(q.Y!).ToLowerInvariant(),
            privateKeyD = PrivateKeyD,
            vectors,
        }, new JsonSerializerOptions { WriteIndented = true });

        var path = SourceVectorPath();
        File.WriteAllText(path, json, new UTF8Encoding(false));
        TestContext.Out.WriteLine($"wrote {vectors.Length} vectors to {path}");
    }

    private static JsonElement Corpus()
    {
        Assert.That(File.Exists(VectorPath), Is.True, $"corpus missing: {VectorPath}");
        return JsonDocument.Parse(File.ReadAllText(VectorPath)).RootElement;
    }

    /// <summary>The corpus still describes what this implementation produces.</summary>
    [Test]
    public void EveryVectorMatchesThisImplementation()
    {
        var corpus = Corpus();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        key.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Convert.FromHexString(corpus.GetProperty("publicKeyX").GetString()!),
                Y = Convert.FromHexString(corpus.GetProperty("publicKeyY").GetString()!),
            },
        });

        var failures = new List<string>();

        foreach (var v in corpus.GetProperty("vectors").EnumerateArray())
        {
            var meterId   = v.GetProperty("meterId").GetString()!;
            var protocol  = v.GetProperty("protocol").GetInt32();
            var session   = Convert.FromHexString(v.GetProperty("sessionId").GetString()!);
            var reading   = ulong.Parse(v.GetProperty("reading").GetString()!, CultureInfo.InvariantCulture);
            long? stamp   = v.GetProperty("timestamp").ValueKind is JsonValueKind.Null
                                ? null
                                : long.Parse(v.GetProperty("timestamp").GetString()!, CultureInfo.InvariantCulture);
            var signature = Convert.FromHexString(v.GetProperty("signature").GetString()!);

            var payload = MeterSigningPayload.Build(protocol, session, meterId, reading, stamp);
            var actual  = Convert.ToHexString(payload).ToLowerInvariant();

            if (actual != v.GetProperty("payload").GetString())
                failures.Add($"{meterId}/{protocol}: payload differs\n  corpus {v.GetProperty("payload").GetString()}\n  actual {actual}");

            if (!SigningMeter.Verify(key, protocol, session, meterId, reading, stamp, signature))
                failures.Add($"{meterId}/{protocol}: signature does not verify");
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// The corpus covers the cases the layout was designed for, so a regeneration cannot quietly
    /// drop the interesting ones and leave a green suite behind.
    /// </summary>
    [Test]
    public void TheCorpusCoversTheAwkwardCases()
    {
        var vectors = Corpus().GetProperty("vectors").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(vectors, Has.Count.GreaterThanOrEqualTo(8));
            Assert.That(vectors.Any(v => v.GetProperty("protocol").GetInt32() is 20), "no -20 vector");
            Assert.That(vectors.Any(v => v.GetProperty("timestamp").ValueKind is JsonValueKind.Null),
                        "no absent-timestamp vector");
            Assert.That(vectors.Any(v => v.GetProperty("meterId").GetString() is ""), "no empty-id vector");
            Assert.That(vectors.Any(v => v.GetProperty("meterId").GetString()!.Any(c => c > 127)),
                        "no non-ASCII id vector — UTF-8 is where three languages most easily disagree");

            // The pair that would collide without length prefixes.
            var a1 = vectors.Single(v => v.GetProperty("meterId").GetString() is "A1");
            var a  = vectors.Single(v => v.GetProperty("meterId").GetString() is "A");
            Assert.That(a1.GetProperty("payload").GetString(),
                        Is.Not.EqualTo(a.GetProperty("payload").GetString()));
        });
    }
}
