namespace Vanaheimr.V2G.Exi.SourceGenerator.Grammar;

/// <summary>
/// Per-child plan inside a sequence — combines the value encoding with the EXI
/// event-code wrapping (mandatory / optional / repeating).
/// </summary>
internal sealed record ChildPlan(
    string         FieldName,        // PascalCase as in the message record
    string         CSharpType,       // "uint", "byte", "string", "AppProtocolEntry"
    bool           IsCSharpNullable, // for optional value-types only
    ChildShape     Shape,
    ValueEncoding  Value,
    int            ListMin = 0,      // for BoundedRepeating children
    int            ListMax = 0,
    bool           IsWildcardAny = false);   // synthetic ANY from an xs:any wildcard (two productions)
