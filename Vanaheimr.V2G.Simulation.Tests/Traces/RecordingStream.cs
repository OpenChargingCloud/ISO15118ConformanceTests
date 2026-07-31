namespace Vanaheimr.V2G.Simulation.Tests.Traces;

/// <summary>
/// A <see cref="Stream"/> decorator that passes every byte through untouched and keeps a copy of each
/// direction. Wrapped around the EVCC's end of a session, <see cref="Sent"/> is exactly what the vehicle
/// put on the wire and <see cref="Received"/> exactly what the station answered.
/// </summary>
/// <remarks>
/// <para>
/// It records the raw octet stream rather than decoded frames, and splitting happens afterwards
/// (<see cref="SessionTrace.Build"/>). That ordering is deliberate: V2GTP frames are self-delimiting, so
/// nothing is lost, and a recorder that had to understand the framing could only record sessions whose
/// framing already works — which is one of the things a trace is supposed to be able to catch.
/// </para>
/// <para>
/// It does not dispose the stream it wraps. Ownership stays with whoever opened it, which in these tests
/// is a <c>using</c> in the fixture.
/// </para>
/// </remarks>
public sealed class RecordingStream(Stream inner) : Stream
{

    private readonly MemoryStream sent     = new();
    private readonly MemoryStream received = new();

    /// <summary>Every byte written to the wrapped stream, in order.</summary>
    public byte[] Sent => sent.ToArray();

    /// <summary>Every byte read from the wrapped stream, in order.</summary>
    public byte[] Received => received.ToArray();


    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var n = await inner.ReadAsync(buffer, ct).ConfigureAwait(false);
        if (n > 0)
            received.Write(buffer.Span[..n]);
        return n;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        sent.Write(buffer.Span);
        await inner.WriteAsync(buffer, ct).ConfigureAwait(false);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = inner.Read(buffer, offset, count);
        if (n > 0)
            received.Write(buffer, offset, n);
        return n;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        sent.Write(buffer, offset, count);
        inner.Write(buffer, offset, count);
    }

    public override void Flush()                            => inner.Flush();
    public override Task FlushAsync(CancellationToken ct)   => inner.FlushAsync(ct);

    public override bool CanRead  => inner.CanRead;
    public override bool CanSeek  => false;
    public override bool CanWrite => inner.CanWrite;

    public override long Length                    => throw new NotSupportedException();
    public override long Position                  { get => throw new NotSupportedException();
                                                     set => throw new NotSupportedException(); }
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long value)      => throw new NotSupportedException();

}
