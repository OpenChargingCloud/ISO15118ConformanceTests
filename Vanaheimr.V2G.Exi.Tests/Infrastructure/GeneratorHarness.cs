using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Vanaheimr.V2G.Exi.SourceGenerator;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure;

/// <summary>
/// Drives <see cref="ExiCodecGenerator"/> over synthetic mini-XSDs so the grammar/emit
/// pipeline can be unit-tested construct by construct: feed one or more <c>.xsd</c>
/// documents, inspect the generator's diagnostics and the generated C# source.
/// </summary>
public static class GeneratorHarness
{
    public sealed record Result(ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource)
    {
        public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error ||
                                                      d.Severity == DiagnosticSeverity.Warning);
    }

    /// <param name="files">(fileName ending in .xsd, xsd content) pairs — one schema set.</param>
    public static Result Run(params (string name, string xsd)[] files)
    {
        var compilation = CSharpCompilation.Create(
            "GrammarTestAsm",
            syntaxTrees: null,
            references: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var additional = files
            .Select(f => (AdditionalText)new InMemoryAdditionalText(f.name, f.xsd))
            .ToImmutableArray();

        GeneratorDriver driver = CSharpGeneratorDriver
            .Create(new ExiCodecGenerator().AsSourceGenerator())
            .AddAdditionalTexts(additional);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var perGenerator = runResult.Results.Single();
        var source = string.Concat(runResult.GeneratedTrees.Select(t => t.ToString()));
        return new Result(perGenerator.Diagnostics, source);
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly string _text;
        public InMemoryAdditionalText(string path, string text) { Path = path; _text = text; }
        public override string Path { get; }
        public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default)
            => SourceText.From(_text, System.Text.Encoding.UTF8);
    }
}
