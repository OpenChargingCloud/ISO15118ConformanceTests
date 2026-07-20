namespace Vanaheimr.V2G.AppProtocol
{
    /// <summary>
    /// One AppProtocol entry advertised by the EV in the AppHandshake.
    /// Generated from <c>AppProtocolType</c> in V2G_CI_AppProtocol.xsd.
    /// </summary>
    public sealed record AppProtocolEntry(
        string ProtocolNamespace,    // xs:string,  maxLength 100
        uint   VersionNumberMajor,   // xs:unsignedInt
        uint   VersionNumberMinor,   // xs:unsignedInt
        byte   SchemaID,             // xs:unsignedByte
        byte   Priority);            // xs:unsignedByte, [1..20]
}
