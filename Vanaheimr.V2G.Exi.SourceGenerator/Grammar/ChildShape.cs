namespace Vanaheimr.V2G.Exi.SourceGenerator.Grammar;

internal enum ChildShape
{
    /// <summary>minOccurs=1, maxOccurs=1 — zero-bit transition.</summary>
    RequiredSingle,
    /// <summary>minOccurs=0, maxOccurs=1 — one-bit SE/EE choice.</summary>
    OptionalSingle,
    /// <summary>maxOccurs &gt; 1 — list with EE termination, requires <see cref="ListMin"/>/<see cref="ListMax"/>.</summary>
    BoundedRepeating,
}
