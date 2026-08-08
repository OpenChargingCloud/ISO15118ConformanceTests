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

using System.Security.Cryptography;

using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

namespace ISO15118ConformanceTests.Simulation.Traces;

/// <summary>Raised the moment a replayed session sends something the trace did not record.</summary>
public sealed class TraceMismatch(string message) : Exception(message);


/// <summary>
/// A station made of a recorded session: it answers each request with the trace's next recorded response,
/// and requires the request that arrived to be byte-identical to the one recorded in that slot. There is
/// no peer, no socket and no SECC — only the file.
/// </summary>
/// <remarks>
/// <para>
/// It fails on the <b>first</b> divergent frame rather than collecting differences at the end. Once a
/// request differs, every later one is a consequence of a session that already went wrong, and reporting
/// twelve mismatches when one thing broke buries the finding under its own fallout.
/// </para>
/// <para>
/// This is the C# half of the oracle, and its whole job is to be reimplemented: the Kotlin and Swift
/// suites need the same ~80 lines against the same files. Consuming the trace here first is what makes it
/// a running check before any port depends on it — the C# EVCC replaying its own recording is the cheapest
/// possible proof that the corpus is replayable at all.
/// </para>
/// </remarks>
public sealed class TraceReplayStream(SessionTrace trace) : Stream
{

    private readonly List<byte> pending  = [];   // request bytes not yet forming a whole frame
    private readonly Queue<byte> readable = new();   // recorded response bytes waiting to be read

    /// <summary>How many exchanges were replayed. Compare against the trace's own count after the run:
    /// a session that stops early sends no wrong bytes and would otherwise pass.</summary>
    public int Replayed { get; private set; }

    /// <summary>True once every recorded exchange has been replayed.</summary>
    public bool Complete => Replayed == trace.Exchanges.Count;

    private ECDsa? signingKey;

    /// <summary>The corpus public key, built once. Verification needs a key from outside the frame —
    /// taking one from the message itself would accept anything a port cared to sign with.</summary>
    private ECDsa SigningKey()
    {
        if (signingKey is not null)
            return signingKey;

        var key = trace.SigningKey
            ?? throw new TraceMismatch(
                $"trace '{trace.Name}' carries a signed exchange but no signing key. " +
                 "SessionTrace.Build refuses to produce that, so this file was hand-edited.");

        signingKey = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Convert.FromHexString(key.X), Y = Convert.FromHexString(key.Y) },
        });
        return signingKey;
    }


    private void Accept(ReadOnlySpan<byte> written)
    {

        pending.AddRange(written);

        while (true)
        {

            if (pending.Count < V2GTPCodec.HeaderSize)
                return;

            if (!V2GTPCodec.TryReadHeader(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pending),
                                     out _, out var payloadLength))
                throw new TraceMismatch(
                    $"exchange {Replayed}: the bytes written are not a V2GTP frame (bad version/type bytes).");

            var total = V2GTPCodec.HeaderSize + checked((int) payloadLength);
            if (pending.Count < total)
                return;

            var frame = pending.GetRange(0, total).ToArray();
            pending.RemoveRange(0, total);

            if (Replayed >= trace.Exchanges.Count)
                throw new TraceMismatch(
                    $"the session sent exchange {Replayed}, but the trace '{trace.Name}' records only " +
                    $"{trace.Exchanges.Count}. The port charges on past where the recording ends.");

            var exchange = trace.Exchanges[Replayed];
            var expected = exchange.Request.Bytes;

            // A signed frame cannot be compared as bytes — ECDSA's nonce is random, so the same
            // message signed twice differs. SignedFrame explains the substitution; the short of it is
            // that the signature value is the only random part, so putting the recorded one back
            // makes everything else comparable exactly, and the produced one is checked on its own.
            var comparable = exchange.Request.SignatureBytes is { } recordedSignature
                                 ? SignedFrame.WithSignatureValue(frame, recordedSignature)
                                 : frame;

            if (!comparable.AsSpan().SequenceEqual(expected))
                throw new TraceMismatch(
                    $"exchange {Replayed} ({exchange.Request.Message}) differs from the trace '{trace.Name}'" +
                    (exchange.Request.IsSigned ? " (compared with the recorded signature substituted)" : "") +
                    ":\n" + Diff(expected, comparable));

            if (exchange.Request.IsSigned && !SignedFrame.VerifiesWith(frame, SigningKey()))
                throw new TraceMismatch(
                    $"exchange {Replayed} ({exchange.Request.Message}) matches the trace once its signature " +
                     "is substituted, but the signature it actually produced does not verify against the " +
                     "corpus key. The message is right and the signing is not — a wrong key, wrong octets, " +
                     "or a wrong signature encoding.");

            foreach (var b in exchange.Response.Bytes)
                readable.Enqueue(b);

            Replayed++;

        }

    }


    private int Serve(Span<byte> buffer)
    {

        if (readable.Count == 0)
            throw new TraceMismatch(
                $"exchange {Replayed}: the session tried to read a response without having written a " +
                 "complete request first — nothing in the trace answers that.");

        var n = Math.Min(buffer.Length, readable.Count);
        for (var i = 0; i < n; i++)
            buffer[i] = readable.Dequeue();

        return n;

    }


    /// <summary>Where two frames first part company, with a little of each side around it. The offset is
    /// the useful part: under 8 it is the V2GTP header, above it the EXI body.</summary>
    private static string Diff(byte[] expected, byte[] actual)
    {

        var at = 0;
        while (at < expected.Length && at < actual.Length && expected[at] == actual[at])
            at++;

        var where = at < V2GTPCodec.HeaderSize
                        ? $"byte {at}, inside the 8-byte V2GTP header"
                        : $"byte {at} (EXI payload offset {at - V2GTPCodec.HeaderSize})";

        static string Window(byte[] bytes, int at) =>
            bytes.Length <= at
                ? "<ends here>"
                : Convert.ToHexString(bytes.AsSpan(at, Math.Min(16, bytes.Length - at))).ToLowerInvariant();

        return $"  first difference at {where}\n" +
               $"  trace  {expected.Length,4} bytes, from there: {Window(expected, at)}\n" +
               $"  actual {actual.Length,4} bytes, from there: {Window(actual,   at)}";

    }


    public override void Write(byte[] buffer, int offset, int count) =>
        Accept(buffer.AsSpan(offset, count));

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        Accept(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        Accept(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Serve(buffer.AsSpan(offset, count));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        ValueTask.FromResult(Serve(buffer.Span));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        Task.FromResult(Serve(buffer.AsSpan(offset, count)));

    public override void Flush()                          { }
    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    public override bool CanRead  => true;
    public override bool CanSeek  => false;
    public override bool CanWrite => true;

    public override long Length                    => throw new NotSupportedException();
    public override long Position                  { get => throw new NotSupportedException();
                                                     set => throw new NotSupportedException(); }
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long value)      => throw new NotSupportedException();

}
