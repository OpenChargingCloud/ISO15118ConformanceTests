/*
 * Copyright (c) 2021-2025 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.AppProtocol;
using cloud.charging.open.protocols.ISO15118_2.Generated;
using Vanaheimr.V2G.Simulation.Framing;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

namespace ISO15118ConformanceTests.Simulation.Framing
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

        /// <summary>
        /// A declared length nobody could mean is refused before it is allocated.
        /// </summary>
        /// <remarks>
        /// The length field is the peer's word for how much memory to set aside, and the peer is
        /// whatever answered the socket. Both values matter and for different reasons: 0x7FFFFFFF is
        /// a 2 GiB allocation an 8-byte frame can ask for, and 0xFFFFFFFF is the one that a cast to
        /// a signed int turns into a negative length — which in a reader without this check produces
        /// a silently truncated frame instead of a refusal, and that is worse than a crash.
        /// <para>
        /// A receive limit, not a wire change: the largest frame in any recorded session is 921
        /// bytes. See <see cref="V2GTP.MaximumPayloadBytes"/>.
        /// </para>
        /// </remarks>
        [Test]
        [TestCase(0x7FFFFFFFu)]
        [TestCase(0xFFFFFFFFu)]
        public void ReadRawFrameAsync_RefusesAnAbsurdDeclaredLengthRatherThanAllocatingForIt(uint declared)
        {
            var header = new byte[V2GTP.HeaderSize];
            V2GTP.WriteHeader(header, V2GTP.PayloadType_DinIso2Main, declared);

            using var stream = new MemoryStream(header);

            Assert.That(async () => await V2GTPStream.ReadRawFrameAsync(stream),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contain("accepts at most"));
        }

        /// <summary>And the limit is far above anything the corpus contains.</summary>
        [Test]
        public async Task ReadRawFrameAsync_AcceptsAFrameAtTheLargestRecordedSize()
        {
            var payload = new byte[921 - V2GTP.HeaderSize];   // the -20 AuthorizationReq with a chain

            using var stream = new MemoryStream();
            await V2GTPStream.WriteRawFrameAsync(stream, V2GTP.PayloadType_Iso20Main, payload);
            stream.Position = 0;

            var (frame, _) = await V2GTPStream.ReadRawFrameAsync(stream);
            Assert.That(frame, Has.Length.EqualTo(921));
        }
    }
}
