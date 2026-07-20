namespace Vanaheimr.V2G.Exi.SourceGenerator.Grammar;

/// <summary>One production of a <see cref="ValueEncoding.SubstitutionChoice"/>.</summary>
internal sealed record SubstMember(string ElementName, string CSharpTypeName, bool IsAbstractHead);
