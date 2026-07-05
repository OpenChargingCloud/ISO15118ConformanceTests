using NUnit.Framework;
using Vanaheimr.V2G.Iso15118_2.Generated;

namespace Vanaheimr.V2G.Exi.Tests;

/// <summary>
/// Differential wire conformance for the EXI <em>fragment</em> codec — the encoding used to digest a
/// signable element for XMLDSig (ISO 15118-2 §7.10 / Annex J). Each signable element is encoded as a
/// standalone fragment (EXI header + 8-bit fragment-grammar event code + the element's content) and
/// diffed against cbV2G's <c>encode_iso2_exiFragment</c> (tools/cbv2g-ref, <c>Fragment_&lt;name&gt;</c>).
/// The content mirrors the corresponding body fixtures.
/// </summary>
[TestFixture]
public class Iso15118_2FragmentTests
{
    [Test]
    public void AuthorizationReq_Fragment_MatchesCbV2G()
    {
        var content = new AuthorizationReqType(Id: null,
            GenChallenge: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
        var buf = new byte[512];
        Assert.That(Iso2Codec.EncodeFragment_AuthorizationReq(content, buf, out int n), Is.True);
        AssertFragment("80 04 42 00 20 40 60 80 a0 c0 e1 01 21 41 61 81 a1 c1 e2 07 a0", buf.AsSpan(0, n).ToArray());
    }

    [Test]
    public void MeteringReceiptReq_Fragment_MatchesCbV2G()
    {
        var content = new MeteringReceiptReqType(Id: null, SessionID: new byte[8], SAScheduleTupleID: 1,
            new MeterInfoType(MeterID: "M1", MeterReading: null, SigMeterReading: null, MeterStatus: null, TMeter: null));
        var buf = new byte[512];
        Assert.That(Iso2Codec.EncodeFragment_MeteringReceiptReq(content, buf, out int n), Is.True);
        AssertFragment("80 79 41 00 00 00 00 00 00 00 00 00 00 00 89 a6 28 f4", buf.AsSpan(0, n).ToArray());
    }

    [Test]
    public void SalesTariff_Fragment_MatchesCbV2G()
    {
        var content = new SalesTariffType(Id: null, SalesTariffID: 1, SalesTariffDescription: null, NumEPriceLevels: null,
            SalesTariffEntry: new[]
            {
                new SalesTariffEntryType(
                    TimeInterval: new RelativeTimeIntervalType(Start: 0, Duration: null),
                    EPriceLevel: null,
                    ConsumptionCost: System.Array.Empty<ConsumptionCostType>()),
            });
        var buf = new byte[512];
        Assert.That(Iso2Codec.EncodeFragment_SalesTariff(content, buf, out int n), Is.True);
        AssertFragment("80 ae 40 08 00 0c fa 00", buf.AsSpan(0, n).ToArray());
    }

    // ---- helpers ----

    private static void AssertFragment(string expectedHex, byte[]? actual)
    {
        Assert.That(actual, Is.Not.Null, "encode failed");
        var expected = expectedHex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(x => Convert.ToByte(x, 16)).ToArray();
        if (!actual!.AsSpan().SequenceEqual(expected))
            Assert.Fail($"fragment bytes diverge from cbV2G.\n" +
                        $"  expected ({expected.Length}): {ToHex(expected)}\n" +
                        $"  actual   ({actual.Length}): {ToHex(actual)}");
    }

    private static string ToHex(byte[] b) => string.Join(' ', b.Select(x => x.ToString("x2")));
}
