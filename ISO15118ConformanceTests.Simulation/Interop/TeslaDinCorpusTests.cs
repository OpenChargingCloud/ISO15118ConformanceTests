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

using System.Text.Json;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Framing;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

namespace ISO15118ConformanceTests.Simulation.Interop
{

    /// <summary>
    /// <b>A protocol we cannot speak, framed by a layer that does not need to.</b>
    ///
    /// <para>
    /// <c>Vectors/Din.tesla-session.corpus.json</c> is a real DC charging session — a Tesla Model 3 at
    /// a German charge point <c>DE*PNX*E12345*1</c>, from tux-evse's <c>trace-logs/tesla-3-din.pcap</c>.
    /// It is **DIN 70121**, which this project has no schemas and no codec for. 4,428 V2GTP frames
    /// collapse to <b>101 distinct</b> ones; 98% of the capture is the charge loop repeating itself.
    /// </para>
    ///
    /// <para>
    /// The frames are still ours to test, because <b>V2GTP framing is protocol-independent</b> — the same
    /// structural fact that made the <c>SupportedAppProtocol</c> handshake readable in a DIN capture (see
    /// <see cref="TeslaDinHandshakeTests"/>). Every session guard we have — the 8-byte header, the
    /// declared length, the refusal to trust it — has until now only ever been run over frames this
    /// project produced. Here it runs over frames from two vendors who never heard of us.
    /// </para>
    ///
    /// <para>
    /// <b>How the corpus was read.</b> We cannot name these messages ourselves. V2Gdecoder
    /// (RISE-V2G + EXIficient) named 100 of the 101 and re-encoded each of those to the captured octets
    /// exactly — real-device DIN, round-tripped through an independent codec. The 101st defeated it:
    /// its DIN grammar cannot decode the station's <c>ChargeParameterDiscoveryRes</c>, and because their
    /// fuzzy decoder returns the first grammar that does not throw rather than the one that fits, it came
    /// back as a bare xmldsig <c>SignatureValue</c> — recorded in the corpus as
    /// <c>grammar-miss(SignatureValue)</c> rather than quietly dropped. tux-evse's cbexigen-based
    /// converter reads that frame without trouble (a 10 kW station: 900 V max, 180 V min, 25 A,
    /// isolation still <c>invalid</c> before the cable check), so the frame is sound and the gap is
    /// V2Gdecoder's. Both readings are in the run notes.
    /// </para>
    ///
    /// <para>
    /// What this fixture therefore claims is narrow and worth stating exactly: <b>not</b> that we
    /// understand DIN, only that our framing layer survives a real one — and that the corpus is here,
    /// byte-exact, for the day a DIN codec exists to be judged against it.
    /// </para>
    /// </summary>
    [TestFixture]
    public class TeslaDinCorpusTests
    {

        private static readonly Lazy<Corpus> TheCorpus = new(Load);

        private sealed record Frame(string frame, string direction, int firstIndex, int count,
                                    string message, string verdict);

        private sealed record Corpus(int totalFrames, int distinctFrames, string source,
                                     Dictionary<string, int> verdicts, Frame[] frames);

        private static Corpus Load()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
                                    "Vectors", "Din.tesla-session.corpus.json");
            if (!File.Exists(path))
                path = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..",
                                    "Vectors", "Din.tesla-session.corpus.json");

            return JsonSerializer.Deserialize<Corpus>(File.ReadAllText(path))
                   ?? throw new InvalidDataException("the DIN corpus did not deserialise");
        }


        /// <summary>
        /// The corpus is what it says it is: a whole session, both directions, mostly charge loop.
        /// </summary>
        [Test]
        public void TheCorpusIsAWholeSession()
        {
            var corpus = TheCorpus.Value;

            Assert.Multiple(() =>
            {
                Assert.That(corpus.totalFrames, Is.EqualTo(4428));
                Assert.That(corpus.distinctFrames, Is.EqualTo(101));
                Assert.That(corpus.frames, Has.Length.EqualTo(101));
                Assert.That(corpus.frames.Sum(f => f.count), Is.EqualTo(corpus.totalFrames),
                    "every captured frame must be accounted for by some distinct shape");

                // A DC session, start to finish: the handshake, setup, discovery, payment selection,
                // authorisation, parameters, cable check, pre-charge, power delivery, the loop, stop.
                var messages = corpus.frames.Select(f => f.message).ToHashSet();
                Assert.That(messages, Does.Contain("supportedAppProtocolReq"));
                Assert.That(messages, Does.Contain("SessionSetupReq"));
                Assert.That(messages, Does.Contain("CableCheckReq"));
                Assert.That(messages, Does.Contain("CurrentDemandReq"));
                Assert.That(messages, Does.Contain("SessionStopRes"));

                Assert.That(corpus.frames.Where(f => f.message == "CurrentDemandReq").Sum(f => f.count),
                    Is.EqualTo(1816), "the charge loop, as captured");
            });
        }


        /// <summary>
        /// Every frame parses as V2GTP, and the header's own account of itself holds: the version pair,
        /// a mainstream EXI payload type, and a declared length that is exactly what follows.
        /// </summary>
        [Test]
        public void EveryFrameIsWellFormedV2GTP()
        {
            foreach (var entry in TheCorpus.Value.frames)
            {
                var bytes = Convert.FromHexString(entry.frame);

                Assert.That(bytes, Has.Length.GreaterThan(V2GTP.HeaderSize),
                    $"{entry.message}: a frame must carry a payload");
                Assert.That(V2GTP.TryReadHeader(bytes, out var payloadType, out var payloadLength), Is.True,
                    $"{entry.message} @{entry.direction}[{entry.firstIndex}]: header must parse");
                Assert.That(payloadLength, Is.EqualTo((uint)(bytes.Length - V2GTP.HeaderSize)),
                    $"{entry.message}: the declared length must be the length that follows");

                // DIN and ISO 15118-2 share the mainstream payload type; the handshake has its own.
                Assert.That(payloadType,
                    Is.EqualTo(V2GTP.PayloadType_DinIso2Main).Or.EqualTo(V2GTP.PayloadType_AppProtocol),
                    $"{entry.message}: unexpected payload type 0x{payloadType:x4}");
            }
        }


        /// <summary>
        /// The framer against the real thing: concatenate the capture's distinct frames into one stream
        /// and read them back one at a time. This is what a session peer actually does — the boundary
        /// between two frames is never signalled, only computed from the previous header, so a single
        /// off-by-one in the length handling desynchronises everything after it.
        /// </summary>
        [Test]
        public async Task TheFramerWalksARealSessionStreamWithoutLosingItsPlace()
        {
            var corpus = TheCorpus.Value;
            var expected = corpus.frames.Select(f => Convert.FromHexString(f.frame)).ToArray();

            using var stream = new MemoryStream(expected.SelectMany(f => f).ToArray());

            for (var i = 0; i < expected.Length; i++)
            {
                var (frame, payloadType) = await V2GTPStream.ReadRawFrameAsync(stream);

                Assert.That(frame, Is.EqualTo(expected[i]),
                    $"frame {i} ({corpus.frames[i].message}) came back changed or misaligned");
                Assert.That(payloadType,
                    Is.EqualTo(V2GTP.PayloadType_DinIso2Main).Or.EqualTo(V2GTP.PayloadType_AppProtocol));
            }

            Assert.That(stream.Position, Is.EqualTo(stream.Length),
                "the stream must be consumed exactly — nothing left over, nothing over-read");
        }


        /// <summary>
        /// The one frame V2Gdecoder could not name is recorded as such, not silently dropped. If a DIN
        /// codec ever lands here, this is the frame to point it at first.
        /// </summary>
        [Test]
        public void TheFrameThatDefeatedTheOracleIsStillInTheCorpus()
        {
            var missed = TheCorpus.Value.frames.Where(f => f.message.StartsWith("grammar-miss")).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(missed, Has.Length.EqualTo(1),
                    "exactly one frame defeated V2Gdecoder's DIN grammar");
                Assert.That(missed[0].direction, Is.EqualTo("61341->49153"),
                    "it is the station's answer, not the car's request");
                Assert.That(TheCorpus.Value.verdicts["ok"], Is.EqualTo(100),
                    "the other 100 re-encoded to the captured octets exactly");
            });
        }

    }

}
