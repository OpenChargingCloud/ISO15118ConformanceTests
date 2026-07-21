using System.Text.Json;
using NUnit.Framework;

using Vanaheimr.V2G.Exi.Tests.Infrastructure;

namespace Vanaheimr.V2G.Exi.Tests
{
    /// <summary>
    /// Curated conformance vectors whose <c>expectedHex</c> comes from an <b>independent</b> stack — real EXI
    /// bytes captured from a live Josev (SwitchEV/iso15118 @ <c>d645255</c>) ISO 15118-20 DC Plug &amp; Charge
    /// session and promoted from the interop-run artifact into the checked-in vector suite
    /// (<c>Vectors/Iso15118_20.DC.josev.vectors.json</c>, <c>referenceEncoder = Josev/EXIficient</c>). Josev
    /// encodes with EXIficient, which shares no lineage with the cbV2G oracle behind the other -20 vector
    /// files, so a byte-identical decode → re-encode here is the highest-value conformance signal short of a
    /// live over-the-wire run. These frames carry Josev's own per-session SessionID/TimeStamp (not the fixed
    /// zero header the cbV2G fixtures build), so they are validated by round-trip, not by fixture rebuild.
    /// See <c>docs/interop-runs/2026-07-21-iso20-dc-pnc-notls/</c> and <c>docs/interop-runs/README.md</c>
    /// (the record-mode → vector adoption path).
    /// </summary>
    [TestFixture]
    public class JosevCuratedVectorTests
    {
        public sealed record Vec(string Name, string ExpectedHex);

        private static IEnumerable<TestCaseData> DcVectors()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors",
                                    "Iso15118_20.DC.josev.vectors.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var v in doc.RootElement.GetProperty("vectors").EnumerateArray())
            {
                var name = v.GetProperty("name").GetString()!;
                yield return new TestCaseData(new Vec(name, v.GetProperty("expectedHex").GetString()!)).SetName(name);
            }
        }

        [TestCaseSource(nameof(DcVectors))]
        public void JosevDcVector_DecodesAndReEncodesIdentically(Vec vector)
        {
            var expected = HexUtil.Parse(vector.ExpectedHex);
            var reEncoded = Iso15118_20DcFixtures.DecodeReEncode(expected);
            Assert.That(reEncoded, Is.EqualTo(expected),
                $"{vector.Name}: our codec must decode and re-encode Josev's EXIficient bytes identically");
        }
    }
}
