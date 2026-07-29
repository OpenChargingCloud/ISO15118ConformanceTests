using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vanaheimr.V2G.Exi.SourceGenerator.Emit;
using Vanaheimr.V2G.Exi.SourceGenerator.Grammar;
using Vanaheimr.V2G.Exi.SourceGenerator.Xsd;

namespace Vanaheimr.V2G.Exi.Codegen
{
    /// <summary>
    /// Roslyn-free driver for the EXI codec generator.
    ///
    /// <para>
    /// The incremental generator can only contribute C# to the compilation it runs in, so every
    /// other target language needs a driver that runs the same front end
    /// (<see cref="XsdReader"/> → <see cref="GrammarBuilder"/>) and writes the chosen
    /// <see cref="ICodecEmitter"/>'s output to disk. This is that driver.
    /// </para>
    ///
    /// <para>
    /// It also serves as a differential test of the seam: emitting <c>--lang csharp</c> for a
    /// schema set must reproduce, byte for byte, what the Roslyn generator puts in
    /// <c>obj/.../generated/</c> for the same inputs.
    /// </para>
    /// </summary>
    public static class Program
    {
        private static readonly ICodecEmitter[] Emitters =
        [
            CSharpCodecEmitter.Instance,
            KotlinCodecEmitter.Instance,
        ];

        public static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (UsageException ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                Console.Error.WriteLine();
                Usage();
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            var opts = Options.Parse(args);

            var emitter = Emitters.FirstOrDefault(e =>
                              string.Equals(e.Language, opts.Language, StringComparison.OrdinalIgnoreCase))
                          ?? throw new UsageException(
                                 $"unknown --lang '{opts.Language}'. Known: " +
                                 string.Join(", ", Emitters.Select(e => e.Language)));

            var schema = XsdReader.ParseSet(opts.XsdPaths.Select(File.ReadAllText));
            var plan   = GrammarBuilder.Build(schema, opts.Fragments);
            var source = emitter.Emit(plan, opts.Namespace, opts.CodecClass);

            // The schema set's first file names the output, matching the incremental generator's
            // hint-name convention (`{label}.g.cs`).
            var label      = Path.GetFileNameWithoutExtension(opts.XsdPaths[0]);
            var outputPath = Directory.Exists(opts.Output) || opts.Output.EndsWith("/", StringComparison.Ordinal)
                                 ? Path.Combine(opts.Output, label + emitter.FileExtension)
                                 : opts.Output;

            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // UTF-8 without BOM, '\n' preserved exactly as the emitter produced it — a byte-diff
            // against the Roslyn output must not trip over encoding or line endings.
            File.WriteAllText(outputPath, source, new System.Text.UTF8Encoding(false));

            Console.WriteLine($"{emitter.Language}: {outputPath} ({source.Length:N0} chars)");
            return 0;
        }

        private static void Usage()
        {
            Console.Error.WriteLine("""
                Usage:
                  Vanaheimr.V2G.Exi.Codegen --xsd <file>[;<file>...] --out <path>
                                            [--lang csharp|kotlin]
                                            [--namespace <ns>] [--codec <class>]
                                            [--fragments <Elem>[,<Elem>...]]

                  --xsd        One or more XSD files forming ONE schema set (types resolve across
                               the whole set). Repeatable, or ';'-separated.
                  --out        Output file, or a directory (then named after the first XSD).
                  --lang       Target language. Default: csharp.
                  --namespace  Generated namespace / package.
                  --codec      Generated codec class name.
                  --fragments  Global elements to emit EXI fragment codecs for (XMLDSig).
                """);
        }

        private sealed class UsageException(string message) : Exception(message);

        private sealed record Options(
            IReadOnlyList<string> XsdPaths,
            string                Output,
            string                Language,
            string                Namespace,
            string                CodecClass,
            string[]              Fragments)
        {
            public static Options Parse(string[] args)
            {
                var xsds       = new List<string>();
                string? output = null, lang = null, ns = null, codec = null;
                var fragments  = Array.Empty<string>();

                for (var i = 0; i < args.Length; i++)
                {
                    string Next(string flag) =>
                        i + 1 < args.Length ? args[++i] : throw new UsageException($"{flag} needs a value.");

                    switch (args[i])
                    {
                        case "--xsd":
                            xsds.AddRange(Next("--xsd").Split([';'], StringSplitOptions.RemoveEmptyEntries));
                            break;
                        case "--out":       output    = Next("--out");       break;
                        case "--lang":      lang      = Next("--lang");      break;
                        case "--namespace": ns        = Next("--namespace"); break;
                        case "--codec":     codec     = Next("--codec");     break;
                        case "--fragments":
                            fragments = Next("--fragments")
                                        .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
                            break;
                        default:
                            throw new UsageException($"unexpected argument '{args[i]}'.");
                    }
                }

                if (xsds.Count == 0) throw new UsageException("--xsd is required.");
                if (output is null)  throw new UsageException("--out is required.");

                foreach (var p in xsds)
                    if (!File.Exists(p))
                        throw new UsageException($"XSD not found: {p}");

                return new Options(
                    xsds,
                    output,
                    lang  ?? "csharp",
                    ns    ?? "Generated",
                    codec ?? "Codec",
                    fragments);
            }
        }
    }
}
