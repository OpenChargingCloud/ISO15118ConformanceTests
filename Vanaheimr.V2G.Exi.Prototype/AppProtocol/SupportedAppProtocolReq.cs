namespace Vanaheimr.V2G.AppProtocol
{
    public sealed record SupportedAppProtocolReq(
        IReadOnlyList<AppProtocolEntry> AppProtocols); // 1..20
}
