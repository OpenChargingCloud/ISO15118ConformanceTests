using System.Text;

namespace Vanaheimr.V2G.Exi;

/// <summary>
/// EXI primitive type codecs: Unsigned Integer, n-bit Unsigned Integer, and String values.
/// <para>
/// These primitives are independent of any schema and should be exercised against the
/// EXI W3C test suite for byte-level conformance. The schema-informed grammar layer
/// (the codecs in <c>AppProtocol/</c>) builds on top of them.
/// </para>
/// <para>
/// String value handling here covers only the "miss" case (verbatim value, length+2 prefix).
/// Local/global value-table hits are TODO — they only matter once the same string repeats
/// within a single EXI stream, which is uncommon for AppProtocol but mandatory for
/// real ISO 15118-2 / -20 message codecs.
/// </para>
/// </summary>
public static class ExiPrimitives
{
    /// <summary>
    /// Encode an EXI Unsigned Integer: 7 bits of value per byte, MSB = continuation flag.
    /// </summary>
    public static void WriteUnsignedInteger(ref BitWriter w, ulong value)
    {
        do
        {
            byte chunk = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) chunk |= 0x80;
            w.WriteBits(chunk, 8);
        } while (value != 0);
    }

    public static ulong ReadUnsignedInteger(ref BitReader r)
    {
        ulong value = 0;
        int shift = 0;
        while (true)
        {
            byte chunk = (byte)r.ReadBits(8);
            value |= (ulong)(chunk & 0x7F) << shift;
            if ((chunk & 0x80) == 0) return value;
            shift += 7;
            if (shift > 63)
                throw new InvalidDataException("EXI Unsigned Integer overflow (>64 bits).");
        }
    }

    /// <summary>
    /// EXI string value, "miss" case: <c>UnsignedInteger(charCount + 2)</c> followed by
    /// each Unicode codepoint as <c>UnsignedInteger</c>. The +2 leaves codes 0 and 1
    /// available for local / global value-table hits.
    /// </summary>
    public static void WriteStringValue(ref BitWriter w, string s)
    {
        int runeCount = 0;
        foreach (var _ in s.EnumerateRunes()) runeCount++;

        WriteUnsignedInteger(ref w, (ulong)(runeCount + 2));
        foreach (var rune in s.EnumerateRunes())
            WriteUnsignedInteger(ref w, (ulong)rune.Value);
    }

    public static string ReadStringValue(ref BitReader r)
    {
        ulong lenPlus2 = ReadUnsignedInteger(ref r);
        if (lenPlus2 < 2)
            throw new NotSupportedException(
                "String value-table hit encountered. Hit handling is not implemented in this prototype.");

        int len = checked((int)(lenPlus2 - 2));
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++)
        {
            int cp = checked((int)ReadUnsignedInteger(ref r));
            sb.Append(char.ConvertFromUtf32(cp));
        }
        return sb.ToString();
    }
}
