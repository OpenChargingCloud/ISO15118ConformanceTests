namespace Vanaheimr.V2G.AppProtocol;

public sealed record SupportedAppProtocolRes(
    ResponseCode Code,
    byte?        SchemaID); // present only on OK_*
