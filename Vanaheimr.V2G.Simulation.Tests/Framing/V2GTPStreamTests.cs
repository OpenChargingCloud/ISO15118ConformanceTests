using NUnit.Framework;

using Vanaheimr.V2G.AppProtocol;
using Vanaheimr.V2G.Iso15118_2.Generated;
using Vanaheimr.V2G.Simulation.Framing;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.Tests.Framing
{
    /// <summary>
    /// Unit tests for <see cref="V2GTPStream"/> against a plain <see cref="MemoryStream"/> — no sockets.
    /// Covers round-tripping a frame for two different message sets (proving dispatch actually resolves
    /// the payload type rather than assuming one codec) and the malformed-wire error paths a real TCP
    /// peer can trigger: a connection closed mid-header, and one closed mid-payload.
    /// </summary>
    [TestFixture]
    public class V2GTPStreamTests
    {
        [Test]
        public async Task RoundtripsAppProtocolFrame_ViaRawFraming()
        {
            // SAP shares payload id 0x8001 with the -2 messages and is disambiguated by session phase, not
            // payload type (a live Josev interop run caught the earlier distinct 0x8000 as a wire bug), so it
            // is framed/decoded through the raw path rather than the payload-type dispatcher.
            var req = new SupportedAppProtocolReq(new[]
            {
                new AppProtocolEntry("urn:iso:15118:2:2013:MsgDef", 1, 0, SchemaID: 1, Priority: 1),
            });
            var payload = new byte[128];
            Assert.That(SupportedAppProtocolCodec.TryEncodeRequest(req, payload, out int n), Is.True);

            using var stream = new MemoryStream();
            await V2GTPStream.WriteRawFrameAsync(stream, V2GTP.PayloadType_AppProtocol, payload.AsMemory(0, n));
            stream.Position = 0;

            var (frame, payloadType) = await V2GTPStream.ReadRawFrameAsync(stream);
            Assert.That(payloadType, Is.EqualTo((ushort) 0x8001));
            var message = SupportedAppProtocolCodec.DecodeAny(frame.AsSpan(V2GTP.HeaderSize), out _);
            Assert.That(message, Is.InstanceOf<SupportedAppProtocolReq>());
        }

        [Test]
        public async Task RoundtripsIso15118_2Frame()
        {
            var msg = new V2G_Message(
                new MessageHeaderType(SessionID: new byte[8], Notification: null, Signature: null),
                new BodyType(new SessionSetupReqType(EVCCID: new byte[] { 0xAB, 0xCD, 0xEF, 0x01, 0x02, 0x03 })));
            var payload = new byte[512];
            Assert.That(msg.TryEncode(payload, out int n), Is.True);

            using var stream = new MemoryStream();
            await V2GTPStream.WriteFrameAsync(stream, MessageSet.Iso15118_2, payload.AsMemory(0, n));
            stream.Position = 0;

            var (set, message) = await V2GTPStream.ReadFrameAsync(stream);
            Assert.That(set, Is.EqualTo(MessageSet.Iso15118_2));
            Assert.That(message, Is.InstanceOf<V2G_Message>());
        }

        [Test]
        public void ReadFrameAsync_ThrowsOnConnectionClosedMidHeader()
        {
            using var stream = new MemoryStream(new byte[] { 0x02, 0xFE, 0x80 }); // 3 of 8 header bytes
            Assert.That(async () => await V2GTPStream.ReadFrameAsync(stream),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contain("header"));
        }

        [Test]
        public async Task ReadFrameAsync_ThrowsOnConnectionClosedMidPayload()
        {
            var payload = new byte[] { 0x80, 0x01, 0x02, 0x03 };
            using var full = new MemoryStream();
            await V2GTPStream.WriteFrameAsync(full, MessageSet.AppProtocol, payload);

            // Truncate to the header plus half the declared payload.
            using var truncated = new MemoryStream(full.ToArray().AsSpan(0, V2GTP.HeaderSize + 2).ToArray());
            Assert.That(async () => await V2GTPStream.ReadFrameAsync(truncated),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contain("payload"));
        }

        [Test]
        public void ReadFrameAsync_ThrowsOnBadHeader()
        {
            using var stream = new MemoryStream(new byte[] { 0x02, 0xFE, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00 });
            Assert.That(async () => await V2GTPStream.ReadFrameAsync(stream),
                Throws.InstanceOf<InvalidDataException>());
        }
    }
}
