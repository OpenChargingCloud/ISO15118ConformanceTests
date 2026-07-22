using Vanaheimr.V2G.AppProtocol;
using Vanaheimr.V2G.Simulation.Framing;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.Sap
{
    /// <summary>
    /// The SupportedAppProtocol handshake every session starts with, before either side switches to the
    /// negotiated -2/-20 codec (see <see cref="MessageSet.AppProtocol"/>). This slice negotiates a single,
    /// fixed protocol per side — no multi-candidate EVCC offer list — since the simulator always knows in
    /// advance which protocol/mode it's testing.
    /// </summary>
    public static class SapHandshake
    {
        private const string Iso2Namespace   = "urn:iso:15118:2:2013:MsgDef";
        private const string Iso20DcNamespace = "urn:iso:std:iso:15118:-20:DC";
        private const string Iso20AcNamespace = "urn:iso:std:iso:15118:-20:AC";

        // The SupportedAppProtocol ProtocolNamespace for -20 is the mode-specific application namespace
        // (…-20:DC / …-20:AC), NOT …-20:CommonMessages — a live Josev interop run rejected the CommonMessages
        // offer (Failed_NoNegotiation); Josev's own -20 DC EVCC offers …-20:DC (see docs/interop-runs/).
        private static string NamespaceFor(ProtocolVariant variant, PowerMode mode) => variant switch
        {
            ProtocolVariant.Iso15118_2  => Iso2Namespace,
            ProtocolVariant.Iso15118_20 => mode == PowerMode.Dc ? Iso20DcNamespace : Iso20AcNamespace,
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "unknown protocol variant"),
        };

        /// <summary>EVCC side: offers exactly <paramref name="wanted"/> (for -20, the <paramref name="mode"/>-specific
        /// namespace), throws <see cref="SessionAborted"/> if the SECC rejects it.</summary>
        public static async Task RunEvccSideAsync(Stream stream, ProtocolVariant wanted, CancellationToken ct = default, PowerMode mode = PowerMode.Dc)
        {
            // Version numbers per protocol: ISO 15118-2:2013 MsgDef is protocol version 2.0, the -20 sets
            // are 1.0. A live Josev SECC matches namespace AND major version — offering -2 as "1.0" gets
            // Failed_NoNegotiation (found live 2026-07-22; our own SECC matched on namespace only).
            var (major, minor) = wanted == ProtocolVariant.Iso15118_2 ? (2u, 0u) : (1u, 0u);
            var req = new SupportedAppProtocolReq(new[]
            {
                new AppProtocolEntry(NamespaceFor(wanted, mode), VersionNumberMajor: major, VersionNumberMinor: minor, SchemaID: 1, Priority: 1),
            });
            var buf = new byte[128];
            if (!SupportedAppProtocolCodec.TryEncodeRequest(req, buf, out int n))
                throw new InvalidOperationException("SAP: EXI encode failed (buffer too small?).");

            await V2GTPStream.WriteRawFrameAsync(stream, V2GTP.PayloadType_AppProtocol, buf.AsMemory(0, n), ct).ConfigureAwait(false);

            if (await ReadSapAsync(stream, ct).ConfigureAwait(false) is not SupportedAppProtocolRes res)
                throw new SessionAborted("SAP: expected a SupportedAppProtocolRes.");
            if (res.Code is not (ResponseCode.OK_SuccessfulNegotiation or ResponseCode.OK_SuccessfulNegotiationWithMinorDeviation))
                throw new SessionAborted($"SAP: SECC rejected the protocol offer ({res.Code}).");
        }

        /// <summary>SECC side: accepts if the EVCC offered <paramref name="accepted"/>, otherwise replies Failed and throws.</summary>
        public static async Task RunSeccSideAsync(Stream stream, ProtocolVariant accepted, CancellationToken ct = default, PowerMode mode = PowerMode.Dc)
        {
            if (await ReadSapAsync(stream, ct).ConfigureAwait(false) is not SupportedAppProtocolReq req)
                throw new SessionAborted("SAP: expected a SupportedAppProtocolReq.");

            var wantedNamespace = NamespaceFor(accepted, mode);
            bool offered = req.AppProtocols.Any(p => p.ProtocolNamespace == wantedNamespace);

            var res = offered
                ? new SupportedAppProtocolRes(ResponseCode.OK_SuccessfulNegotiation, SchemaID: 1)
                : new SupportedAppProtocolRes(ResponseCode.Failed_NoNegotiation, SchemaID: null);

            var buf = new byte[16];
            if (!SupportedAppProtocolCodec.TryEncodeResponse(res, buf, out int n))
                throw new InvalidOperationException("SAP: EXI encode failed (buffer too small?).");
            await V2GTPStream.WriteRawFrameAsync(stream, V2GTP.PayloadType_AppProtocol, buf.AsMemory(0, n), ct).ConfigureAwait(false);

            if (!offered)
                throw new SessionAborted($"SAP: EVCC did not offer {wantedNamespace}.");
        }

        /// <summary>Reads one SupportedAppProtocol frame. SAP shares payload id 0x8001 with the -2 messages
        /// and so is decoded here explicitly, not through the payload-type dispatcher (see
        /// <see cref="V2GTP.PayloadType_AppProtocol"/>).</summary>
        private static async Task<object> ReadSapAsync(Stream stream, CancellationToken ct)
        {
            var (frame, payloadType) = await V2GTPStream.ReadRawFrameAsync(stream, ct).ConfigureAwait(false);
            if (payloadType != V2GTP.PayloadType_AppProtocol)
                throw new SessionAborted($"SAP: expected payload type 0x{V2GTP.PayloadType_AppProtocol:X4}, got 0x{payloadType:X4}.");

            return SupportedAppProtocolCodec.DecodeAny(frame.AsSpan(V2GTP.HeaderSize), out _);
        }
    }
}
