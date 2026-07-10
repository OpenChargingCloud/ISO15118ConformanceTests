using Vanaheimr.V2G.Iso15118_20.DC.Generated;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure;

/// <summary>The fixed ISO 15118-20 DC messages shared by the cbV2G byte-diff tests
/// (<c>Vectors/Iso15118_20.DC.vectors.json</c>, <c>main_iso20.c</c>'s <c>do_dc</c>).</summary>
public static class Iso15118_20DcFixtures
{
    private static MessageHeaderType Header() => new(SessionID: new byte[8], TimeStamp: 1_700_000_000UL, Signature: null);

    public static bool TryEncode(string vectorName, byte[] dest, out int bytesWritten)
    {
        bytesWritten = 0;
        switch (vectorName)
        {
            case "DC_CableCheckReq":
                return new DC_CableCheckReq(Header()).TryEncode(dest, out bytesWritten);

            case "DC_CableCheckRes":
                return new DC_CableCheckRes(Header(), ResponseCode.OK, Processing.Finished)
                    .TryEncode(dest, out bytesWritten);

            default:
                throw new ArgumentException($"no DC fixture for vector '{vectorName}'");
        }
    }
}
