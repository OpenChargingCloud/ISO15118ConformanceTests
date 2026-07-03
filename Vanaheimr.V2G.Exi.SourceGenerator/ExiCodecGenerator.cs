using System;
using System.IO;
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
        var xsdFiles = context.AdditionalTextsProvider
            .Where(f => Path.GetExtension(f.Path).Equals(".xsd", StringComparison.OrdinalIgnoreCase))
            .Select((f, ct) =>
            {
                var src = f.GetText(ct)?.ToString() ?? "";
                return (Path: f.Path, Content: src);
            });

        context.RegisterSourceOutput(xsdFiles, (spc, file) => Generate(spc, file.Path, file.Content));
    }

    private static void Generate(SourceProductionContext spc, string path, string content)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);

        XsdSchema schema;
        try
        {
            schema = XsdReader.Parse(content);
        }
        catch (XsdReaderException ex)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.UnsupportedConstruct, Location.None, fileName, ex.Message));
            return;
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.XsdParseError, Location.None, fileName, ex.Message));
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
                Diagnostics.UnsupportedConstruct, Location.None, fileName, ex.Message));
            return;
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.InternalError, Location.None, fileName, ex.Message));
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
                Diagnostics.InternalError, Location.None, fileName, ex.Message));
            return;
        }

        spc.AddSource($"{fileName}.g.cs", SourceText.From(source, System.Text.Encoding.UTF8));
    }
}
