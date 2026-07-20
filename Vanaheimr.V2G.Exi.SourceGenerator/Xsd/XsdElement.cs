using System.Collections.Generic;

namespace Vanaheimr.V2G.Exi.SourceGenerator.Xsd;

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

    /// <summary>True for the synthetic <c>ANY</c> element standing in for an <c>xs:any</c> wildcard.
    /// cbexigen expands a wildcard into TWO grammar productions — a generic wildcard event and the
    /// typed element it simplifies to — with the element EE between them, so such an element counts
    /// as two productions when sizing a grammar state's event code.</summary>
    public bool IsWildcard { get; init; }

    /// <summary>Non-null for the synthetic marker standing in for an inline <c>xs:choice</c>
    /// particle nested in a <c>xs:sequence</c> (ISO 15118-20, e.g. <c>AuthorizationSetupResType</c>'s
    /// trailing <c>EIM_.../PnC_...</c> choice) — as opposed to a substitution-group reference or a
    /// whole-content root choice, both already modelled. Each entry is one <c>&lt;xs:element
    /// name="..." type="..."/&gt;</c> child of the <c>&lt;xs:choice&gt;</c>, in document order (cbexigen
    /// assigns event codes in document order, not alphabetically — verified against
    /// <c>SignedInstallationDataType</c>'s 3-member choice, where the two orders diverge).</summary>
    public IReadOnlyList<XsdElement>? InlineChoiceMembers { get; init; }
}
