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
        /// One file per type, plus one for the codec class — the same layout as the Kotlin back
        /// end, reached by making the codec class <c>partial</c> rather than by moving anything out
        /// of it — and one more for the JSON-LD (de)serializer.
        /// </summary>
        /// <remarks>
        /// The JSON-LD pass is part of this emitter rather than an emitter of its own, and that is
        /// docs/CONCEPT.md §4.4's actual requirement: "wire codec and JSON-LD codec come from the
        /// same type graph in the same generator pass, so they cannot drift". Two emitters would
        /// leave a seam where someone could regenerate one and not the other; there is no such seam
        /// here.
        /// </remarks>
        public IReadOnlyList<GeneratedFile> Emit(SchemaPlan plan, string targetNamespace, string codecClassName) =>
        [
            .. CodecEmitter.Emit    (plan, targetNamespace, codecClassName),
            .. CSharpJsonEmitter.Emit(plan, targetNamespace, codecClassName),
        ];
    }
}
