using Vanaheimr.V2G.Exi;

namespace Vanaheimr.V2G.AppProtocol;

/// <summary>
/// Hand-written codec for <c>SupportedAppProtocolReq</c> / <c>SupportedAppProtocolRes</c>.
/// In production this would be emitted by an <c>IIncrementalGenerator</c> consuming
/// <c>V2G_CI_AppProtocol.xsd</c>.
///
/// <para>
/// EXI options: bit-packed alignment, schema-informed strict grammar, no preserve options,
/// no header options document → header is the single byte <c>0x80</c>.
/// </para>
///
/// <para>Document grammar (alphabetical order over global elements):</para>
/// <code>
///   Document     → SD DocContent
///   DocContent   → SE(supportedAppProtocolReq) DocEnd : 0      (1 bit, 2 globals)
///                | SE(supportedAppProtocolRes) DocEnd : 1
///   DocEnd       → ED                                          (0 bits)
/// </code>
///
/// <para>Body grammars (strict, single-production states have 0-bit event codes):</para>
/// <code>
///   Req_0        → SE(AppProtocol) Req_1                       (0 bits, mandatory first)
///   Req_n  1..19 → SE(AppProtocol) Req_{n+1} : 0               (1 bit)
///                | EE                       : 1
///   Req_20       → EE                                          (0 bits)
///
///   AppProto_0   → SE(ProtocolNamespace)    AppProto_1         (0 bits)
///   AppProto_1   → SE(VersionNumberMajor)   AppProto_2         (0 bits)
///   AppProto_2   → SE(VersionNumberMinor)   AppProto_3         (0 bits)
///   AppProto_3   → SE(SchemaID)             AppProto_4         (0 bits)
///   AppProto_4   → SE(Priority)             AppProto_5         (0 bits)
///   AppProto_5   → EE                                          (0 bits)
///
///   Res_0        → SE(ResponseCode)         Res_1              (0 bits)
///   Res_1        → SE(SchemaID)             Res_2 : 0          (1 bit, optional element)
///                | EE                              : 1
///   Res_2        → EE                                          (0 bits)
/// </code>
///
/// <para>Value codings:</para>
/// <list type="bullet">
///   <item><c>ProtocolNamespace</c> — String value (miss case: <c>UInt(len+2)</c> + codepoints).</item>
///   <item><c>VersionNumber*</c>    — Unsigned Integer.</item>
///   <item><c>SchemaID</c>          — Unsigned Integer (xs:unsignedByte, unrestricted).</item>
///   <item><c>Priority</c>          — n-bit Unsigned Integer, n=5, stored as <c>value-1</c>
///                                    (range [1..20] → 20 distinct values, ⌈log₂20⌉=5).</item>
///   <item><c>ResponseCode</c>      — n-bit Unsigned Integer, n=2, lexicographic enum index.</item>
/// </list>
///
/// <para><b>Conformance note.</b> Bit-exact wire compatibility with cbV2G/OpenV2G has not
/// been validated in this prototype — only internal roundtripping. Before relying on this
/// against real EVSEs, diff the output byte-by-byte against a reference encoder for a
/// pinned set of test vectors.</para>
/// </summary>
public static class SupportedAppProtocolCodec
{
    /// <summary>EXI header byte: distinguishing bits 10, no options, format version 0.</summary>
    public const byte ExiHeader = 0x80;

    private const int MaxAppProtocols = 20;

    // Lexicographic enum order for ResponseCode (how EXI sorts xs:enumeration values).
    // Encoded as 2-bit unsigned (3 values → ⌈log₂3⌉ = 2 bits; index 3 is invalid).
    private static int EncodeResponseCode(ResponseCode code) => code switch
    {
        ResponseCode.Failed_NoNegotiation                       => 0,
        ResponseCode.OK_SuccessfulNegotiation                   => 1,
        ResponseCode.OK_SuccessfulNegotiationWithMinorDeviation => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };

    private static ResponseCode DecodeResponseCode(uint idx) => idx switch
    {
        0 => ResponseCode.Failed_NoNegotiation,
        1 => ResponseCode.OK_SuccessfulNegotiation,
        2 => ResponseCode.OK_SuccessfulNegotiationWithMinorDeviation,
        _ => throw new InvalidDataException($"Reserved/invalid ResponseCode index: {idx}"),
    };

    // ---------------------------------------------------------------------
    //  Encode
    // ---------------------------------------------------------------------

    public static bool TryEncodeRequest(
        SupportedAppProtocolReq msg, Span<byte> dest, out int bytesWritten)
    {
        bytesWritten = 0;

        if (msg.AppProtocols.Count is < 1 or > MaxAppProtocols) return false;
        if (dest.Length < 1) return false;

        dest[0] = ExiHeader;
        var w = new BitWriter(dest[1..]);

        // DocContent → SE(supportedAppProtocolReq) : event code 0 in 1 bit.
        w.WriteBits(0, 1);

        // SE(supportedAppProtocolReq) is implicit (no event code: only one production).
        // Now in Req_0.
        for (int i = 0; i < msg.AppProtocols.Count; i++)
        {
            // Req_0 has only "SE(AppProtocol)" → 0 bits.
            // Req_1..Req_19 add "EE" alternative → 1 bit (write 0 = continue).
            if (i > 0)
                w.WriteBits(0, 1);

            EncodeAppProtocol(ref w, msg.AppProtocols[i]);
        }

        // Terminate the AppProtocol list.
        // If we wrote N < 20 entries, we are in Req_N which needs an explicit EE (1 bit = 1).
        // If N == 20, we are in Req_20 whose only production is EE (0 bits).
        if (msg.AppProtocols.Count < MaxAppProtocols)
            w.WriteBits(1, 1);

        // EE for supportedAppProtocolReq element and ED for the document are both 0-bit
        // transitions in strict mode — nothing more to write.

        w.AlignToByte();
        bytesWritten = 1 + w.BytesWritten;
        return true;
    }

    private static void EncodeAppProtocol(ref BitWriter w, AppProtocolEntry e)
    {
        // All five SE transitions and their EEs are 0-bit in strict mode;
        // we only write the actual values.

        ExiPrimitives.WriteStringValue(ref w, e.ProtocolNamespace);
        ExiPrimitives.WriteUnsignedInteger(ref w, e.VersionNumberMajor);
        ExiPrimitives.WriteUnsignedInteger(ref w, e.VersionNumberMinor);
        ExiPrimitives.WriteUnsignedInteger(ref w, e.SchemaID);

        if (e.Priority is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(e.Priority), "Priority must be in [1..20].");
        w.WriteBits((uint)(e.Priority - 1), 5);
    }

    public static bool TryEncodeResponse(
        SupportedAppProtocolRes msg, Span<byte> dest, out int bytesWritten)
    {
        bytesWritten = 0;
        if (dest.Length < 1) return false;

        dest[0] = ExiHeader;
        var w = new BitWriter(dest[1..]);

        // DocContent → SE(supportedAppProtocolRes) : event code 1 in 1 bit.
        w.WriteBits(1, 1);

        // ResponseCode (2-bit enum index)
        w.WriteBits((uint)EncodeResponseCode(msg.Code), 2);

        // Optional SchemaID
        if (msg.SchemaID is byte schemaId)
        {
            w.WriteBits(0, 1); // 0 = SE(SchemaID)
            ExiPrimitives.WriteUnsignedInteger(ref w, schemaId);
        }
        else
        {
            w.WriteBits(1, 1); // 1 = EE
        }

        w.AlignToByte();
        bytesWritten = 1 + w.BytesWritten;
        return true;
    }

    // ---------------------------------------------------------------------
    //  Decode
    // ---------------------------------------------------------------------

    /// <summary>
    /// Decode either a request or response. Caller dispatches on the result type.
    /// </summary>
    public static object DecodeAny(ReadOnlySpan<byte> src, out int bytesConsumed)
    {
        if (src.Length < 2 || src[0] != ExiHeader)
            throw new InvalidDataException("Invalid EXI header for AppProtocol stream.");

        var r = new BitReader(src[1..]);
        uint sel = r.ReadBits(1); // 0 = Req, 1 = Res

        object result = sel == 0 ? DecodeRequestBody(ref r) : DecodeResponseBody(ref r);
        bytesConsumed = 1 + r.BytesConsumed;
        return result;
    }

    private static SupportedAppProtocolReq DecodeRequestBody(ref BitReader r)
    {
        var entries = new List<AppProtocolEntry>(capacity: 4);

        // First entry mandatory (Req_0 has 0-bit event code).
        entries.Add(DecodeAppProtocol(ref r));

        // Up to 19 further entries, each preceded by a 1-bit event code.
        while (entries.Count < MaxAppProtocols)
        {
            uint ec = r.ReadBits(1);
            if (ec == 1) break;          // EE
            entries.Add(DecodeAppProtocol(ref r));
        }
        // If we reached 20, Req_20 → EE (0 bits) terminates implicitly.

        return new SupportedAppProtocolReq(entries);
    }

    private static AppProtocolEntry DecodeAppProtocol(ref BitReader r)
    {
        var ns       = ExiPrimitives.ReadStringValue(ref r);
        var verMaj   = checked((uint)ExiPrimitives.ReadUnsignedInteger(ref r));
        var verMin   = checked((uint)ExiPrimitives.ReadUnsignedInteger(ref r));
        var schemaId = checked((byte)ExiPrimitives.ReadUnsignedInteger(ref r));
        var priority = (byte)(r.ReadBits(5) + 1);
        return new AppProtocolEntry(ns, verMaj, verMin, schemaId, priority);
    }

    private static SupportedAppProtocolRes DecodeResponseBody(ref BitReader r)
    {
        var code = DecodeResponseCode(r.ReadBits(2));

        byte? schemaId = null;
        uint ec = r.ReadBits(1);
        if (ec == 0)
            schemaId = checked((byte)ExiPrimitives.ReadUnsignedInteger(ref r));
        // ec == 1 → EE, no SchemaID

        return new SupportedAppProtocolRes(code, schemaId);
    }
}
