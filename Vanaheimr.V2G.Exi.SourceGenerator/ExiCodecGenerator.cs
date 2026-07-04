using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Vanaheimr.V2G.Exi.SourceGenerator.Emit;
using Vanaheimr.V2G.Exi.SourceGenerator.Grammar;
using Vanaheimr.V2G.Exi.SourceGenerator.Xsd;

namespace Vanaheimr.V2G.Exi.SourceGenerator;

/// <summary>
/// Roslyn incremental source generator that produces an EXI codec from an XSD
/// schema supplied as <c>&lt;AdditionalFiles&gt;</c>.
///
/// <para>Hook-up in a consumer project:</para>
/// <code>
///   &lt;ItemGroup&gt;
///     &lt;AdditionalFiles Include="Schemas\V2G_CI_AppProtocol.xsd" /&gt;
///     &lt;ProjectReference Include="..\Vanaheimr.V2G.Exi.SourceGenerator\Vanaheimr.V2G.Exi.SourceGenerator.csproj"
///                       OutputItemType="Analyzer"
///                       ReferenceOutputAssembly="false" /&gt;
///   &lt;/ItemGroup&gt;
/// </code>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ExiCodecGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Collect ALL .xsd AdditionalFiles of the compilation as one schema set. A set may
        // span several files and namespaces linked by <xs:import>; types are resolved across
        // the whole set. (A project with a single XSD — e.g. AppProtocol — is a set of one.)
        var xsdFiles = context.AdditionalTextsProvider
            .Where(f => Path.GetExtension(f.Path).Equals(".xsd", StringComparison.OrdinalIgnoreCase))
            .Select((f, ct) => (Path: f.Path, Content: f.GetText(ct)?.ToString() ?? ""))
            .Collect();

        context.RegisterSourceOutput(xsdFiles, (spc, files) => Generate(spc, files));
    }

    private static void Generate(SourceProductionContext spc, ImmutableArray<(string Path, string Content)> files)
    {
        if (files.IsDefaultOrEmpty) return;

        var label = Path.GetFileNameWithoutExtension(files[0].Path);

        XsdSchema schema;
        try
        {
            schema = XsdReader.ParseSet(files.Select(f => f.Content));
        }
        catch (XsdReaderException ex)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.UnsupportedConstruct, Location.None, label, ex.Message));
            return;
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.XsdParseError, Location.None, label, ex.Message));
            return;
        }

        SchemaPlan plan;
        try
        {
            plan = GrammarBuilder.Build(schema);
        }
        catch (NotSupportedException ex)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.UnsupportedConstruct, Location.None, label, ex.Message));
            return;
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.InternalError, Location.None, label, ex.Message));
            return;
        }

        string source;
        try
        {
            source = CodecEmitter.Emit(plan);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.InternalError, Location.None, label, ex.Message));
            return;
        }

        spc.AddSource($"{label}.g.cs", SourceText.From(source, System.Text.Encoding.UTF8));
    }
}
