using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure
{
    /// <summary>One test vector. <see cref="Input"/> is schema-dependent; tests parse it per <see cref="MessageType"/>.</summary>
    public sealed record Vector(
        [property: JsonPropertyName("name")]          string      Name,
        [property: JsonPropertyName("description")]   string      Description,
        [property: JsonPropertyName("messageType")]   string      MessageType,
        [property: JsonPropertyName("input")]         JsonElement Input,
        [property: JsonPropertyName("expectedBytes")] int         ExpectedBytes,
        [property: JsonPropertyName("expectedHex")]   string      ExpectedHex)
    {
        // Keeps NUnit's auto-generated test names short; the explicit SetName in
        // AppProtocolVectorSource takes precedence anyway, but TRX output and
        // diagnostics also use this.
        public override string ToString() => $"{MessageType}/{Name}";
    }
}
