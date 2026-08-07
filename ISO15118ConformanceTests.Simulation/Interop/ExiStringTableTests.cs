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

using cloud.charging.open.protocols.ISO15118_2.Generated;

namespace ISO15118ConformanceTests.Simulation.Interop
{

    /// <summary>
    /// <b>The EXI value partition: a repeated string, encoded two different ways.</b>
    ///
    /// <para>
    /// EXI keeps a string table. When a value is written, §7.3.3 has the encoder look in the local
    /// value partition (keyed by the qname it appears under) and then the global one; a hit is written
    /// as a compact identifier, a miss as a literal that then joins both partitions. Our encoder is
    /// <b>miss-only</b> — always the literal — and that is a deliberate, documented choice, not an
    /// oversight: see the remarks on <c>ExiPrimitives.ReadStringValue</c>. cbV2G never emits hits
    /// either, every checked-in vector is cbV2G's output, and an encoder that began emitting them
    /// would invalidate all of them at once. The decoder, by the same note, is expected to accept
    /// what a conforming peer may send.
    /// </para>
    ///
    /// <para>
    /// <b>What is new here is the evidence.</b> Until now both halves of that decision rested on
    /// design intent: no foreign encoder had ever handed us a partition hit to read. Running our
    /// recorded frames through V2Gdecoder (RISE-V2G + EXIficient — <c>tools/interop-v2gdecoder/</c>,
    /// run of 2026-08-07) put 183 of 186 frames back byte-exact; two of the three that differed are
    /// the signed PnC requests below, and they are the first genuine hits we have. Both differ by
    /// exactly 35 bytes, and 35 is the length of <c>http://www.w3.org/TR/canonical-exi/</c> — the only
    /// value in the document that occurs twice under the same attribute name <c>Algorithm</c>:
    /// </para>
    ///
    /// <code>
    ///   CanonicalizationMethod/@Algorithm = http://www.w3.org/TR/canonical-exi/          (1st)
    ///   SignatureMethod/@Algorithm        = http://www.w3.org/2001/04/xmldsig-more#…
    ///   Transform/@Algorithm              = http://www.w3.org/TR/canonical-exi/          (2nd — a hit)
    ///   DigestMethod/@Algorithm           = http://www.w3.org/2001/04/xmlenc#sha256
    /// </code>
    ///
    /// <para>
    /// Confirmed by substitution, not by inference: make that second value unique while keeping its
    /// length, and their encoder produces <b>307 bytes — ours exactly</b>. Leave it repeated and they
    /// produce 272.
    /// </para>
    ///
    /// <para>
    /// <b>The corpus has a blind spot, and that is the finding.</b> Not one of the 39 cbV2G-generated
    /// <c>Iso15118_2</c> vectors repeats an <c>Algorithm</c> value — the signed ones carry no
    /// <c>&lt;Transforms&gt;</c> block at all. All 39 round-trip through EXIficient byte-exact, and
    /// that agreement means less than it looks: the vectors never put a value in a partition twice, so
    /// they cannot tell a miss-only encoder from a conforming one, and they never produced a hit for
    /// the decoder to read. The session traces do, which is why the divergence appears there and
    /// nowhere else.
    /// </para>
    ///
    /// <para>
    /// <b>Which way the risk runs.</b> Writing literals costs bytes and nothing else — a conformant
    /// decoder reads them, as EXIficient did for all 186 frames. The exposure is the other direction:
    /// any peer whose encoder does use the partitions <i>will</i> send us the compact form, and we have
    /// to read it. EXIficient does, so Josev and RISE-V2G do; whether eVDriveFlow's OpenEXI does is
    /// untested — a separate implementation, not the same codec. Our decoder was written for that and
    /// had
    /// never been shown a real one. Now it has —
    /// <see cref="TheirPartitionCompressedFrames_DecodeToOurExactBytes"/> reads two, from an encoder
    /// that is not ours, and lands back on our own octets.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ExiStringTableTests
    {

        // A matched pair per message: the same infoset, encoded by us and re-encoded by EXIficient from
        // its own decode of ours. From Session.iso2-ac-pnc.trace.json, exchanges 5 and 9.

        /// <summary>Our AuthorizationReq — the repeated URI written out a second time. 307 B.</summary>
        private const string OurAuthorizationReq =
            "8098020282c3034383c4044a895a1d1d1c0e8bcbddddddcb9dcccb9bdc99cbd5148bd8d85b9bdb9a58d85b0b5" +
            "95e1a4bd0d5a1d1d1c0e8bcbddddddcb9dcccb9bdc99cbcc8c0c0c4bcc0d0bde1b5b191cda59cb5b5bdc9948d" +
            "958d91cd84b5cda184c8d4d910311b4b218812b43a3a381d1797bbbbbb973b999737b93397aa2917b1b0b737b" +
            "734b1b0b616b2bc3497a429687474703a2f2f7777772e77332e6f72672f323030312f30342f786d6c656e6323" +
            "736861323536420479581ad0399d6f1aabcead4e055620210222ac24f6a4fe034445751b1497c971280590e00" +
            "7cc1360ed6d7019c29dead082d4876c77177f188cb50611ffcf21ea4052d5915f07125bfb4a0a31104824e852" +
            "e8777c9bd122be513a18ca8406e3a8af88000569643102040424446484a4c4e50525456585a5c5e0";

        /// <summary>EXIficient's re-encoding of the same message — the second URI as a partition hit. 272 B.</summary>
        private const string TheirAuthorizationReq =
            "8098020282c3034383c4044a895a1d1d1c0e8bcbddddddcb9dcccb9bdc99cbd5148bd8d85b9bdb9a58d85b0b5" +
            "95e1a4bd0d5a1d1d1c0e8bcbddddddcb9dcccb9bdc99cbcc8c0c0c4bcc0d0bde1b5b191cda59cb5b5bdc9948d" +
            "958d91cd84b5cda184c8d4d910311b4b2188001214b43a3a381d1797bbbbbb973b999737b93397991818189798" +
            "1a17bc36b632b73191b9b430991a9b21023cac0d681cceb78d55e756a702ab1010811156127b527f01a222ba8" +
            "d8a4be4b89402c87003e609b076b6b80ce14ef568416a43b63b8bbf8c465a8308ffe790f520296ac8af83892d" +
            "fda505188824127429743bbe4de8915f289d0c65420371d457c40002b4b218810202122232425262728292a2b" +
            "2c2d2e2f0";

        /// <summary>Our MeteringReceiptReq. 317 B.</summary>
        private const string OurMeteringReceiptReq =
            "8098020282c3034383c4044a895a1d1d1c0e8bcbddddddcb9dcccb9bdc99cbd5148bd8d85b9bdb9a58d85b0b5" +
            "95e1a4bd0d5a1d1d1c0e8bcbddddddcb9dcccb9bdc99cbcc8c0c0c4bcc0d0bde1b5b191cda59cb5b5bdc9948d" +
            "958d91cd84b5cda184c8d4d910311b4b219012b43a3a381d1797bbbbbb973b999737b93397aa2917b1b0b737b" +
            "734b1b0b616b2bc3497a429687474703a2f2f7777772e77332e6f72672f323030312f30342f786d6c656e6323" +
            "736861323536420371 58aa93ccb3e030f89305db07163bd18c55fb69f7596ee0d1932dd0c3840a31281b5dae" +
            "2275e1a3f7d092a000477f2343457d176f25befee7fa1dc35423faf95ad5bb45209fca2cc00186efc40c6d1ff" +
            "a7a630ab8d69030b14f4fc61e39e6f01f883c05696432020282c3034383c4044000021590538a934c41700244" +
            "0796b6503000";

        /// <summary>EXIficient's re-encoding of the same MeteringReceiptReq. 282 B.</summary>
        private const string TheirMeteringReceiptReq =
            "8098020282c3034383c4044a895a1d1d1c0e8bcbddddddcb9dcccb9bdc99cbd5148bd8d85b9bdb9a58d85b0b5" +
            "95e1a4bd0d5a1d1d1c0e8bcbddddddcb9dcccb9bdc99cbcc8c0c0c4bcc0d0bde1b5b191cda59cb5b5bdc9948d" +
            "958d91cd84b5cda184c8d4d910311b4b2190001214b43a3a381d1797bbbbbb973b999737b93397991818189798" +
            "1a17bc36b632b73191b9b430991a9b2101b8ac5549e659f0187c4982ed838b1de8c62afdb4fbacb77068c996e" +
            "861c20518940daed7113af0d1fbe849500023bf91a1a2be8bb792df7f73fd0ee1aa11fd7cad6adda2904fe516" +
            "6000c377e206368ffd3d31855c6b481858a7a7e30f1cf3780fc41e02b4b2190101416181a1c1e202200001 0ac" +
            "829c549a620b8012203cb5b281800";

        /// <summary>The value that occurs twice, and therefore the whole of the 35-byte difference.</summary>
        private const string RepeatedAlgorithm = "http://www.w3.org/TR/canonical-exi/";


        /// <summary>
        /// The one that matters: an EXIficient-encoded frame using the compact identifier must decode to
        /// the very same message we encoded verbosely. Checked by re-encoding what we read and demanding
        /// our own octets back — a stricter test than comparing models, and it needs no equality on
        /// byte-array members.
        /// </summary>
        [Test]
        [TestCase(TheirAuthorizationReq,   OurAuthorizationReq,   TestName = "AuthorizationReq")]
        [TestCase(TheirMeteringReceiptReq, OurMeteringReceiptReq, TestName = "MeteringReceiptReq")]
        public void TheirPartitionCompressedFrames_DecodeToOurExactBytes(string theirHex, string ourHex)
        {
            var theirs = Hex(theirHex);
            var ours   = Hex(ourHex);

            var message = (V2G_Message) Iso2Codec.DecodeAny(theirs, out _);

            var buffer = new byte[8192];
            Assert.That(message.TryEncode(buffer, out var n), Is.True, "our re-encode must succeed");
            Assert.That(buffer.AsSpan(0, n).ToArray(), Is.EqualTo(ours),
                "reading the compact form and writing it back must land on our own octets");
        }


        /// <summary>
        /// The other direction is already covered by the corpus, but the cost is worth stating: the
        /// literal we write instead of an identifier is the entire difference, to the byte.
        /// </summary>
        [Test]
        [TestCase(OurAuthorizationReq,   TheirAuthorizationReq)]
        [TestCase(OurMeteringReceiptReq, TheirMeteringReceiptReq)]
        public void OurLiteralCostsExactlyTheRepeatedString(string ourHex, string theirHex)
        {
            Assert.That(Hex(ourHex).Length - Hex(theirHex).Length,
                Is.EqualTo(RepeatedAlgorithm.Length),
                $"we spend {RepeatedAlgorithm.Length} bytes re-writing '{RepeatedAlgorithm}'");
        }


        /// <summary>Our own encoding is stable: what we wrote, we read and write again unchanged.</summary>
        [Test]
        [TestCase(OurAuthorizationReq)]
        [TestCase(OurMeteringReceiptReq)]
        public void OurOwnFramesRoundTrip(string ourHex)
        {
            var ours    = Hex(ourHex);
            var message = (V2G_Message) Iso2Codec.DecodeAny(ours, out _);

            var buffer = new byte[8192];
            Assert.That(message.TryEncode(buffer, out var n), Is.True);
            Assert.That(buffer.AsSpan(0, n).ToArray(), Is.EqualTo(ours));
        }


        private static byte[] Hex(string text)
            => Convert.FromHexString(text.Replace(" ", ""));

    }

}
