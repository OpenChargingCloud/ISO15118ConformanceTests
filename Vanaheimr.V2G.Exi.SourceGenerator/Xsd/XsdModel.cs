using System.Collections.Generic;

namespace Vanaheimr.V2G.Exi.SourceGenerator.Xsd;

/// <summary>
/// In-memory model of the XSD subset the generator understands.
/// Deliberately minimal: only the constructs needed for V2G_CI_AppProtocol.xsd.
/// </summary>
internal sealed class XsdSchema
{
    public string TargetNamespace { get; set; } = "";
    public List<XsdElement> GlobalElements { get; } = new();
    public Dictionary<string, XsdSimpleType> SimpleTypes { get; } = new();
    public Dictionary<string, XsdComplexType> ComplexTypes { get; } = new();
}

/// <summary>An element declaration: <c>&lt;xs:element name="..." type="..." minOccurs maxOccurs/&gt;</c>.</summary>
internal sealed record XsdElement(
    string Name,
    string TypeRef,         // "xs:unsignedInt", "AppProtocolType", or "" if inline complex type
    int    MinOccurs,
    int    MaxOccurs,       // int.MaxValue ≡ "unbounded"
    XsdComplexType? InlineType,
    string? Ref              = null,   // <xs:element ref="Head"/> — local name of the referenced element
    string? SubstitutionGroup = null,  // on a global element: the head it substitutes (local name)
    bool    IsAbstract        = false);// on a global element: abstract="true" (substitution head)

/// <summary>
/// A complex type with element content. <see cref="Sequence"/> holds the type's OWN
/// particles; if <see cref="BaseTypeRef"/> is set (xs:complexContent/xs:extension), the
/// grammar builder prepends the (recursively flattened) base particles.
/// </summary>
internal sealed record XsdComplexType(
    string                    Name,
    IReadOnlyList<XsdElement>  Sequence,
    string?                   BaseTypeRef = null,   // extension base (may carry a prefix)
    bool                      IsAbstract  = false);

/// <summary>
/// Simple type derived by restriction from a single base. Value-space facets cover
/// only what AppProtocol uses: integer min/max bounds, string maxLength, and enumeration.
/// </summary>
internal sealed class XsdSimpleType
{
    public string Name { get; set; } = "";
    public string Base { get; set; } = "";   // e.g. "xs:unsignedByte" or "xs:string"

    public long?  MinInclusive { get; set; }
    public long?  MaxInclusive { get; set; }
    public int?   MaxLength    { get; set; }

    /// <summary>Lexicographically sorted (per EXI canonical ordering).</summary>
    public List<string>? Enumeration { get; set; }
}
