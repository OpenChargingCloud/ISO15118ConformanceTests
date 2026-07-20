using System.Collections.Generic;

namespace Vanaheimr.V2G.Exi.SourceGenerator.Xsd;

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
