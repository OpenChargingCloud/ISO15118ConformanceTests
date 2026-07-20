using System.Text.Json;
using NUnit.Framework;
using Vanaheimr.V2G.Exi;
using Vanaheimr.V2G.Exi.Tests.Infrastructure;

namespace Vanaheimr.V2G.Exi.Tests
{
    /// <summary>
    /// Vector-driven tests for the schema-less EXI datatypes, loaded from
    /// <c>Vectors/Primitives.vectors.json</c>.
    /// <para>
    /// <b>Provenance.</b> These <c>expectedHex</c> values are currently self-encoded by the
    /// codec under test — they guard against accidental regressions but do not yet prove wire
    /// conformance. The intended external oracle is EXIficient; see <c>PRIMITIVES_VECTORS.md</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    public class PrimitiveVectorTests
    {
        public static IEnumerable<TestCaseData> All()
        {
            var dir  = Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors");
            var file = Path.Combine(dir, "Primitives.vectors.json");
            if (!File.Exists(file)) yield break;

            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var v in doc.RootElement.GetProperty("vectors").EnumerateArray())
            {
                var name = v.GetProperty("name").GetString()!;
                yield return new TestCaseData(v.Clone()).SetName($"{{m}}({name})");
            }
        }

        [TestCaseSource(nameof(All))]
        public void Encode_Matches_Expected(JsonElement v)
        {
            var datatype = v.GetProperty("datatype").GetString()!;
            var expected = HexUtil.Parse(v.GetProperty("expectedHex").GetString()!);

            var buf = new byte[512];
            var w = new BitWriter(buf);

            switch (datatype)
            {
                case "unsignedInteger":
                    ExiPrimitives.WriteUnsignedInteger(ref w, ulong.Parse(v.GetProperty("value").GetString()!));
                    break;
                case "signedInteger":
                    ExiPrimitives.WriteSignedInteger(ref w, long.Parse(v.GetProperty("value").GetString()!));
                    break;
                case "binary":
                    ExiPrimitives.WriteBinary(ref w, HexUtil.Parse(v.GetProperty("valueHex").GetString()!));
                    break;
                case "boolean":
                    ExiPrimitives.WriteBoolean(ref w, bool.Parse(v.GetProperty("value").GetString()!));
                    break;
                case "string":
                    ExiPrimitives.WriteStringValue(ref w, v.GetProperty("value").GetString()!);
                    break;
                default:
                    throw new NotSupportedException($"Unknown primitive datatype '{datatype}'.");
            }

            w.AlignToByte();
            var actual = buf.AsSpan(0, w.BytesWritten).ToArray();

            if (!actual.AsSpan().SequenceEqual(expected))
                Assert.Fail($"{datatype}: encode mismatch\n{HexUtil.Diff(expected, actual)}");
        }
    }
}
