using System.Text.Json;
using NUnit.Framework;
using Vanaheimr.V2G.AppProtocol;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure;

/// <summary>
/// Provides parameterised test data for the AppProtocol codec.
/// Loads every <c>Vectors\*.vectors.json</c> file copied next to the test assembly
/// and yields one <see cref="TestCaseData"/> per vector. The vector's <c>name</c>
/// is used as the test case name so failures show up readably.
/// </summary>
public static class AppProtocolVectorSource
{
    public static IEnumerable<TestCaseData> All()
    {
        var dir = Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors");
        if (!Directory.Exists(dir)) yield break;

        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var file in Directory.EnumerateFiles(dir, "*.vectors.json"))
        {
            var doc = JsonSerializer.Deserialize<VectorFile>(File.ReadAllText(file), jsonOpts)
                      ?? throw new InvalidDataException($"Empty vector file: {file}");

            foreach (var v in doc.Vectors)
            {
                yield return new TestCaseData(Path.GetFileName(file), v)
                    .SetName($"{{m}}({v.Name})");
            }
        }
    }
}

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
