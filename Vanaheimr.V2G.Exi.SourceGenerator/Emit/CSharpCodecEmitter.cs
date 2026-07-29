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

        public string Emit(SchemaPlan plan, string targetNamespace, string codecClassName) =>
            CodecEmitter.Emit(plan, targetNamespace, codecClassName);
    }
}
