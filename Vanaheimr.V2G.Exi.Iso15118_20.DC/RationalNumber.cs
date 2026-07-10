using Vanaheimr.V2G.Exi;
using Vanaheimr.V2G.Iso15118_20.DC.Generated;

namespace Vanaheimr.V2G.Iso15118_20.DC;

/// <summary>Ergonomics for this assembly's <see cref="RationalNumberType"/>; math shared via
/// <see cref="RationalNumberMath"/> (see there for rounding behaviour).</summary>
public static class RationalNumber
{
    public static RationalNumberType Of(decimal amount)
    {
        var (exponent, value) = RationalNumberMath.Decompose(amount);
        return new RationalNumberType(exponent, value);
    }

    public static decimal ToDecimal(this RationalNumberType value) =>
        RationalNumberMath.Compose(value.Exponent, value.Value);
}
