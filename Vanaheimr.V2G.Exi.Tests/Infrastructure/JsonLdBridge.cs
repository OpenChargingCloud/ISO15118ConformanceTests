using System.Text.Json.Nodes;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure
{

    /// <summary>
    /// One message set's four generated entry points, behind one shape: decode, serialize, parse,
    /// encode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists because the eight generated assemblies deliberately share type names —
    /// <c>SessionSetupReq</c> is a different type in -2 and in -20, and <c>MessageHeaderType</c>
    /// exists five times — so a test file that referenced them directly could only ever test one set.
    /// The same reason <c>Iso15118_20*Fixtures</c> exist; this is their read-only counterpart.
    /// </para>
    /// <para>
    /// Every member here is a generated method. Nothing in this file knows a field, and it is
    /// deliberately incapable of compensating for a mapping bug.
    /// </para>
    /// </remarks>
    public sealed record JsonLdBridge(
        string                                        Name,
        string                                        Context,
        Func<byte[], (object Value, int Consumed)>    DecodeAny,
        Func<object, JsonObject>                      ToJSON,
        Func<JsonNode, object>                        ParseJSON,
        TryEncode                                     Encode)
    {

        public object Decode(byte[] bytes, out int consumed)
        {
            var (value, read) = DecodeAny(bytes);
            consumed = read;
            return value;
        }


        public static readonly JsonLdBridge AppProtocol = new(
            "AppProtocol",
            Vanaheimr.V2G.AppProtocol.Generated.SupportedAppProtocolCodecJson.Context,
            bytes => { var v = Vanaheimr.V2G.AppProtocol.Generated.SupportedAppProtocolCodec.DecodeAny(bytes, out var n); return (v, n); },
            Vanaheimr.V2G.AppProtocol.Generated.SupportedAppProtocolCodecJson.ToJSON,
            node => Vanaheimr.V2G.AppProtocol.Generated.SupportedAppProtocolCodecJson.ParseJSON(node),
            Vanaheimr.V2G.AppProtocol.Generated.SupportedAppProtocolCodec.TryEncodeAny);

        public static readonly JsonLdBridge Iso2 = new(
            "ISO 15118-2",
            Vanaheimr.V2G.Iso15118_2.Generated.Iso2CodecJson.Context,
            bytes => { var v = Vanaheimr.V2G.Iso15118_2.Generated.Iso2Codec.DecodeAny(bytes, out var n); return (v, n); },
            Vanaheimr.V2G.Iso15118_2.Generated.Iso2CodecJson.ToJSON,
            node => Vanaheimr.V2G.Iso15118_2.Generated.Iso2CodecJson.ParseJSON(node),
            Vanaheimr.V2G.Iso15118_2.Generated.Iso2Codec.TryEncodeAny);

        public static readonly JsonLdBridge Common = new(
            "ISO 15118-20 CommonMessages",
            Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated.CommonMessagesCodecJson.Context,
            bytes => { var v = Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated.CommonMessagesCodec.DecodeAny(bytes, out var n); return (v, n); },
            Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated.CommonMessagesCodecJson.ToJSON,
            node => Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated.CommonMessagesCodecJson.ParseJSON(node),
            Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated.CommonMessagesCodec.TryEncodeAny);

        public static readonly JsonLdBridge Dc = new(
            "ISO 15118-20 DC",
            Vanaheimr.V2G.Iso15118_20.DC.Generated.DcCodecJson.Context,
            bytes => { var v = Vanaheimr.V2G.Iso15118_20.DC.Generated.DcCodec.DecodeAny(bytes, out var n); return (v, n); },
            Vanaheimr.V2G.Iso15118_20.DC.Generated.DcCodecJson.ToJSON,
            node => Vanaheimr.V2G.Iso15118_20.DC.Generated.DcCodecJson.ParseJSON(node),
            Vanaheimr.V2G.Iso15118_20.DC.Generated.DcCodec.TryEncodeAny);

        public static readonly JsonLdBridge Ac = new(
            "ISO 15118-20 AC",
            Vanaheimr.V2G.Iso15118_20.AC.Generated.AcCodecJson.Context,
            bytes => { var v = Vanaheimr.V2G.Iso15118_20.AC.Generated.AcCodec.DecodeAny(bytes, out var n); return (v, n); },
            Vanaheimr.V2G.Iso15118_20.AC.Generated.AcCodecJson.ToJSON,
            node => Vanaheimr.V2G.Iso15118_20.AC.Generated.AcCodecJson.ParseJSON(node),
            Vanaheimr.V2G.Iso15118_20.AC.Generated.AcCodec.TryEncodeAny);

        public static readonly JsonLdBridge Wpt = new(
            "ISO 15118-20 WPT",
            Vanaheimr.V2G.Iso15118_20.WPT.Generated.WptCodecJson.Context,
            bytes => { var v = Vanaheimr.V2G.Iso15118_20.WPT.Generated.WptCodec.DecodeAny(bytes, out var n); return (v, n); },
            Vanaheimr.V2G.Iso15118_20.WPT.Generated.WptCodecJson.ToJSON,
            node => Vanaheimr.V2G.Iso15118_20.WPT.Generated.WptCodecJson.ParseJSON(node),
            Vanaheimr.V2G.Iso15118_20.WPT.Generated.WptCodec.TryEncodeAny);

        public static readonly JsonLdBridge Acdp = new(
            "ISO 15118-20 ACDP",
            Vanaheimr.V2G.Iso15118_20.ACDP.Generated.AcdpCodecJson.Context,
            bytes => { var v = Vanaheimr.V2G.Iso15118_20.ACDP.Generated.AcdpCodec.DecodeAny(bytes, out var n); return (v, n); },
            Vanaheimr.V2G.Iso15118_20.ACDP.Generated.AcdpCodecJson.ToJSON,
            node => Vanaheimr.V2G.Iso15118_20.ACDP.Generated.AcdpCodecJson.ParseJSON(node),
            Vanaheimr.V2G.Iso15118_20.ACDP.Generated.AcdpCodec.TryEncodeAny);

        public static readonly JsonLdBridge AcDerIec = new(
            "ISO 15118-20 AC_DER_IEC",
            Vanaheimr.V2G.Iso15118_20.AC_DER_IEC.Generated.AcDerIecCodecJson.Context,
            bytes => { var v = Vanaheimr.V2G.Iso15118_20.AC_DER_IEC.Generated.AcDerIecCodec.DecodeAny(bytes, out var n); return (v, n); },
            Vanaheimr.V2G.Iso15118_20.AC_DER_IEC.Generated.AcDerIecCodecJson.ToJSON,
            node => Vanaheimr.V2G.Iso15118_20.AC_DER_IEC.Generated.AcDerIecCodecJson.ParseJSON(node),
            Vanaheimr.V2G.Iso15118_20.AC_DER_IEC.Generated.AcDerIecCodec.TryEncodeAny);

        public static readonly JsonLdBridge AcDerSae = new(
            "ISO 15118-20 AC_DER_SAE",
            Vanaheimr.V2G.Iso15118_20.AC_DER_SAE.Generated.AcDerSaeCodecJson.Context,
            bytes => { var v = Vanaheimr.V2G.Iso15118_20.AC_DER_SAE.Generated.AcDerSaeCodec.DecodeAny(bytes, out var n); return (v, n); },
            Vanaheimr.V2G.Iso15118_20.AC_DER_SAE.Generated.AcDerSaeCodecJson.ToJSON,
            node => Vanaheimr.V2G.Iso15118_20.AC_DER_SAE.Generated.AcDerSaeCodecJson.ParseJSON(node),
            Vanaheimr.V2G.Iso15118_20.AC_DER_SAE.Generated.AcDerSaeCodec.TryEncodeAny);

        public override string ToString() => Name;

    }


    /// <summary>A generated <c>TryEncodeAny</c>, which cannot be an ordinary <c>Func</c> — it has a
    /// <c>Span</c> parameter and an <c>out</c>.</summary>
    public delegate bool TryEncode(object message, Span<byte> destination, out int bytesWritten);

}
