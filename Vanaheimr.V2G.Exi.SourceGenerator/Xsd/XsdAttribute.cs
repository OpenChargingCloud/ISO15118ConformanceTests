namespace Vanaheimr.V2G.Exi.SourceGenerator.Xsd;

/// <summary>An <c>&lt;xs:attribute name="..." type="..." use="..."/&gt;</c> on a complex type.</summary>
internal sealed record XsdAttribute(string Name, string TypeRef, bool Required);
