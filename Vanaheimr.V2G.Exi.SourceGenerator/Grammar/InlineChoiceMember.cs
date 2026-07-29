namespace Vanaheimr.V2G.Exi.SourceGenerator.Grammar
{
    /// <summary>One branch of a <see cref="ValueEncoding.InlineChoice"/> — an independent, always-nullable
    /// field in the enclosing record (only one branch is ever set).</summary>
    internal sealed record InlineChoiceMember(
        string        ElementName,
        string        FieldName,
        TypeRef       Type,
        ValueEncoding Value,
        bool          IsValueType);
}
