using NUnit.Framework;
using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;

namespace Vanaheimr.V2G.Exi.Tests;

/// <summary>
/// Differential wire conformance for the ISO 15118-20 CommonMessages EXI <em>fragment</em> codec —
/// the encoding used to digest a signable element for XMLDSig. Each signable element is encoded as a
/// standalone fragment and diffed against cbV2G's <c>encode_iso20_exiFragment</c>
/// (tools/cbv2g-ref/main_iso20.c, <c>Fragment_&lt;name&gt;</c>). Unlike -2 (ECDSA-SHA256/32-byte
/// digest), -20 uses the stronger ECDSA-SHA512 suite with a 64-byte digest.
/// </summary>
[TestFixture]
public class Iso15118_20FragmentTests
{
    [Test]
    public void SignedInfo_Fragment_MatchesCbV2G()
    {
        // The XMLDSig SignedInfo subtree ISO 15118-20 puts on the wire: EXI-canonical C14N,
        // ECDSA-SHA512 signature method, a single Reference (no Transforms) over a 64-byte SHA-512
        // digest. Mirrors tools/cbv2g-ref do_fragment("SignedInfo") for -20.
        var content = new SignedInfoType(
            Id: null,
            CanonicalizationMethod: new CanonicalizationMethodType(
                Algorithm: "http://www.w3.org/TR/canonical-exi/", ANY: null),
            SignatureMethod: new SignatureMethodType(
                Algorithm: "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha512",
                HMACOutputLength: null, ANY: null),
            Reference: new[]
            {
                new ReferenceType(Id: null, Type: null, URI: "#ID1", Transforms: null,
                    DigestMethod: new DigestMethodType(
                        Algorithm: "http://www.w3.org/2001/04/xmlenc#sha512", ANY: null),
                    DigestValue: Enumerable.Range(1, 64).Select(i => (byte)i).ToArray()),
            });
        var buf = new byte[512];
        Assert.That(CommonMessagesCodec.EncodeFragment_SignedInfo(content, buf, out int n), Is.True);
        AssertFragment(
            "80 73 22 56 87 47 47 03 a2 f2 f7 77 77 72 e7 73 32 e6 f7 26 72 f5 45 22 f6 36 16 e6 f6 e6 " +
            "96 36 16 c2 d6 57 86 92 f4 35 68 74 74 70 3a 2f 2f 77 77 77 2e 77 33 2e 6f 72 67 2f 32 30 " +
            "30 31 2f 30 34 2f 78 6d 6c 64 73 69 67 2d 6d 6f 72 65 23 65 63 64 73 61 2d 73 68 61 35 31 " +
            "32 44 0c 46 92 88 62 8a 5a 1d 1d 1c 0e 8b cb dd dd dd cb 9d cc cb 9b dc 99 cb cc 8c 0c 0c " +
            "4b cc 0d 0b de 1b 5b 19 5b 98 c8 dc da 18 4d 4c 4c 91 00 04 08 0c 10 14 18 1c 20 24 28 2c " +
            "30 34 38 3c 40 44 48 4c 50 54 58 5c 60 64 68 6c 70 74 78 7c 80 84 88 8c 90 94 98 9c a0 a4 " +
            "a8 ac b0 b4 b8 bc c0 c4 c8 cc d0 d4 d8 dc e0 e4 e8 ec f0 f4 f8 fd 00 63 40",
            buf.AsSpan(0, n).ToArray());
    }

    [Test]
    public void MeteringConfirmationReq_Fragment_MatchesCbV2G()
    {
        var content = new MeteringConfirmationReqType(
            Header: new MessageHeaderType(SessionID: new byte[8], TimeStamp: 1_700_000_000UL, Signature: null),
            SignedMeteringData: new SignedMeteringDataType(
                Id: "ID1", SessionID: new byte[8],
                MeterInfo: new MeterInfoType(
                    MeterID: "M1", ChargedEnergyReadingWh: 5000,
                    BPT_DischargedEnergyReadingWh: null, CapacitiveEnergyReadingVARh: null,
                    BPT_InductiveEnergyReadingVARh: null, MeterSignature: null,
                    MeterStatus: null, MeterTimestamp: null),
                Receipt: null,
                Dynamic_SMDTControlMode: null,
                Scheduled_SMDTControlMode: new Scheduled_SMDTControlModeType(SelectedScheduleTupleID: 1)));
        var buf = new byte[512];
        Assert.That(CommonMessagesCodec.EncodeFragment_MeteringConfirmationReq(content, buf, out int n), Is.True);
        AssertFragment(
            "80 3b 80 80 00 00 00 00 00 00 00 01 01 c5 9f 54 0c 40 54 94 43 10 20 00 00 00 00 00 00 " +
            "00 00 01 13 4c 44 41 3b 40 08 46 80",
            buf.AsSpan(0, n).ToArray());
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
