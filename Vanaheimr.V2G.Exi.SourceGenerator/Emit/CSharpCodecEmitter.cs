using System.Collections.Generic;
using Vanaheimr.V2G.Exi.SourceGenerator.Grammar;

namespace Vanaheimr.V2G.Exi.SourceGenerator.Emit
{
    /// <summary>
    /// The C# back end: the <see cref="ICodecEmitter"/> face of <see cref="CodecEmitter"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="CodecEmitter"/> stays a self-contained, single-use string builder (it carries
    /// per-emission state), so the seam is a thin stateless adapter over it rather than an
    /// interface bolted onto that class.
    /// </remarks>
    internal sealed class CSharpCodecEmitter : ICodecEmitter
    {
        public static readonly CSharpCodecEmitter Instance = new();

        public string Language      => "csharp";
        public string FileExtension => ".g.cs";

        /// <summary>
        /// One file, named after the codec class. C# has no reason to split: partial classes make
        /// the file boundary invisible to the compiler anyway, and a single compilation unit of
        /// this size costs Roslyn nothing worth avoiding.
        /// </summary>
        public IReadOnlyList<GeneratedFile> Emit(SchemaPlan plan, string targetNamespace, string codecClassName) =>
            new[]
            {
                new GeneratedFile(codecClassName + FileExtension,
                                  CodecEmitter.Emit(plan, targetNamespace, codecClassName)),
            };
    }
}
