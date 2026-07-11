using Vanaheimr.V2G.AppProtocol;
using Vanaheimr.V2G.Simulation.Framing;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.Sap;

/// <summary>
/// The SupportedAppProtocol handshake every session starts with, before either side switches to the
/// negotiated -2/-20 codec (see <see cref="MessageSet.AppProtocol"/>). This slice negotiates a single,
/// fixed protocol per side — no multi-candidate EVCC offer list — since the simulator always knows in
/// advance which protocol/mode it's testing.
/// </summary>
public static class SapHandshake
{
    private const string Iso2Namespace  = "urn:iso:15118:2:2013:MsgDef";
    private const string Iso20Namespace = "urn:iso:std:iso:15118:-20:CommonMessages";

    private static string NamespaceFor(ProtocolVariant variant) => variant switch
    {
        ProtocolVariant.Iso15118_2  => Iso2Namespace,
        ProtocolVariant.Iso15118_20 => Iso20Namespace,
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "unknown protocol variant"),
    };

    /// <summary>EVCC side: offers exactly <paramref name="wanted"/>, throws <see cref="SessionAborted"/> if the SECC rejects it.</summary>
    public static async Task RunEvccSideAsync(Stream stream, ProtocolVariant wanted, CancellationToken ct = default)
    {
        var req = new SupportedAppProtocolReq(new[]
        {
            new AppProtocolEntry(NamespaceFor(wanted), VersionNumberMajor: 1, VersionNumberMinor: 0, SchemaID: 1, Priority: 1),
        });
        var buf = new byte[128];
        if (!SupportedAppProtocolCodec.TryEncodeRequest(req, buf, out int n))
            throw new InvalidOperationException("SAP: EXI encode failed (buffer too small?).");

        await V2GTPStream.WriteFrameAsync(stream, MessageSet.AppProtocol, buf.AsMemory(0, n), ct).ConfigureAwait(false);
        var (set, message) = await V2GTPStream.ReadFrameAsync(stream, ct).ConfigureAwait(false);

        if (set != MessageSet.AppProtocol || message is not SupportedAppProtocolRes res)
            throw new SessionAborted($"SAP: expected a SupportedAppProtocolRes, got {set}.");
        if (res.Code is not (ResponseCode.OK_SuccessfulNegotiation or ResponseCode.OK_SuccessfulNegotiationWithMinorDeviation))
            throw new SessionAborted($"SAP: SECC rejected the protocol offer ({res.Code}).");
    }

    /// <summary>SECC side: accepts if the EVCC offered <paramref name="accepted"/>, otherwise replies Failed and throws.</summary>
    public static async Task RunSeccSideAsync(Stream stream, ProtocolVariant accepted, CancellationToken ct = default)
    {
        var (set, message) = await V2GTPStream.ReadFrameAsync(stream, ct).ConfigureAwait(false);
        if (set != MessageSet.AppProtocol || message is not SupportedAppProtocolReq req)
            throw new SessionAborted($"SAP: expected a SupportedAppProtocolReq, got {set}.");

        var wantedNamespace = NamespaceFor(accepted);
        bool offered = req.AppProtocols.Any(p => p.ProtocolNamespace == wantedNamespace);

        var res = offered
            ? new SupportedAppProtocolRes(ResponseCode.OK_SuccessfulNegotiation, SchemaID: 1)
            : new SupportedAppProtocolRes(ResponseCode.Failed_NoNegotiation, SchemaID: null);

        var buf = new byte[16];
        if (!SupportedAppProtocolCodec.TryEncodeResponse(res, buf, out int n))
            throw new InvalidOperationException("SAP: EXI encode failed (buffer too small?).");
        await V2GTPStream.WriteFrameAsync(stream, MessageSet.AppProtocol, buf.AsMemory(0, n), ct).ConfigureAwait(false);

        if (!offered)
            throw new SessionAborted($"SAP: EVCC did not offer {wantedNamespace}.");
    }
}
