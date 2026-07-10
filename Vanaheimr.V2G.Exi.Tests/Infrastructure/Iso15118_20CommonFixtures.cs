using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure;

/// <summary>
/// The fixed ISO 15118-20 CommonMessages messages shared by the cbV2G byte-diff tests. Each is keyed
/// by the same name used in <c>Vectors/Iso15118_20.CommonMessages.vectors.json</c> and in the
/// reference tool <c>tools/cbv2g-ref/main_iso20.c</c> (<c>Common_&lt;name&gt;</c>). Every message uses
/// an all-zero 8-byte SessionID, a fixed TimeStamp and no header signature — exposed as a single
/// <see cref="TryEncode"/> so the test project never needs to reference the generated types directly
/// (CommonMessages/DC/AC all declare their own <c>MessageHeaderType</c>/<c>ResponseCode</c>, which
/// would collide if imported into the same file).
/// </summary>
public static class Iso15118_20CommonFixtures
{
    private static MessageHeaderType Header() => new(SessionID: new byte[8], TimeStamp: 1_700_000_000UL, Signature: null);

    public static bool TryEncode(string vectorName, byte[] dest, out int bytesWritten)
    {
        bytesWritten = 0;
        switch (vectorName)
        {
            case "SessionSetupReq":
                return new SessionSetupReq(Header(), "EVCCID1234567")
                    .TryEncode(dest, out bytesWritten);

            case "SessionSetupRes":
                return new SessionSetupRes(Header(), ResponseCode.OK, "EVSEID1234567")
                    .TryEncode(dest, out bytesWritten);

            case "AuthorizationSetupRes":
                // Exercises InlineChoice (EIM/PnC) and the repeating-list-with-tail
                // (AuthorizationServices -> CertificateInstallationService) together.
                return new AuthorizationSetupRes(
                        Header(), ResponseCode.OK,
                        AuthorizationServices: new[] { Authorization.EIM, Authorization.PnC },
                        CertificateInstallationService: true,
                        EIM_ASResAuthorizationMode: new EIM_ASResAuthorizationModeType(),
                        PnC_ASResAuthorizationMode: null)
                    .TryEncode(dest, out bytesWritten);

            default:
                throw new ArgumentException($"no CommonMessages fixture for vector '{vectorName}'");
        }
    }
}
