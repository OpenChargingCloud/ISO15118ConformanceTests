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

using ISO15118ConformanceTests.Simulation.Traces;

namespace ISO15118ConformanceTests.Simulation.Interop;

/// <summary>
/// The recorder that turns an interop run into artifacts, checked without an interop run.
/// </summary>
/// <remarks>
/// <para>
/// A recorder is only ever exercised in the situation it cannot be debugged in: somebody else's stack, a
/// container that will not exist tomorrow, a session that went wrong in a way nobody has seen before. So
/// it is checked here, against a recorded session standing in for the live one — including the case it
/// exists for, where the session ends mid-frame and the strict corpus builder refuses it.
/// </para>
/// <para>
/// Offline and part of the ordinary run: unlike the fixtures beside it, this needs no peer.
/// </para>
/// </remarks>
[TestFixture]
public class InteropRecordingTests
{

    private String directory = default!;

    [SetUp]
    public void MakeADirectory()
    {
        directory = Path.Combine(Path.GetTempPath(), $"v2g-interop-recording-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void RemoveIt()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }


    private static SessionTrace Corpus(String name)
        => SessionTrace.ReadFrom(Path.Combine(TestContext.CurrentContext.TestDirectory,
                                              "Vectors", $"Session.{name}.trace.json"));

    private static String Artifact(IReadOnlyList<String> written, String suffix)
        => written.SingleOrDefault(p => p.EndsWith(suffix, StringComparison.Ordinal))
               ?? throw new AssertionException($"nothing was written ending in '{suffix}': " +
                                               String.Join(", ", written.Select(Path.GetFileName)));


    /// <summary>Drives the recorded exchanges over the tap, as a session would.</summary>
    /// <param name="upTo">How many exchanges to run — fewer than all of them ends the session early.</param>
    /// <param name="truncateLastResponseBy">Bytes of the final response left unread, i.e. a session that
    /// died mid-frame.</param>
    private static void Replay(Stream stream, SessionTrace trace, Int32 upTo, Int32 truncateLastResponseBy = 0)
    {

        for (var i = 0; i < upTo; i++)
        {

            var exchange = trace.Exchanges[i];
            stream.Write(exchange.Request.Bytes);

            var wanted = exchange.Response.Bytes.Length - (i == upTo - 1 ? truncateLastResponseBy : 0);
            var buffer = new Byte[wanted];
            var read   = 0;

            // Three bytes at a time: a stream is allowed to return fewer bytes than asked for, and a
            // recorder that only ever saw whole frames arrive in one read would not have been tested
            // against the way a socket actually behaves.
            while (read < wanted)
            {
                var n = stream.Read(buffer, read, Math.Min(3, wanted - read));
                if (n <= 0) break;
                read += n;
            }

            Assert.That(read, Is.EqualTo(wanted), $"exchange {i}: the stand-in station ran out of bytes");

        }

    }


    [Test]
    public void AWholeSessionComesBackAsTheTraceItWas()
    {

        var trace = Corpus("iso2-ac-eim");

        var recording = new InteropRecording(directory, "check");
        using var station = new TraceReplayStream(trace);

        Replay(recording.Tap(station), trace, trace.Exchanges.Count);

        var written = recording.Save(trace.Protocol, trace.Mode, "recorded by InteropRecordingTests",
                                     weAreTheEvcc: true);

        var rebuilt = SessionTrace.ReadFrom(Artifact(written, "check.trace.json"));

        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.Exchanges,                    Has.Count.EqualTo(trace.Exchanges.Count));
            Assert.That(rebuilt.Exchanges.Select(e => e.Request.Frame),
                        Is.EqualTo(trace.Exchanges.Select(e => e.Request.Frame)));
            Assert.That(rebuilt.Exchanges.Select(e => e.Response.Frame),
                        Is.EqualTo(trace.Exchanges.Select(e => e.Response.Frame)));

            // And the raw octets are the frames concatenated — the artifact that survives even when
            // nothing else can be made of the capture.
            Assert.That(File.ReadAllBytes(Artifact(written, "ev-to-station.bin")),
                        Is.EqualTo(trace.Exchanges.SelectMany(e => e.Request.Bytes).ToArray()));
            Assert.That(File.ReadAllBytes(Artifact(written, "station-to-ev.bin")),
                        Is.EqualTo(trace.Exchanges.SelectMany(e => e.Response.Bytes).ToArray()));
        });

    }


    /// <summary>
    /// Which side we were changes the labels and nothing else — and getting it wrong files the EV's frames
    /// as the station's, which no later reader could detect.
    /// </summary>
    [Test]
    public void RecordingFromTheStationsEndFilesTheDirectionsTheSameWayRound()
    {

        var trace = Corpus("iso2-ac-eim");

        var recording = new InteropRecording(directory, "reverse");
        using var station = new TraceReplayStream(trace);

        // Still driving the EV's side of the wire — but claiming, as the SECC fixture does, that the
        // bytes we sent are the station's.
        Replay(recording.Tap(station), trace, trace.Exchanges.Count);

        var written = recording.Save(trace.Protocol, trace.Mode, "as the station", weAreTheEvcc: false);

        Assert.That(File.ReadAllBytes(Artifact(written, "station-to-ev.bin")),
                    Is.EqualTo(trace.Exchanges.SelectMany(e => e.Request.Bytes).ToArray()),
                    "what this process sent should be filed as station→EV when this process was the station");

    }


    /// <summary>
    /// The case the recorder exists for: a session that ends inside a frame.
    /// </summary>
    /// <remarks>
    /// <see cref="SessionTrace.Build"/> refuses it, correctly — half a session is a broken corpus entry.
    /// If that refusal were allowed to end the recording, the run that actually found something would be
    /// the run that left nothing behind.
    /// </remarks>
    [Test]
    public void ASessionThatDiesMidFrameStillLeavesTheBytesAndSaysWhy()
    {

        var trace = Corpus("iso2-ac-eim");

        var recording = new InteropRecording(directory, "aborted");
        using var station = new TraceReplayStream(trace);

        Replay(recording.Tap(station), trace, upTo: 4, truncateLastResponseBy: 5);

        var written = recording.Save(trace.Protocol, trace.Mode, "aborted", weAreTheEvcc: true);

        Assert.Multiple(() =>
        {
            Assert.That(written.Any(p => p.EndsWith("trace.json", StringComparison.Ordinal)), Is.False,
                        "a truncated session is not a corpus entry");

            var why = File.ReadAllText(Artifact(written, "trace-not-built.txt"));
            Assert.That(why, Does.Contain("truncated").Or.Contain("declares"));

            // The bytes are all there, including the five that never became a frame.
            Assert.That(File.ReadAllBytes(Artifact(written, "station-to-ev.bin")),
                        Has.Length.EqualTo(trace.Exchanges.Take(4).Sum(e => e.Response.Bytes.Length) - 5));

            var log = File.ReadAllText(Artifact(written, "frames.log"));

            // By name, not just by payload type. On a session that stopped in the middle this file is all
            // there is, and "which message was it on" is the only question anybody asks of it.
            Assert.That(log, Does.Contain("[3] PaymentServiceSelectionReq payloadType=0x8001"),
                        "the frames that did arrive are listed, and named");
            Assert.That(log, Does.Contain("trailing byte(s) that are not a complete frame"),
                        "and the tail that did not is the most useful line in the file");

            // The flow report is written from the frames, so an aborted session still has one — and the
            // row where it stopped is the row that says so.
            var flow = File.ReadAllText(Artifact(written, "flow.md"));
            Assert.That(flow, Does.Contain("| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |"));
            Assert.That(flow, Does.Contain("| 3 | PaymentServiceSelectionReq | — (no answer) |"),
                        "the response that never completed is shown as missing rather than omitted");

            // The handshake's own code, which the hand-written SAP codec calls 'Code' rather than
            // 'ResponseCode' — and which is the first thing an interop session can fail on.
            Assert.That(flow, Does.Contain("| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_"));
        });

    }


    [Test]
    public void TheTolerantSplitterStopsAtTheFirstThingThatIsNotAFrame()
    {

        var trace  = Corpus("iso2-ac-eim");
        var frames = trace.Exchanges.Take(3).Select(e => e.Response.Bytes).ToArray();

        var whole = frames.SelectMany(f => f).ToArray();
        var (parsed, leftover) = InteropRecording.SplitAsFarAsPossible(whole);

        Assert.Multiple(() =>
        {
            Assert.That(parsed,   Has.Count.EqualTo(3));
            Assert.That(leftover, Is.Empty);
        });

        // A frame whose header says it is longer than what follows: kept as leftover, not guessed at.
        var short_ = whole[..^4];
        var (parsedShort, leftoverShort) = InteropRecording.SplitAsFarAsPossible(short_);

        Assert.Multiple(() =>
        {
            Assert.That(parsedShort,   Has.Count.EqualTo(2));
            Assert.That(leftoverShort, Has.Length.EqualTo(frames[2].Length - 4));
        });

        // Something that is not V2GTP at all: nothing is parsed and nothing is lost.
        var (none, all) = InteropRecording.SplitAsFarAsPossible([0x00, 0x01, 0x02, 0x03, 0x04]);

        Assert.Multiple(() =>
        {
            Assert.That(none, Is.Empty);
            Assert.That(all,  Has.Length.EqualTo(5));
        });

    }


    /// <summary>
    /// A length field is a peer's claim, and this splitter runs over bytes a counterparty produced.
    /// </summary>
    [Test]
    public void AnAbsurdDeclaredLengthIsRefusedRatherThanTurnedIntoANegativeIndex()
    {

        // 0xFFFFFFFF payload bytes: as a signed int that is -1, and slicing with it would throw somewhere
        // far away from the cause — or, in a splitter written slightly differently, index backwards.
        Byte[] hostile = [0x01, 0xFE, 0x80, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

        var (frames, leftover) = InteropRecording.SplitAsFarAsPossible(hostile);

        Assert.Multiple(() =>
        {
            Assert.That(frames,   Is.Empty);
            Assert.That(leftover, Has.Length.EqualTo(hostile.Length));
        });

    }

}
