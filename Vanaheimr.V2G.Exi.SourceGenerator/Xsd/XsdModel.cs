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

    /// <summary>
    /// Local names of global elements declared in an <em>opaque</em> namespace — one whose
    /// full grammar the generator deliberately does not model yet (currently only XMLDSig).
    /// Types from such a namespace are never built; a reference to one of these elements
    /// becomes an opaque, encode-absent/round-trip-only child (see the Signature reference
    /// in the ISO 15118-2 message header). Full fidelity is deferred to Phase 3.
    /// </summary>
    public HashSet<string> OpaqueElementNames { get; } = new();

    /// <summary>
    /// Every element declaration of the collected set (global AND local, all namespaces incl.
    /// XMLDSig), as (localName, namespace) pairs. This is the production set of the EXI fragment
    /// grammar (§8.5.3): sorted by name then namespace, each gets an event code (used to encode a
    /// signable element as a standalone fragment for XMLDSig).
    /// </summary>
    public HashSet<(string Name, string Namespace)> AllElementDeclarations { get; } = new();

    /// <summary>Maps an element declaration (name, namespace) to its <c>type</c> reference, so a
    /// signable fragment element (which may be local, e.g. SalesTariff) can be tied to its record.</summary>
    public Dictionary<(string Name, string Namespace), string> ElementTypeRefs { get; } = new();
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
    bool    IsAbstract        = false, // on a global element: abstract="true" (substitution head)
    XsdSimpleType? InlineSimpleType = null) // anonymous inline xs:simpleType
{
    /// <summary>For a global element: its target namespace (used to order the document grammar,
    /// which enumerates every global element across the collected set).</summary>
    public string Namespace { get; init; } = "";
}

/// <summary>
/// A complex type with element content. <see cref="Sequence"/> holds the type's OWN
/// particles; if <see cref="BaseTypeRef"/> is set (xs:complexContent/xs:extension), the
/// grammar builder prepends the (recursively flattened) base particles.
/// </summary>
internal sealed record XsdComplexType(
    string                    Name,
    IReadOnlyList<XsdElement>  Sequence,
    string?                   BaseTypeRef = null,   // extension base (may carry a prefix)
    bool                      IsAbstract  = false,
    IReadOnlyList<XsdAttribute>? Attributes = null,
    IReadOnlyList<XsdElement>? Choice = null,        // xs:choice content (mutually exclusive with Sequence)
    string?                   SimpleContentBase = null); // xs:simpleContent/xs:extension base (a value + attributes)

/// <summary>An <c>&lt;xs:attribute name="..." type="..." use="..."/&gt;</c> on a complex type.</summary>
internal sealed record XsdAttribute(string Name, string TypeRef, bool Required);

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
