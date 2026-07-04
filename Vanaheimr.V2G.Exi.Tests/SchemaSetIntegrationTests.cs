using NUnit.Framework;
using Vanaheimr.V2G.Exi.Tests.Infrastructure;

namespace Vanaheimr.V2G.Exi.Tests;

/// <summary>
/// Phase 2 integration gate: run the generator over the full ISO 15118-2 schema set and
/// require zero diagnostics. Marked <c>[Explicit]</c> because the generator does not yet
/// handle every construct in the set — run it manually to see which constructs remain
/// (the failure message lists them). When it goes green the whole set generates.
/// </summary>
[TestFixture]
public class SchemaSetIntegrationTests
{
    [Test, Explicit("Full -2 schema set; run to list remaining unsupported constructs.")]
    public void FullIso2SchemaSet_GeneratesWithoutDiagnostics()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "Vanaheimr.V2G.Exi.Iso15118_2")))
            root = root.Parent;
        Assert.That(root, Is.Not.Null, "repo root not found");

        var schemaDir = Path.Combine(root!.FullName, "Vanaheimr.V2G.Exi.Iso15118_2", "Schemas");
        var files = new List<(string, string)>();
        foreach (var f in Directory.GetFiles(schemaDir, "*.xsd"))
            files.Add((Path.GetFileName(f), File.ReadAllText(f)));

        var r = GeneratorHarness.Run(files.ToArray());

        var msg = $"{r.Diagnostics.Length} diagnostics, {r.GeneratedSource.Length} generated chars:\n";
        foreach (var d in r.Diagnostics)
            msg += "  " + d.GetMessage() + "\n";
        Assert.That(r.Diagnostics.Length, Is.Zero, msg);
    }
}
