using NUnit.Framework;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Exi.Tests
{
    [TestFixture]
    public class V2GTPFrameTests
    {
        [Test]
        public void Header_Roundtrip()
        {
            // Use a heap byte[] (not stackalloc) so the buffer isn't a ref struct;
            // ref structs can't be captured by Assert.Multiple's closure.
            var buf = new byte[V2GTP.HeaderSize];
            V2GTP.WriteHeader(buf, V2GTP.PayloadType_AppProtocol, 42);   // 0x8001 (SAP / -2 EXI payload id)

            // Compare the wire bytes in one shot. This is both clearer than 8
            // individual asserts and dodges the ref-struct-in-lambda issue entirely.
            var expected = new byte[] { 0x01, 0xFE, 0x80, 0x01, 0x00, 0x00, 0x00, 0x2A };
            Assert.That(buf, Is.EqualTo(expected));

            Assert.That(V2GTP.TryReadHeader(buf, out var pt, out var plen), Is.True);
            Assert.That(pt,   Is.EqualTo(V2GTP.PayloadType_AppProtocol));
            Assert.That(plen, Is.EqualTo(42u));
        }

        [Test]
        public void Header_Rejects_WrongVersion()
        {
            var buf = new byte[8];
            buf[0] = 0x02; buf[1] = 0xFE; // bad version
            Assert.That(V2GTP.TryReadHeader(buf, out _, out _), Is.False);
        }

        [Test]
        public void Header_Rejects_TooShortBuffer()
        {
            var buf = new byte[7];
            Assert.That(V2GTP.TryReadHeader(buf, out _, out _), Is.False);
        }
    }
}
