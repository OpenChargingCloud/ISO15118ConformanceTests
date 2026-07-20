using System.Text.Json;
using NUnit.Framework;

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

        // Only the AppProtocol vector files — other *.vectors.json (e.g. Primitives) have
        // their own shape and their own test source.
        foreach (var file in Directory.EnumerateFiles(dir, "AppProtocol*.vectors.json"))
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
