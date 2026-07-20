namespace Vanaheimr.V2G.Exi
{
    /// <summary>
    /// Bit-level reader over a <see cref="ReadOnlySpan{Byte}"/>, MSB-first to match EXI bit-packed alignment.
    /// </summary>
    public ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _buffer;
        private int _bitPos;

        public BitReader(ReadOnlySpan<byte> buffer)
        {
            _buffer = buffer;
            _bitPos = 0;
        }

        public readonly int BitsRead => _bitPos;
        public readonly int BytesConsumed => (_bitPos + 7) >> 3;

        public bool ReadBit()
        {
            int byteIdx = _bitPos >> 3;
            int bitInByte = _bitPos & 7;
            if (byteIdx >= _buffer.Length)
                throw new EndOfStreamException("EXI bitstream exhausted");
            bool bit = ((_buffer[byteIdx] >> (7 - bitInByte)) & 1) != 0;
            _bitPos++;
            return bit;
        }

        public uint ReadBits(int numBits)
        {
            if ((uint)numBits > 32)
                throw new ArgumentOutOfRangeException(nameof(numBits));
            uint value = 0;
            for (int i = 0; i < numBits; i++)
                value = (value << 1) | (ReadBit() ? 1u : 0u);
            return value;
        }
    }
}
