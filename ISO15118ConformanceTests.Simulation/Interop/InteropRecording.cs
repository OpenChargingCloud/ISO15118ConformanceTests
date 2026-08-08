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

using System.Text;

using ISO15118ConformanceTests.Simulation.Traces;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

namespace ISO15118ConformanceTests.Simulation.Interop;

/// <summary>
/// Turns an interop run into artifacts: the raw octets of both directions, a readable frame log, and —
/// when the session was well-formed enough — a <see cref="SessionTrace"/> the whole corpus machinery
/// already understands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a run has to leave something behind.</b> <c>docs/counterparties.md</c> puts it as "every session
/// against a counterparty should come back as a trace, not as a memory". A live run against somebody
/// else's stack is the only oracle we have that is outside our own head, it costs real setup to arrange,
/// and it is not repeatable on demand — the peer moves on, the container image changes, the veth pair is
/// gone tomorrow. A run that produced only a green tick has spent all of that and kept none of it. A run
/// that produced a trace becomes a corpus entry all four back ends are held to from then on.
/// </para>
/// <para>
/// <b>Why the raw octets are always written first, and the trace only if it builds.</b>
/// <see cref="SessionTrace.Build"/> is strict on purpose: it refuses a recording that is not strictly
/// alternating, or truncated, or signed without a key. Those refusals are right for a corpus and exactly
/// wrong for a capture — the interop run that <i>fails</i> is the interesting one, and it is precisely the
/// one whose recording will not build. A recorder that only kept what it could parse would discard the
/// evidence of every disagreement it was built to find. So: bytes first, always; then the frame log, as
/// far as the framing holds; then the trace, if it will have one.
/// </para>
/// </remarks>
internal sealed class InteropRecording
{

    /// <summary>Where artifacts go, or <c>null</c> when nobody asked for any.</summary>
    public static String? Directory
        => Environment.GetEnvironmentVariable("V2G_INTEROP_RECORD") is { Length: > 0 } dir ? dir : null;


    private readonly String       directory;
    private readonly String       name;
    private          RecordingStream? tap;

    public InteropRecording(String directory, String name)
    {
        this.directory = directory;
        this.name      = name;
        System.IO.Directory.CreateDirectory(directory);
    }

    /// <summary>An <see cref="InteropRecording"/>, or <c>null</c> when <c>V2G_INTEROP_RECORD</c> is unset —
    /// so a caller can wrap unconditionally and record only when asked.</summary>
    public static InteropRecording? FromEnvironment(String name)
        => Directory is { } dir ? new InteropRecording(dir, name) : null;


    /// <summary>
    /// The flow to compare this run against, when <c>V2G_INTEROP_SCENARIO</c> points at one — either a
    /// counterparty's scenario file or one of our own recorded traces.
    /// </summary>
    /// <remarks>
    /// Optional, and a file that cannot be read is reported rather than thrown: an unreadable reference
    /// is a reason to lose the comparison, never a reason to lose the session.
    /// </remarks>
    public static DeclaredFlow? Reference()
    {

        var path = Environment.GetEnvironmentVariable("V2G_INTEROP_SCENARIO");
        if (String.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return DeclaredFlow.FromFile(path);
        }
        catch (Exception e)
        {
            Console.WriteLine($"V2G_INTEROP_SCENARIO='{path}' could not be read, so the flow report will " +
                              $"have nothing to compare against: {e.Message}");
            return null;
        }

    }


    /// <summary>Wraps the session's stream. Everything in both directions is kept.</summary>
    public Stream Tap(Stream inner)
        => tap = new RecordingStream(inner);


    /// <param name="weAreTheEvcc">Which end of the wire this process was. It decides nothing about the
    /// recording and everything about the labels: a trace's "requests" are the EV's frames whether we
    /// sent them or received them, and a capture filed under the wrong direction is worse than none.</param>
    /// <returns>The paths written, for the test output.</returns>
    public IReadOnlyList<String> Save(String protocol, String mode, String note, Boolean weAreTheEvcc)
    {

        if (tap is null)
            throw new InvalidOperationException("Save() before Tap(): there is nothing recorded.");

        var evToStation = weAreTheEvcc ? tap.Sent     : tap.Received;
        var stationToEv = weAreTheEvcc ? tap.Received : tap.Sent;

        var written = new List<String>();

        void Write(String suffix, Action<String> body)
        {
            var path = Path.Combine(directory, $"{name}.{suffix}");
            body(path);
            written.Add(path);
        }

        Write("ev-to-station.bin", path => File.WriteAllBytes(path, evToStation));
        Write("station-to-ev.bin", path => File.WriteAllBytes(path, stationToEv));
        Write("frames.log",        path => File.WriteAllText(path, FrameLog(evToStation, stationToEv),
                                                             new UTF8Encoding(false)));

        // The reading of the run, as opposed to the evidence for it. Written from the frames rather than
        // from the trace, so a session that ended badly still has one — that is the session whose order of
        // messages somebody most wants to see.
        Write("flow.md", path => File.WriteAllText(path,
            SessionFlow.Report(SplitAsFarAsPossible(evToStation).Frames,
                               SplitAsFarAsPossible(stationToEv).Frames,
                               Reference()),
            new UTF8Encoding(false)));

        try
        {
            var trace = SessionTrace.Build(name, protocol, mode, note, evToStation, stationToEv);
            Write("trace.json", trace.WriteTo);
        }
        catch (Exception e)
        {
            // Not a failure of the run: an aborted or one-sided session is a normal interop outcome, and
            // the bytes above are the evidence. Written down rather than thrown so the reason is in the
            // artifacts next to the capture it explains.
            Write("trace-not-built.txt", path => File.WriteAllText(path,
                $"A SessionTrace could not be built from this recording, which is not in itself a finding:\n\n" +
                $"  {e.Message}\n\n" +
                $"The raw octets and the frame log beside this file are the capture. A trace additionally " +
                $"requires a strictly alternating, untruncated session (and a signing key, if any request " +
                $"was signed) — see Traces/SessionTrace.cs.\n", new UTF8Encoding(false)));
        }

        return written;

    }


    /// <summary>
    /// Both directions, frame by frame: index, payload type, byte count, and the frame as hex.
    /// </summary>
    /// <remarks>
    /// Deliberately not interleaved. The recorder keeps two octet streams and no timestamps, so any
    /// interleaving would be invented — and an invented ordering in a file called <c>frames.log</c> is
    /// exactly the kind of thing somebody later reasons about as though it were observed.
    /// </remarks>
    private static String FrameLog(Byte[] evToStation, Byte[] stationToEv)
    {

        var log = new StringBuilder();

        log.AppendLine("# Frames as recorded, per direction. Ordering within a direction is real;");
        log.AppendLine("# the two directions are not interleaved, because the recording has no timestamps.");

        foreach (var (label, bytes) in new[] { ("EV -> station", evToStation), ("station -> EV", stationToEv) })
        {

            var (frames, leftover) = SplitAsFarAsPossible(bytes);

            log.AppendLine();
            log.AppendLine($"## {label}: {bytes.Length} byte(s), {frames.Count} frame(s)");

            // Named, not just typed. This file is what a run that produced no trace leaves behind, and a
            // list of payload types and hex is the wrong artifact for the only question anybody asks of a
            // session that stopped in the middle: which message was it on?
            foreach (var step in SessionFlow.Of(frames))
                log.AppendLine($"[{step.Index}] {step.Message}" +
                               (step.ResponseCode is { } code ? $" ({code})" : "") +
                               $" payloadType={step.PayloadType} bytes={step.Bytes}" +
                               Environment.NewLine +
                               $"    {Convert.ToHexString(frames[step.Index]).ToLowerInvariant()}");

            if (leftover.Length > 0)
            {
                // The single most useful line in the file when something went wrong: a session that ended
                // mid-frame looks exactly like a session that ended, unless the tail is written down.
                log.AppendLine($"[!] {leftover.Length} trailing byte(s) that are not a complete frame:");
                log.AppendLine($"    {Convert.ToHexString(leftover).ToLowerInvariant()}");
            }

        }

        return log.ToString();

    }


    /// <summary>
    /// Splits a recorded direction into V2GTP frames, stopping at the first thing that is not one and
    /// handing back the rest untouched.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="SessionTrace"/>'s splitter, which throws instead. Both are right:
    /// a corpus entry that is half a session is a broken corpus entry, and a capture that is half a
    /// session is a capture of something worth looking at.
    /// </remarks>
    public static (IReadOnlyList<Byte[]> Frames, Byte[] Leftover) SplitAsFarAsPossible(Byte[] bytes)
    {

        var frames = new List<Byte[]>();
        var offset = 0;

        while (offset < bytes.Length)
        {

            if (!V2GTPCodec.TryReadHeader(bytes.AsSpan(offset), out _, out var payloadLength))
                break;

            // Guarded before the cast: a peer-declared length is not ours to trust, and a hostile or
            // simply broken 0xFFFFFFFF would otherwise become a negative int and index backwards.
            if (payloadLength > V2GTPCodec.MaximumPayloadBytes)
                break;

            var total = V2GTPCodec.HeaderSize + (Int32) payloadLength;
            if (offset + total > bytes.Length)
                break;

            frames.Add(bytes[offset..(offset + total)]);
            offset += total;

        }

        return (frames, bytes[offset..]);

    }

}
