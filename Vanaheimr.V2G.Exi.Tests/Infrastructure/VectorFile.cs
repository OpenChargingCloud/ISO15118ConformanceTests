using System.Text.Json.Serialization;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure
{
    /// <summary>Top-level golden-vector JSON document.</summary>
    public sealed record VectorFile(
        [property: JsonPropertyName("schemaVersion")]   int      SchemaVersion,
        [property: JsonPropertyName("generator")]       string   Generator,
        [property: JsonPropertyName("generatorNote")]   string?  GeneratorNote,
        [property: JsonPropertyName("generatedAtUtc")]  string   GeneratedAtUtc,
        [property: JsonPropertyName("vectors")]         Vector[] Vectors);
}
