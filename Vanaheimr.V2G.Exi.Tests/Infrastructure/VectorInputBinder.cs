using System.Text.Json;
using Vanaheimr.V2G.AppProtocol;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure
{
    /// <summary>
    /// Maps a <see cref="Vector"/>'s <c>input</c> JSON onto strongly-typed message objects.
    /// Kept separate from the data source so it's easy to extend per new message type.
    /// </summary>
    public static class VectorInputBinder
    {
        public static SupportedAppProtocolReq BindRequest(JsonElement input)
        {
            var entries = new List<AppProtocolEntry>();
            foreach (var e in input.GetProperty("appProtocols").EnumerateArray())
            {
                entries.Add(new AppProtocolEntry(
                    ProtocolNamespace : e.GetProperty("protocolNamespace").GetString()!,
                    VersionNumberMajor: e.GetProperty("versionNumberMajor").GetUInt32(),
                    VersionNumberMinor: e.GetProperty("versionNumberMinor").GetUInt32(),
                    SchemaID          : e.GetProperty("schemaId").GetByte(),
                    Priority          : e.GetProperty("priority").GetByte()));
            }
            return new SupportedAppProtocolReq(entries);
        }

        public static SupportedAppProtocolRes BindResponse(JsonElement input)
        {
            var code = Enum.Parse<ResponseCode>(input.GetProperty("code").GetString()!);

            byte? schemaId = null;
            if (input.TryGetProperty("schemaId", out var sid) &&
                sid.ValueKind != JsonValueKind.Null)
            {
                schemaId = sid.GetByte();
            }
            return new SupportedAppProtocolRes(code, schemaId);
        }
    }
}
