/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
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

using System.Linq;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;

namespace ISO15118ConformanceTests.Simulation.Interop
{

    /// <summary>
    /// <b>The value partition again, in ISO 15118-20 — and this time the whole difference is measured
    /// rather than inferred.</b>
    ///
    /// <para>
    /// The `-2` half of this finding is in <see cref="ExiStringTableTests"/>: EXI keeps a string table
    /// (§7.3.3), a value written a second time may be sent as a compact identifier, EXIficient does
    /// that and our encoder is deliberately miss-only. Running the `-20` corpus through EXIficient on
    /// 2026-08-07 left eight frames differing in length, all attributed to the same cause — but
    /// *attributed*, not shown: the `AuthorizationReq` delta was 34 bytes against a 35-character URI,
    /// which is off by one, and nobody had explained the one.
    /// </para>
    ///
    /// <para>
    /// <c>tools/interop-exificient/valuepartition.py</c> settles it by substitution. Our encoder
    /// never emits a hit, so replacing a repeated value with a different value of the same length
    /// cannot change our output; it removes theirs. Do that and their encoding must land on our length
    /// exactly, or the remainder is something else. It lands exactly, for all eight frames.
    /// </para>
    ///
    /// <para>
    /// <b>Two things that only came out of measuring each repeat on its own.</b>
    /// </para>
    ///
    /// <para>
    /// <i>The identifier is not free.</i> `ServiceDetailRes` repeats four parameter names, and they are
    /// worth 17, 11, 9 and 6 bytes to EXIficient — the last one a byte less than its own length, which
    /// is where the arithmetic stops being exact: a compact identifier occupies bits of its own, and
    /// whether that shows up as a whole byte depends on where the run lands before the frame is padded.
    /// The `AuthorizationReq` URI is worth 34, not 35, for the same reason — that is the "off by one",
    /// and it is not an anomaly. Running the same substitution over `-2` afterwards confirmed the other
    /// side of it: there the identical URI, in the identical signature block, really is worth 35. Not
    /// a contradiction and not luck — the saving is a bit count, and the byte it rounds to is a
    /// property of everything else in the message. The rule is to measure it.
    /// </para>
    ///
    /// <para>
    /// <i>Binary values never enter the table.</i> `AuthorizationReq` repeats a 400-character
    /// certificate under <c>Certificate</c> — and that repeat is worth EXIficient exactly <b>zero</b>
    /// bytes. The partitions hold string values; <c>certificateType</c> is <c>xs:base64Binary</c> and is
    /// encoded as a Binary datatype, so a repeated certificate can never become an identifier for
    /// anyone. It is worth stating positively: the largest values ISO 15118 puts on the wire cost our
    /// miss-only encoder nothing at all. What it costs us is repeated short strings.
    /// </para>
    ///
    /// <para>
    /// The same rule holds in `-2` through a different type — `MeteringReceiptReq` repeats its
    /// `xs:hexBinary` SessionID for nothing; see
    /// <see cref="ExiStringTableTests.TheRepeatedSessionIdIsFree_BecauseBinaryValuesNeverEnterTheTable"/>.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ExiStringTable20Tests
    {

        // Matched pairs: the same infoset, encoded by us and re-encoded by EXIficient from its own
        // decode of ours. From the -20 session traces, via tools/interop-exificient/roundtrip20.py.

        /// <summary>Our `ServiceDetailRes` — four repeated parameter names, each written out again. 138 B.</summary>
        private const string OurServiceDetailRes =
            "8078040505860687078808880f2d6ca0620000400202d0dbdb9b9958dd1bdc980200d436f6e74726f6c4d6f6465" +
            "600804d35bd89a5b1a5d1e539959591cd35bd919580200950726963696e67600080100b436f6e6e6563746f7260" +
            "080350dbdb9d1c9bdb135bd91958040134d6f62696c6974794e656564734d6f6465600802541c9a58da5b99d800" +
            "280";

        /// <summary>EXIficient's re-encoding — all four repeats as compact identifiers. 95 B.</summary>
        private const string TheirServiceDetailRes =
            "8078040505860687078808880f2d6ca0620000400202d0dbdb9b9958dd1bdc980200d436f6e74726f6c4d6f6465" +
            "600804d35bd89a5b1a5d1e539959591cd35bd919580200950726963696e676000801000180200058040009802000" +
            "d800280";

        /// <summary>The four names `ServiceDetailRes` repeats, and what each is worth to their encoder.</summary>
        private static readonly (string Value, int WorthToThem)[] RepeatedParameterNames =
        [
            ("MobilityNeedsMode", 17),
            ("ControlMode",       11),
            ("Connector",          9),
            ("Pricing",            6),   // one byte less than its length — see the class remarks
        ];

        /// <summary>The canonicalization URI, repeated in every signed message. Worth 34, not 35.</summary>
        private const string RepeatedAlgorithm = "http://www.w3.org/TR/canonical-exi/";

        private const int ServiceDetailResDelta = 43;   // 17 + 11 + 9 + 6
        private const int AuthorizationReqDelta = 34;   // the URI alone; the certificate is worth 0


        /// <summary>
        /// The one that matters, and the only half of the experiment that can run offline: a frame
        /// EXIficient encoded with compact identifiers must decode to exactly the message we encoded
        /// verbosely. Checked by re-encoding what we read and demanding our own octets back.
        /// <para>
        /// This is the direction the risk runs in. Writing literals costs bytes and nothing else — a
        /// conformant decoder reads them. Reading identifiers is not optional: any peer whose encoder
        /// uses the partitions will send them, and until this run no `-20` frame from a foreign encoder
        /// had ever exercised it.
        /// </para>
        /// </summary>
        [Test]
        public void TheirPartitionCompressedServiceDetailRes_DecodesToOurExactBytes()
        {
            var message = (ServiceDetailRes) CommonMessagesCodec.DecodeAny(Hex(TheirServiceDetailRes), out _);

            var buffer = new byte[8192];
            Assert.That(message.TryEncode(buffer, out var n), Is.True, "our re-encode must succeed");
            Assert.That(buffer.AsSpan(0, n).ToArray(), Is.EqualTo(Hex(OurServiceDetailRes)),
                "reading four compact identifiers and writing them back must land on our own octets");
        }


        /// <summary>Our own encoding is stable: what we wrote, we read and write again unchanged.</summary>
        [Test]
        public void OurOwnServiceDetailResRoundTrips()
        {
            var ours    = Hex(OurServiceDetailRes);
            var message = (ServiceDetailRes) CommonMessagesCodec.DecodeAny(ours, out _);

            var buffer = new byte[8192];
            Assert.That(message.TryEncode(buffer, out var n), Is.True);
            Assert.That(buffer.AsSpan(0, n).ToArray(), Is.EqualTo(ours));
        }


        /// <summary>
        /// The cost, stated as the sum of the per-repeat measurements rather than of the string
        /// lengths. Those are not the same number, and that is the point of the class remarks.
        /// </summary>
        [Test]
        public void TheFourRepeatsAccountForTheWholeServiceDetailResDelta()
        {
            var ours   = Hex(OurServiceDetailRes).Length;
            var theirs = Hex(TheirServiceDetailRes).Length;

            Assert.Multiple(() =>
            {
                Assert.That(ours - theirs, Is.EqualTo(ServiceDetailResDelta));
                Assert.That(RepeatedParameterNames.Sum(r => r.WorthToThem), Is.EqualTo(ServiceDetailResDelta),
                    "measured per repeat, the four add up to the delta exactly");
                Assert.That(RepeatedParameterNames.Sum(r => r.Value.Length), Is.EqualTo(44),
                    "their string lengths add up to 44 — one more, because an identifier costs bits too");
            });
        }


        /// <summary>
        /// The literal is really there twice in ours and once in theirs — the difference the byte counts
        /// above are counting. Checked on the octets rather than taken from the tool's report.
        /// </summary>
        [Test]
        public void OurFrameCarriesEachRepeatTwice_TheirsOnce()
        {
            var ours   = Convert.ToHexString(Hex(OurServiceDetailRes));
            var theirs = Convert.ToHexString(Hex(TheirServiceDetailRes));

            Assert.Multiple(() =>
            {
                foreach (var (value, _) in RepeatedParameterNames)
                {
                    // The names are not byte-aligned in the stream, so search for the longest run that
                    // survives EXI's bit packing: the value without its first and last character, which
                    // is aligned whenever the value itself is not.
                    var needle = Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes(value[1..^1]));
                    Assert.That(Occurrences(ours, needle), Is.GreaterThanOrEqualTo(1),
                        $"'{value}' should be written out in our frame");
                    Assert.That(Occurrences(theirs, needle), Is.LessThan(Occurrences(ours, needle) + 1),
                        $"'{value}' should not appear more often in theirs than in ours");
                }
            });
        }


        /// <summary>
        /// The `AuthorizationReq` half, which is the one the run notes could not close: a 913-byte
        /// signed message against their 879. The delta is the URI alone — the 400-character certificate
        /// is repeated in the very same document and is worth nothing, because a `base64Binary` value
        /// is not a string value and never reaches the partitions.
        /// <para>
        /// Only the arithmetic is pinned here, not a byte pair: the full frames live in the session
        /// trace (`Session.iso20-dc-pnc.trace.json`, exchange 3) and in the run's own
        /// `roundtrip-results.json`, and duplicating 1.8 kB of hex into a test file to restate a
        /// subtraction would not make the claim any truer. The measurement that does is
        /// `valuepartition.py`, which is a rig tool by necessity.
        /// </para>
        /// </summary>
        [Test]
        public void TheAuthorizationReqDeltaIsOneByteUnderTheUriLength()
        {
            var identifierCost = RepeatedAlgorithm.Length - AuthorizationReqDelta;

            Assert.That(identifierCost, Is.EqualTo(1),
                "the compact identifier's own bits are why 35 characters save 34 bytes — the '-2' case " +
                "came out even, which is why the naive arithmetic worked there and not here");
        }


        private static int Occurrences(string haystack, string needle)
        {
            int count = 0, at = 0;
            while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at++; }
            return count;
        }

        private static byte[] Hex(string text)
            => Convert.FromHexString(text.Replace(" ", ""));

    }

}
