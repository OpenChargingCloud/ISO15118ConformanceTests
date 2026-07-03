namespace Vanaheimr.V2G.Exi;

/// <summary>
/// Bit-level writer over a <see cref="Span{Byte}"/>.
/// <para>
/// EXI bit-packed alignment is MSB-first within each byte: the first bit written
/// occupies bit 7 (0x80) of byte 0, the second bit occupies bit 6 (0x40), and so on.
/// </para>
/// <para>
/// As a <c>ref struct</c> this lives on the stack only — no allocations, and the
/// compiler prevents accidental boxing or async capture.
/// </para>
/// </summary>
public ref struct BitWriter
{
    private readonly Span<byte> _buffer;
    private int _bitPos;

    public BitWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _bitPos = 0;
        // Caller is responsible for the buffer being zero-initialised; stackalloc is.
    }

    public readonly int BitsWritten => _bitPos;
    public readonly int BytesWritten => (_bitPos + 7) >> 3;

    /// <summary>
    /// Write the lowest <paramref name="numBits"/> of <paramref name="value"/>, MSB first.
    /// </summary>
    public void WriteBits(uint value, int numBits)
    {
        if ((uint)numBits > 32)
            throw new ArgumentOutOfRangeException(nameof(numBits));

        for (int i = numBits - 1; i >= 0; i--)
            WriteBit(((value >> i) & 1u) != 0u);
    }

    public void WriteBit(bool b)
    {
        int byteIdx = _bitPos >> 3;
        int bitInByte = _bitPos & 7;
        if (b)
            _buffer[byteIdx] |= (byte)(1 << (7 - bitInByte));
        _bitPos++;
    }

    /// <summary>Pad to the next byte boundary by writing zero bits.</summary>
    public void AlignToByte()
    {
        int rem = _bitPos & 7;
        if (rem != 0) _bitPos += 8 - rem;
    }
}
