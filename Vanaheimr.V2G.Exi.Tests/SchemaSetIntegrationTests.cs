using NUnit.Framework;
using Vanaheimr.V2G.Exi.Tests.Infrastructure;

namespace Vanaheimr.V2G.Exi.Tests;

/// <summary>
/// Phase 2 integration gate: run the generator over the full ISO 15118-2 schema set and require
/// zero diagnostics AND that the generated codec compiles (Phase 2 Definition of Done #1). The
/// per-message byte conformance against cbV2G is a separate step (differential vectors).
/// </summary>
[TestFixture]
public class SchemaSetIntegrationTests
{
    private static (string GeneratedSource, Microsoft.CodeAnalysis.Diagnostic[] Diagnostics) GenerateFullSet()
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
        return (r.GeneratedSource, r.Diagnostics.ToArray());
    }

    [Test]
    public void FullIso2SchemaSet_GeneratesWithoutDiagnostics()
    {
        var (_, diagnostics) = GenerateFullSet();

        var msg = $"{diagnostics.Length} diagnostics:\n";
        foreach (var d in diagnostics)
            msg += "  " + d.GetMessage() + "\n";
        Assert.That(diagnostics.Length, Is.Zero, msg);
    }

    [Test]
    public void FullIso2SchemaSet_GeneratedCodecCompiles()
    {
        var (source, diagnostics) = GenerateFullSet();
        Assert.That(diagnostics.Length, Is.Zero, "generation must be diagnostic-free first");

        var errors = GeneratorHarness.CompileErrors(source, typeof(Vanaheimr.V2G.Exi.ExiPrimitives));
        Assert.That(errors, Is.Empty,
            string.Join("\n", errors.Select(e => e.ToString())));
    }
}
