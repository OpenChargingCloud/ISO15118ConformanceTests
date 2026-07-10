using Vanaheimr.V2G.Iso15118_20.AC.Generated;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure;

/// <summary>The fixed ISO 15118-20 AC messages shared by the cbV2G byte-diff tests
/// (<c>Vectors/Iso15118_20.AC.vectors.json</c>, <c>main_iso20.c</c>'s <c>do_ac</c>).</summary>
public static class Iso15118_20AcFixtures
{
    private static MessageHeaderType Header() => new(SessionID: new byte[8], TimeStamp: 1_700_000_000UL, Signature: null);
    private static RationalNumberType Rational(sbyte exponent, short value) => new(exponent, value);

    public static bool TryEncode(string vectorName, byte[] dest, out int bytesWritten)
    {
        bytesWritten = 0;
        switch (vectorName)
        {
            case "AC_ChargeParameterDiscoveryReq":
                // Exercises the concrete (non-abstract-element) substitution head
                // AC_CPDReqEnergyTransferMode, choosing the base (non-BPT) member.
                return new AC_ChargeParameterDiscoveryReq(
                        Header(),
                        new AC_CPDReqEnergyTransferModeType(
                            EVMaximumChargePower: Rational(0, 11000),
                            EVMaximumChargePower_L2: null,
                            EVMaximumChargePower_L3: null,
                            EVMinimumChargePower: Rational(0, 100),
                            EVMinimumChargePower_L2: null,
                            EVMinimumChargePower_L3: null))
                    .TryEncode(dest, out bytesWritten);

            default:
                throw new ArgumentException($"no AC fixture for vector '{vectorName}'");
        }
    }
}
