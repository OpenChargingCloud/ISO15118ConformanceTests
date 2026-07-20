namespace Vanaheimr.V2G.Exi
{
    /// <summary>
    /// Shared decimal ↔ scaled-integer math for ISO 15118-20's <c>RationalNumberType</c>
    /// (<c>CommonTypes</c>: <c>Exponent xs:byte, Value xs:short</c>; <c>amount = Value · 10^Exponent</c>).
    /// The generator duplicates <c>RationalNumberType</c> once per message-set assembly (CommonMessages/
    /// DC/AC each get their own copy, mirroring cbV2G), so each assembly wraps this shared math in a tiny
    /// <c>RationalNumber.Of/.ToDecimal</c> helper rather than repeating the rounding logic three times.
    /// <para>
    /// <b>Rounding.</b> Picks the exponent closest to zero that still captures the amount exactly as an
    /// integer <see cref="short"/> — finer (more negative) only as far as needed to clear the fractional
    /// part, coarser (more positive) only as far as needed to fit <see cref="short"/>. Unlike
    /// <c>PhysicalValueType</c>'s multiplier (spec-capped to [-3, 3], so it truncates and rounds
    /// routinely), the byte-wide exponent here has so much more headroom than any <see cref="decimal"/>'s
    /// own dynamic range (±~7.9×10^28, ≤28 significant digits) that every representable
    /// <see cref="decimal"/> decomposes exactly — the half-to-even rounding and the overflow guard exist
    /// for defensive completeness but are not reachable through a valid <see cref="decimal"/> input.
    /// </para>
    /// </summary>
    public static class RationalNumberMath
    {
        public static (sbyte Exponent, short Value) Decompose(decimal amount)
        {
            sbyte exponent = 0;
            decimal scaled = amount;

            while (exponent > sbyte.MinValue && scaled != Math.Truncate(scaled))
            {
                exponent--;
                scaled = amount / Pow10(exponent);
            }
            while (exponent < sbyte.MaxValue && (scaled > short.MaxValue || scaled < short.MinValue))
            {
                exponent++;
                scaled = amount / Pow10(exponent);
            }
            if (scaled > short.MaxValue || scaled < short.MinValue)
                throw new OverflowException(
                    $"RationalNumber {amount} does not fit an exponent in [{sbyte.MinValue}, {sbyte.MaxValue}] " +
                    $"(|value| would exceed {short.MaxValue}).");

            return (exponent, (short)Math.Round(scaled, MidpointRounding.ToEven));
        }

        public static decimal Compose(sbyte exponent, short value) => value * Pow10(exponent);

        private static decimal Pow10(int exponent)
        {
            decimal p = 1m;
            for (int i = 0; i < (exponent < 0 ? -exponent : exponent); i++) p *= 10m;
            return exponent >= 0 ? p : 1m / p;
        }
    }
}
