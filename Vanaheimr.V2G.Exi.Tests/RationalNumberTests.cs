using System.Globalization;
using NUnit.Framework;
using Vanaheimr.V2G.Exi;
using Vanaheimr.V2G.Iso15118_20.CommonMessages;
using Vanaheimr.V2G.Iso15118_20.DC;
using Vanaheimr.V2G.Iso15118_20.AC;

namespace Vanaheimr.V2G.Exi.Tests;

/// <summary>
/// Rounding / range behaviour of the shared <see cref="RationalNumberMath"/> decimal ↔ scaled-integer
/// bridge that backs every message-set's <c>RationalNumber.Of/.ToDecimal</c> wrapper (ISO 15118-20's
/// <c>RationalNumberType</c> is duplicated once per generated assembly, so the math lives here once
/// instead of three times — see <see cref="Vanaheimr.V2G.Iso15118_20.CommonMessages.RationalNumber"/>,
/// <see cref="Vanaheimr.V2G.Iso15118_20.DC.RationalNumber"/>, <see cref="Vanaheimr.V2G.Iso15118_20.AC.RationalNumber"/>).
/// </summary>
[TestFixture]
public class RationalNumberTests
{
    [TestCase("400", (sbyte)0, (short)400)]        // fits at exponent 0
    [TestCase("0.001", (sbyte)-3, (short)1)]        // finer than PhysicalValue's cap — exponent isn't limited to [-3,3]
    [TestCase("230.5", (sbyte)-1, (short)2305)]
    [TestCase("40000", (sbyte)1, (short)4000)]      // too big for short at 0 -> exponent 1
    [TestCase("-12.34", (sbyte)-2, (short)-1234)]
    [TestCase("0", (sbyte)0, (short)0)]
    public void Decompose_PicksFinestFittingExponent(string amount, sbyte exponent, short value)
    {
        var (e, v) = RationalNumberMath.Decompose(decimal.Parse(amount, CultureInfo.InvariantCulture));
        Assert.That(e, Is.EqualTo(exponent));
        Assert.That(v, Is.EqualTo(value));
    }

    [Test]
    public void ComposeOfDecompose_IsTheInverseOfExactlyRepresentableAmounts()
    {
        foreach (var amount in new[] { 400m, 0.001m, 230.5m, 40000m, -12.34m, 0m })
        {
            var (e, v) = RationalNumberMath.Decompose(amount);
            Assert.That(RationalNumberMath.Compose(e, v), Is.EqualTo(amount), $"round-trip failed for {amount}");
        }
    }

    [Test]
    public void Decompose_IsExactEvenForManyFractionalDigits()
    {
        // Unlike PhysicalValue (multiplier capped at -3), the byte-wide exponent has no trouble
        // going far enough negative to capture every fractional digit decimal can hold, exactly.
        var (e, v) = RationalNumberMath.Decompose(0.0000001234m);
        Assert.That(RationalNumberMath.Compose(e, v), Is.EqualTo(0.0000001234m));
    }

    [Test]
    public void Decompose_HandlesMagnitudesFarBeyondPhysicalValuesRange()
    {
        // 15 significant digits — far past PhysicalValue's overflow point (~3.3x10^7 at multiplier 3)
        // but nowhere near decimal's own ~7.9x10^28 ceiling, so this is squarely realistic territory
        // for the byte-wide exponent, just rounded to the nearest short at the coarsest needed scale.
        const decimal amount = 123_456_789_012_345m;
        var (e, v) = RationalNumberMath.Decompose(amount);
        var unit = RationalNumberMath.Compose(e, 1);

        Assert.That(RationalNumberMath.Compose(e, v), Is.EqualTo(amount).Within(unit),
            "the re-composed value should be within one unit of the exponent's own granularity");
    }

    [Test]
    public void CommonMessages_Wrapper_RoundtripsThroughGeneratedType()
    {
        var r = Vanaheimr.V2G.Iso15118_20.CommonMessages.RationalNumber.Of(3.7m);
        Assert.That(r.ToDecimal(), Is.EqualTo(3.7m));
    }

    [Test]
    public void DC_Wrapper_RoundtripsThroughGeneratedType()
    {
        var r = Vanaheimr.V2G.Iso15118_20.DC.RationalNumber.Of(400m);
        Assert.That(r.ToDecimal(), Is.EqualTo(400m));
    }

    [Test]
    public void AC_Wrapper_RoundtripsThroughGeneratedType()
    {
        var r = Vanaheimr.V2G.Iso15118_20.AC.RationalNumber.Of(11000m);
        Assert.That(r.ToDecimal(), Is.EqualTo(11000m));
    }
}
