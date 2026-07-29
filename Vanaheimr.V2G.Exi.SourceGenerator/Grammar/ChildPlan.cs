namespace Vanaheimr.V2G.Exi.SourceGenerator.Grammar
{
    /// <summary>
    /// Per-child plan inside a sequence — combines the value encoding with the EXI
    /// event-code wrapping (mandatory / optional / repeating).
    /// </summary>
    internal sealed record ChildPlan(
        string         FieldName,        // PascalCase as in the message record
        TypeRef        Type,             // built-in kind, or a named record/enum
        bool           IsValueType,      // of the referent; emitters derive their own nullability
        ChildShape     Shape,
        ValueEncoding  Value,
        int            ListMin = 0,      // for BoundedRepeating children
        int            ListMax = 0,
        bool           IsWildcardAny = false);   // synthetic ANY from an xs:any wildcard (two productions)
}
