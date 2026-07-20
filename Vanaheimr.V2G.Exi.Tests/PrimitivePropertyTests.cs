using CsCheck;
using NUnit.Framework;
using Vanaheimr.V2G.Exi;

namespace Vanaheimr.V2G.Exi.Tests
{
    /// <summary>
    /// Property-based round-trip tests (CsCheck): for a wide range of random inputs,
    /// <c>encode</c> then <c>decode</c> must return the original value. Failures shrink to a
    /// minimal reproducing case with a reproducible seed.
    /// </summary>
    [TestFixture]
    public class PrimitivePropertyTests
    {
        // Valid Unicode scalar values: 0..0x10FFFF excluding the surrogate range D800..DFFF.
        private static readonly Gen<string> GenUnicodeString =
            Gen.Int[0, 0x10FFFF - 0x800]
               .Select(i => i < 0xD800 ? i : i + 0x800)
               .List[0, 40]
               .Select(cps => string.Concat(cps.ConvertAll(char.ConvertFromUtf32)));

        [Test]
        public void UnsignedInteger_Roundtrips()
        {
            Gen.ULong.Sample(value =>
            {
                var buf = new byte[16];
                var w = new BitWriter(buf);
                ExiPrimitives.WriteUnsignedInteger(ref w, value);
                w.AlignToByte();
                var r = new BitReader(buf.AsSpan(0, w.BytesWritten));
                return ExiPrimitives.ReadUnsignedInteger(ref r) == value;
            });
        }

        [Test]
        public void SignedInteger_Roundtrips_FullRange()
        {
            Gen.Long.Sample(value =>
            {
                var buf = new byte[16];
                var w = new BitWriter(buf);
                ExiPrimitives.WriteSignedInteger(ref w, value);
                w.AlignToByte();
                var r = new BitReader(buf.AsSpan(0, w.BytesWritten));
                return ExiPrimitives.ReadSignedInteger(ref r) == value;
            });
        }

        [Test]
        public void Binary_Roundtrips()
        {
            Gen.Byte.Array[0, 300].Sample(data =>
            {
                var buf = new byte[512];
                var w = new BitWriter(buf);
                ExiPrimitives.WriteBinary(ref w, data);
                w.AlignToByte();
                var r = new BitReader(buf.AsSpan(0, w.BytesWritten));
                return ExiPrimitives.ReadBinary(ref r).AsSpan().SequenceEqual(data);
            });
        }

        [Test]
        public void StringValue_Roundtrips_IncludingNonBmp()
        {
            GenUnicodeString.Sample(s =>
            {
                var buf = new byte[4096];
                var w = new BitWriter(buf);
                ExiPrimitives.WriteStringValue(ref w, s);
                w.AlignToByte();
                var r = new BitReader(buf.AsSpan(0, w.BytesWritten));
                return ExiPrimitives.ReadStringValue(ref r) == s;
            });
        }

        [Test]
        public void ValueTable_SequenceRoundtrips_WithHitsAndMisses()
        {
            // Small key- and value-pools so hits (local and global) occur frequently.
            var genValue = Gen.OneOfConst("a", "bb", "ccc", "urn:x", "urn:y", "urn:z");
            var genPair  = Gen.Select(Gen.Int[0, 3], genValue, (k, v) => (k, v));

            genPair.List[0, 50].Sample(seq =>
            {
                var encTable = new ExiStringTable();
                var buf = new byte[16384];
                var w = new BitWriter(buf);
                foreach (var (k, v) in seq)
                    encTable.WriteStringValue(ref w, k, v);
                w.AlignToByte();

                var decTable = new ExiStringTable();
                var r = new BitReader(buf.AsSpan(0, w.BytesWritten));
                foreach (var (k, v) in seq)
                {
                    if (decTable.ReadStringValue(ref r, k) != v)
                        return false;
                }
                return true;
            });
        }
    }
}
