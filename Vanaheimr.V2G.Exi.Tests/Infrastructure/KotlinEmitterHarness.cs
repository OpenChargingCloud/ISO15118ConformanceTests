using System.Text.RegularExpressions;
using NUnit.Framework;
using Vanaheimr.V2G.Exi.SourceGenerator.Emit;
using Vanaheimr.V2G.Exi.SourceGenerator.Grammar;
using Vanaheimr.V2G.Exi.SourceGenerator.Xsd;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure
{
    /// <summary>
    /// Drives the Kotlin back end over synthetic or real XSDs.
    /// </summary>
    /// <remarks>
    /// The C# back end can be reached through <see cref="GeneratorHarness"/>, because Roslyn runs
    /// it; the Kotlin one has no such route — the only production caller is the Codegen driver.
    /// This harness is the seam that lets it be unit-tested at all.
    /// </remarks>
    internal static class KotlinEmitterHarness
    {
        /// <param name="files">(file name, xsd content) pairs forming ONE schema set.</param>
        public static IReadOnlyList<GeneratedFile> Emit(
            string targetPackage, string codecObject, params (string Name, string Xsd)[] files) =>
            Emit(targetPackage, codecObject, [], files);

        public static IReadOnlyList<GeneratedFile> Emit(
            string targetPackage, string codecObject, string[] fragments,
            params (string Name, string Xsd)[] files)
        {
            var schema = XsdReader.ParseSet(files.Select(f => f.Xsd));
            var plan   = GrammarBuilder.Build(schema, fragments);
            return KotlinCodecEmitter.Instance.Emit(plan, targetPackage, codecObject);
        }

        /// <summary>Every <c>.xsd</c> of a schema set that ships with one of the sibling projects.</summary>
        public static (string Name, string Xsd)[] RealSchemaSet(string projectName)
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root is not null && !Directory.Exists(Path.Combine(root.FullName, projectName)))
                root = root.Parent;
            if (root is null)
                throw new DirectoryNotFoundException($"{projectName} not found above the test directory");

            return Directory.GetFiles(Path.Combine(root.FullName, projectName, "Schemas"), "*.xsd")
                            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))
                            .ToArray();
        }

        // ---- shapes the tests assert against ------------------------------------------------

        /// <summary>A top-level declaration: `class Foo`, `enum class Bar`, `internal fun encodeFoo`, …</summary>
        public static readonly Regex TopLevelDeclaration =
            new(@"^(?<modifier>internal |private |public )?"
              + @"(?<keyword>enum class|data class|abstract class|open class|sealed class|class|object|fun)"
              + @" (?<name>[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.Compiled);

        /// <summary>A call to a generated per-type codec, as the codec object and nested types make it.</summary>
        public static readonly Regex CodecCall =
            new(@"(?<![A-Za-z0-9_.])(?<name>(?:encode|decode)[A-Za-z_][A-Za-z0-9_]*)\s*\(",
                RegexOptions.Compiled);

        public static IEnumerable<string> Lines(GeneratedFile file) =>
            file.Source.Replace("\r\n", "\n").Split('\n');

        /// <summary>Declarations at column 0, i.e. everything the file contributes to the package.</summary>
        public static IEnumerable<Match> TopLevelDeclarations(GeneratedFile file) =>
            Lines(file).Select(l => TopLevelDeclaration.Match(l)).Where(m => m.Success);
    }
}
