using System.Buffers.Binary;

namespace Vanaheimr.V2G.Tp;

/// <summary>
/// V2G Transfer Protocol frame (8-byte header wrapping an EXI payload).
/// </summary>
public static class V2GTP
{
    public const byte ProtocolVersion        = 0x01;
    public const byte InverseProtocolVersion = 0xFE;

    public const ushort PayloadType_AppProtocol      = 0x8000;
    public const ushort PayloadType_DinIso2Main      = 0x8001;
    public const ushort PayloadType_Iso20Main        = 0x8002;
    public const ushort PayloadType_Iso20AC          = 0x8003;
    public const ushort PayloadType_Iso20DC          = 0x8004;
    public const ushort PayloadType_Iso20ACDP        = 0x8005;

    public const int HeaderSize = 8;

    public static int WriteHeader(Span<byte> dest, ushort payloadType, uint payloadLength)
    {
        if (dest.Length < HeaderSize)
            throw new ArgumentException("Need at least 8 bytes for V2GTP header.", nameof(dest));

        dest[0] = ProtocolVersion;
        dest[1] = InverseProtocolVersion;
        BinaryPrimitives.WriteUInt16BigEndian(dest[2..4], payloadType);
        BinaryPrimitives.WriteUInt32BigEndian(dest[4..8], payloadLength);
        return HeaderSize;
    }

    public static bool TryReadHeader(
        ReadOnlySpan<byte> src, out ushort payloadType, out uint payloadLength)
    {
        payloadType = 0; payloadLength = 0;
        if (src.Length < HeaderSize) return false;
        if (src[0] != ProtocolVersion || src[1] != InverseProtocolVersion) return false;

        payloadType   = BinaryPrimitives.ReadUInt16BigEndian(src[2..4]);
        payloadLength = BinaryPrimitives.ReadUInt32BigEndian(src[4..8]);
        return true;
    }
}
