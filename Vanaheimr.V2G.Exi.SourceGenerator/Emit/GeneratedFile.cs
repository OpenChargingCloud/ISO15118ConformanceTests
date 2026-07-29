namespace Vanaheimr.V2G.Exi.SourceGenerator.Emit
{
    /// <summary>
    /// One file an <see cref="ICodecEmitter"/> produces.
    /// </summary>
    /// <param name="FileName">
    /// File name including the extension, without any directory part — the emitter decides how it
    /// splits its output, so it also decides what the pieces are called. Doubles as the Roslyn
    /// generator's hint name.
    /// </param>
    /// <param name="Source">The file's complete source text.</param>
    internal sealed record GeneratedFile(string FileName, string Source);
}
