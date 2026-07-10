using System;
using System.Collections.Generic;
using System.Linq;
using Vanaheimr.V2G.Exi.SourceGenerator.Xsd;

namespace Vanaheimr.V2G.Exi.SourceGenerator.Grammar;

/// <summary>
/// Per-element value encoding plan: which EXI primitive codec applies and with
/// what parameters.
/// </summary>
internal abstract record ValueEncoding
{
    public sealed record UnsignedInt : ValueEncoding;
    public sealed record SignedInt : ValueEncoding;            // xs:byte/short/int/long → EXI Integer
    public sealed record Binary : ValueEncoding;               // xs:hexBinary / xs:base64Binary → byte[]
    public sealed record StringValue : ValueEncoding;

    /// <summary>
    /// An attribute (AT) value carried inside an optional run: unlike an element, it is a bare
    /// string with no SE / value-start / child-EE wrapper — only the run's event code precedes it.
    /// </summary>
    public sealed record AttributeValue : ValueEncoding;
    public sealed record NBitUnsigned(int BitWidth, long Bias) : ValueEncoding;
    public sealed record EnumIndex(string EnumName, int BitWidth, IReadOnlyList<string> Members) : ValueEncoding;
    public sealed record ComplexRef(string TypeName) : ValueEncoding;

    /// <summary>
    /// A reference to an element in an opaque namespace (XMLDSig). Its grammar is not modelled;
    /// the child is only ever encoded/decoded as <em>absent</em>. Encoding or decoding a present
    /// instance fails loud — full fidelity is deferred to Phase 3. <see cref="TypeName"/> is the
    /// generated empty placeholder record.
    /// </summary>
    public sealed record OpaqueElement(string TypeName) : ValueEncoding;

    /// <summary>
    /// A reference to a substitution-group head: the value is one of several concrete member
    /// types, selected by an n-bit event code. Members are sorted by element name and include
    /// the abstract head element itself (cbexigen assigns it a production slot too).
    /// </summary>
    public sealed record SubstitutionChoice(int BitWidth, IReadOnlyList<SubstMember> Members) : ValueEncoding;

    /// <summary>
    /// An <c>xs:choice</c> nested inside a sequence (ISO 15118-20), flattened into the enclosing
    /// state exactly like a substitution reference — but unlike substitution, each branch is its
    /// OWN independent field in the record, not one polymorphic field (cbexigen models an inline
    /// choice as N sibling <c>_isUsed</c>-flagged fields, verified against
    /// <c>iso20_AuthorizationSetupResType</c>). <see cref="BitWidth"/> is only used for the
    /// standalone dispatch (no surrounding optional run); the run machine sizes the shared state
    /// itself via <c>ProductionCount</c>. Members keep XSD document order (not alphabetical —
    /// verified against <c>SignedInstallationDataType</c>'s 3-member choice).
    /// </summary>
    public sealed record InlineChoice(int BitWidth, IReadOnlyList<InlineChoiceMember> Members) : ValueEncoding;
}

/// <summary>One production of a <see cref="ValueEncoding.SubstitutionChoice"/>.</summary>
internal sealed record SubstMember(string ElementName, string CSharpTypeName, bool IsAbstractHead);

/// <summary>One branch of a <see cref="ValueEncoding.InlineChoice"/> — an independent, always-nullable
/// field in the enclosing record (only one branch is ever set).</summary>
internal sealed record InlineChoiceMember(
    string        ElementName,
    string        FieldName,
    string        CSharpType,
    ValueEncoding Value,
    bool          IsCSharpNullable);

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

internal enum ChildShape
{
    /// <summary>minOccurs=1, maxOccurs=1 — zero-bit transition.</summary>
    RequiredSingle,
    /// <summary>minOccurs=0, maxOccurs=1 — one-bit SE/EE choice.</summary>
    OptionalSingle,
    /// <summary>maxOccurs &gt; 1 — list with EE termination, requires <see cref="ListMin"/>/<see cref="ListMax"/>.</summary>
    BoundedRepeating,
}

internal sealed record SequencePlan(
    string                   CSharpRecordName,  // e.g. "AppProtocolEntry"
    IReadOnlyList<ChildPlan> Children,
    int                      ListMin = 0,
    int                      ListMax = 0,
    bool                     IsAbstract = false, // emit as `abstract record`
    string?                  BaseRecordName = null, // extension/substitution base record
    IReadOnlyList<AttrPlan>? Attributes = null,    // AT events (sorted by name), before content
    bool                     IsChoice = false,      // Children are mutually-exclusive xs:choice alternatives
    ValueEncoding?           SimpleContent = null,  // xs:simpleContent: the single content value's encoding
    string?                  SimpleContentType = null);

/// <summary>An attribute (AT event) of a complex type.</summary>
internal sealed record AttrPlan(string FieldName, string CSharpType, ValueEncoding Value, bool Required = false);

/// <summary>
/// Top-level plan for a global element.
/// </summary>
internal sealed record GlobalElementPlan(
    string        XsdName,            // e.g. "supportedAppProtocolReq"
    string        CSharpTypeName,     // "SupportedAppProtocolReq"
    SequencePlan  Body,
    int           DocumentIndex);     // production index in the (full) document grammar

/// <summary>
/// The full plan for one schema, ready for emission.
/// </summary>
internal sealed record SchemaPlan(
    string                          TargetNamespace,
    IReadOnlyList<GlobalElementPlan> GlobalElements,
    IReadOnlyDictionary<string, SequencePlan> ComplexTypes,
    IReadOnlyList<EnumPlan>         Enums,
    IReadOnlyList<string>           OpaqueTypes,   // empty placeholder records for opaque refs
    int                             DocumentSelectorBits, // width of the document element selector
    int                             FragmentSelectorBits, // width of the EXI fragment element selector
    int                             FragmentEndCode,      // "End Fragment" (ED) event code
    IReadOnlyList<FragmentPlan>     Fragments);    // signable elements to emit fragment codecs for

/// <summary>A signable element that gets an EXI fragment encoder/decoder: its fragment-grammar
/// event code and the C# record that carries its content.</summary>
internal sealed record FragmentPlan(string ElementName, string CSharpTypeName, int EventCode);

internal sealed record EnumPlan(string Name, IReadOnlyList<string> Members);

/// <summary>
/// Lowers an <see cref="XsdSchema"/> to a <see cref="SchemaPlan"/> that the
/// emitter can consume mechanically.
/// </summary>
internal static class GrammarBuilder
{
    private const string XmlDsigNamespace = "http://www.w3.org/2000/09/xmldsig#";

    public static SchemaPlan Build(XsdSchema schema) => Build(schema, System.Array.Empty<string>());

    public static SchemaPlan Build(XsdSchema schema, IReadOnlyList<string> fragmentElements)
    {
        var enums = new List<EnumPlan>();
        var opaqueTypes = new List<string>();

        // Build per-named-complexType plans.
        var complex = new Dictionary<string, SequencePlan>();
        foreach (var kv in schema.ComplexTypes)
            complex[kv.Key] = BuildSequence(kv.Key, kv.Value, schema, enums, opaqueTypes);

        // The document grammar enumerates EVERY global element of the collected set (abstract
        // substitution heads, their members, opaque XMLDSig elements, …), sorted by element name
        // then namespace — cbexigen assigns each a production even though only true roots are
        // decodable. The selector width and each root's index come from this full list (verified
        // against cbV2G: V2G_Message is index 76 of 80, a 7-bit selector).
        var docOrder = schema.GlobalElements
            .Select(g => (g.Name, g.Namespace))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Namespace, StringComparer.Ordinal)
            .ToList();
        int docBits = BitsForChoices(docOrder.Count + 1);

        // Build global-element plans for the true document roots — a concrete, non-substituting,
        // non-opaque global element (V2G_Message; supportedAppProtocolReq/Res). Abstract heads and
        // substitution members are reached through the substitution choice, not as roots.
        var globals = new List<GlobalElementPlan>();
        foreach (var ge in schema.GlobalElements)
        {
            if (ge.IsAbstract || ge.SubstitutionGroup is not null || ge.Ref is not null)
                continue;
            if (schema.OpaqueElementNames.Contains(ge.Name))
                continue;
            // Whitelisted XMLDSig SignedInfo-subtree elements are modelled (their type codecs exist)
            // but are never V2G document roots — they are reached only through a fragment or a
            // containing type. They still occupy a document-grammar production (counted above).
            if (ge.Namespace == XmlDsigNamespace)
                continue;

            var typeName = PascalCase(ge.Name);

            SequencePlan body;
            if (ge.InlineType is not null)
            {
                body = BuildSequence(typeName, ge.InlineType, schema, enums, opaqueTypes);
                complex[typeName] = body;
            }
            else if (complex.TryGetValue(StripPrefix(ge.TypeRef), out var named))
            {
                body = named with { CSharpRecordName = typeName };
                complex[typeName] = body;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Global element '{ge.Name}' references unknown complex type '{ge.TypeRef}'.");
            }

            int docIndex = docOrder.FindIndex(x => x.Name == ge.Name && x.Namespace == ge.Namespace);
            globals.Add(new GlobalElementPlan(ge.Name, typeName, body, docIndex));
        }

        // Fragment grammar: every element declaration of the set (global + local, all namespaces),
        // sorted by name then namespace, gets an event code. Signable elements named by the caller
        // get a fragment codec (their content encoder already exists).
        var fragOrder = schema.AllElementDeclarations
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Namespace, StringComparer.Ordinal)
            .ToList();
        int fragBits = BitsForChoices(fragOrder.Count + 1);
        // FragmentContent productions: one SE per element (0..n-1), a generic slot (n), then ED (n+1)
        // — cbexigen's non-strict fragment grammar; the End-Fragment event code is n+1.
        int fragEnd = fragOrder.Count + 1;

        var fragments = new List<FragmentPlan>();
        foreach (var name in fragmentElements)
        {
            var decls = fragOrder.Where(x => x.Name == name).ToList();
            if (decls.Count != 1)
                throw new InvalidOperationException(decls.Count == 0
                    ? $"fragment element '{name}' is not an element declaration of the set."
                    : $"fragment element '{name}' is declared in {decls.Count} namespaces; disambiguation not supported.");
            var key = decls[0];
            int code = fragOrder.IndexOf(key);
            if (!schema.ElementTypeRefs.TryGetValue(key, out var typeRef))
                throw new InvalidOperationException($"fragment element '{name}' has no named type (inline types are not supported).");
            var local = StripPrefix(typeRef);
            if (!complex.ContainsKey(local))
                throw new InvalidOperationException($"fragment element '{name}': type '{typeRef}' is not modelled.");
            fragments.Add(new FragmentPlan(name, PascalCase(local), code));
        }

        return new SchemaPlan(schema.TargetNamespace, globals, complex, enums,
            opaqueTypes.Distinct().ToList(), docBits, fragBits, fragEnd, fragments);
    }

    private static SequencePlan BuildSequence(
        string ctName, XsdComplexType ct, XsdSchema schema, List<EnumPlan> enums,
        List<string> opaqueTypes)
    {
        var baseRecord = ct.BaseTypeRef is null ? null : PascalCase(StripPrefix(ct.BaseTypeRef));

        // Attributes (AT events) precede the content, in lexicographic name order.
        IReadOnlyList<AttrPlan>? attrPlans = null;
        if (ct.Attributes is { Count: > 0 })
        {
            var list = new List<AttrPlan>();
            foreach (var a in ct.Attributes.OrderBy(a => a.Name, StringComparer.Ordinal))
            {
                var (csType, val, _) = ResolveTypeRef(a.TypeRef, schema, enums, a.Name);
                list.Add(new AttrPlan(PascalCase(a.Name), csType, val, a.Required));
            }
            attrPlans = list;
        }

        // xs:simpleContent — a single content value plus attributes.
        if (ct.SimpleContentBase is not null)
        {
            var (scType, scVal, _) = ResolveTypeRef(ct.SimpleContentBase, schema, enums, ctName);
            return new SequencePlan(PascalCase(ctName), System.Array.Empty<ChildPlan>(),
                IsAbstract: ct.IsAbstract, BaseRecordName: baseRecord, Attributes: attrPlans,
                SimpleContent: scVal, SimpleContentType: scType);
        }

        // xs:choice content — the alternatives become mutually-exclusive nullable fields.
        if (ct.Choice is not null)
        {
            var alts = new List<ChildPlan>();
            foreach (var el in ct.Choice)
            {
                var (csType, val, isVal) = ResolveElementType(el, schema, enums);
                alts.Add(new ChildPlan(
                    FieldName       : PascalCase(el.Name),
                    CSharpType      : csType,
                    IsCSharpNullable: isVal,
                    Shape           : ChildShape.OptionalSingle, // renders the field as nullable
                    Value           : val));
            }
            return new SequencePlan(PascalCase(ctName), alts,
                IsAbstract: ct.IsAbstract, BaseRecordName: baseRecord, Attributes: attrPlans, IsChoice: true);
        }

        // Flatten inherited particles: for xs:complexContent/xs:extension the base type's
        // particles come first, then this type's own — this is the EXI content order.
        var particles = FlattenParticles(ct, schema);

        // Detect "single repeating element" pattern (e.g. AppProtocolType list inside Req).
        if (particles.Count == 1 && particles[0].MaxOccurs > 1 && particles[0].Ref is null)
        {
            if (attrPlans is not null)
                throw new NotSupportedException(
                    $"complexType '{ctName}': attributes on a repeating-content type are not supported yet.");
            var only = particles[0];
            var (csType, val, _) = ResolveElementType(only, schema, enums);

            // For bounded repeating, the "child" represents the repeating element type;
            // the emitter handles the loop using ListMin/ListMax.
            var child = new ChildPlan(
                FieldName       : PascalCase(only.Name),
                CSharpType      : csType,
                IsCSharpNullable: false,
                Shape           : ChildShape.BoundedRepeating,
                Value           : val);

            return new SequencePlan(
                CSharpRecordName: PascalCase(ctName),
                Children        : new[] { child },
                ListMin         : only.MinOccurs,
                ListMax         : only.MaxOccurs,
                IsAbstract      : ct.IsAbstract,
                BaseRecordName  : baseRecord);
        }

        // Otherwise, treat each child individually.
        var children = new List<ChildPlan>();
        foreach (var el in particles)
        {
            // An inline xs:choice nested in the sequence (ISO 15118-20): each branch resolves to its
            // own independent (always-nullable) field, exactly like any other child element — cbexigen
            // flattens the branches into the enclosing run's shared state (see ValueEncoding.InlineChoice).
            if (el.InlineChoiceMembers is not null)
            {
                var members = new List<InlineChoiceMember>();
                foreach (var mbr in el.InlineChoiceMembers)
                {
                    var (mbrCsType, mbrVal, mbrIsValueType) = ResolveElementType(mbr, schema, enums);
                    members.Add(new InlineChoiceMember(mbr.Name, PascalCase(mbr.Name), mbrCsType, mbrVal, mbrIsValueType));
                }
                children.Add(new ChildPlan(
                    FieldName       : el.Name,   // "$InlineChoice" — never dereferenced as msg.<FieldName>
                    CSharpType      : "",
                    IsCSharpNullable: false,
                    Shape           : el.MinOccurs == 0 ? ChildShape.OptionalSingle : ChildShape.RequiredSingle,
                    Value           : new ValueEncoding.InlineChoice(BitsForChoices(members.Count + 1), members)));
                continue;
            }

            // A repeating reference into an opaque namespace (SignatureType's Object): always absent
            // in ISO 15118-2, so it is modelled as an opaque optional single. While absent this is
            // byte-identical — its first-occurrence SE is one production of the enclosing state; the
            // repeat only matters once present, which never happens. A present instance fails loud.
            if (el.MaxOccurs > 1 && el.Ref is not null && schema.OpaqueElementNames.Contains(el.Ref))
            {
                var opaqueType = PascalCase(el.Ref);
                opaqueTypes.Add(opaqueType);
                children.Add(new ChildPlan(
                    FieldName       : PascalCase(el.Ref),
                    CSharpType      : opaqueType,
                    IsCSharpNullable: false,
                    Shape           : ChildShape.OptionalSingle,
                    Value           : new ValueEncoding.OpaqueElement(opaqueType)));
                continue;
            }

            // A repeating element (maxOccurs > 1) among other children: supported when it is the
            // last particle (cbexigen encodes it as a list after the preceding children).
            if (el.MaxOccurs > 1)
            {
                if (!ReferenceEquals(el, particles[particles.Count - 1]))
                    throw new NotSupportedException(
                        $"complexType '{ctName}': repeating element '{el.Name}' must be the last child of the sequence.");
                var (repType, repVal, _) = ResolveElementType(el, schema, enums);
                children.Add(new ChildPlan(
                    FieldName : PascalCase(el.Name),
                    CSharpType: repType,
                    IsCSharpNullable: false,
                    Shape     : ChildShape.BoundedRepeating,
                    Value     : repVal,
                    ListMin   : el.MinOccurs,
                    ListMax   : el.MaxOccurs));
                continue;
            }

            // <xs:element ref="Head"/> pointing at a substitution-group head → a polymorphic
            // choice among the head's members.
            if (el.Ref is not null)
            {
                var subst = TryBuildSubstitution(schema, el.Ref);
                if (subst is { } s)
                {
                    // A substitution reference expands to one grammar production per member (and the
                    // abstract head); an optional reference (minOccurs=0) joins the surrounding
                    // optional run and gains an EE alternative, a required one terminates it. The
                    // emitter flattens the members into the run's grammar state (cbexigen model,
                    // verified against PowerDeliveryReqType and ChargeParameterDiscoveryResType).
                    children.Add(new ChildPlan(
                        FieldName       : PascalCase(el.Ref),
                        CSharpType      : s.BaseType,
                        IsCSharpNullable: false,
                        Shape           : el.MinOccurs == 0 ? ChildShape.OptionalSingle : ChildShape.RequiredSingle,
                        Value           : s.Choice));
                    continue;
                }

                // A reference into an opaque namespace (ds:Signature in the message header):
                // model it as an opaque, encode-absent child. It is optional in the schema and
                // always absent for the Phase 2 messages.
                if (schema.OpaqueElementNames.Contains(el.Ref))
                {
                    var opaqueType = PascalCase(el.Ref);
                    opaqueTypes.Add(opaqueType);
                    children.Add(new ChildPlan(
                        FieldName       : PascalCase(el.Ref),
                        CSharpType      : opaqueType,
                        IsCSharpNullable: false, // reference type; nullability comes from Shape
                        Shape           : el.MinOccurs == 0 ? ChildShape.OptionalSingle : ChildShape.RequiredSingle,
                        Value           : new ValueEncoding.OpaqueElement(opaqueType)));
                    continue;
                }

                // A plain reference to a modelled (whitelisted) global element — resolve to that
                // element's type, exactly like a named child. Used by the XMLDSig SignedInfo subtree
                // (SignedInfoType → CanonicalizationMethod/SignatureMethod, ReferenceType → …).
                var refTarget = schema.GlobalElements.FirstOrDefault(g => g.Ref is null && g.Name == el.Ref);
                if (refTarget is not null && !string.IsNullOrEmpty(refTarget.TypeRef))
                {
                    var (refType, refVal, refIsValueType) = ResolveElementType(el, schema, enums);
                    var refShape = el.MinOccurs == 0 ? ChildShape.OptionalSingle : ChildShape.RequiredSingle;
                    children.Add(new ChildPlan(
                        FieldName       : PascalCase(el.Ref),
                        CSharpType      : refType,
                        IsCSharpNullable: refShape == ChildShape.OptionalSingle && refIsValueType,
                        Shape           : refShape,
                        Value           : refVal));
                    continue;
                }

                throw new NotSupportedException(
                    $"complexType '{ctName}': element ref '{el.Ref}' is not a substitution-group head " +
                    "and not an opaque-namespace element (plain element references are not supported yet).");
            }

            var (csType, val, isValueType) = ResolveElementType(el, schema, enums);
            var shape = el.MinOccurs == 0 ? ChildShape.OptionalSingle : ChildShape.RequiredSingle;

            children.Add(new ChildPlan(
                FieldName       : PascalCase(el.Name),
                CSharpType      : csType,
                IsCSharpNullable: shape == ChildShape.OptionalSingle && isValueType,
                Shape           : shape,
                Value           : val,
                IsWildcardAny   : el.IsWildcard));
        }

        return new SequencePlan(PascalCase(ctName), children,
            IsAbstract: ct.IsAbstract, BaseRecordName: baseRecord, Attributes: attrPlans);
    }

    /// <summary>
    /// If <paramref name="headName"/> names a substitution-group head (an abstract element
    /// and/or one that others substitute), build the sorted production list. cbexigen includes
    /// the head element itself as a production and sorts by element name.
    /// </summary>
    private static (string BaseType, ValueEncoding.SubstitutionChoice Choice)? TryBuildSubstitution(
        XsdSchema schema, string headName)
    {
        var head = schema.GlobalElements.FirstOrDefault(g => g.Ref is null && g.Name == headName);
        if (head is null) return null;

        var productions = new List<XsdElement> { head };
        productions.AddRange(schema.GlobalElements.Where(g => g.Ref is null && g.SubstitutionGroup == headName));

        if (productions.Count <= 1 && !head.IsAbstract)
            return null; // a plain global element, not a substitution point

        productions.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        var members = productions
            .Select(e => new SubstMember(
                ElementName    : e.Name,
                CSharpTypeName : PascalCase(StripPrefix(e.TypeRef)),
                IsAbstractHead : e.Name == headName && head.IsAbstract))
            .ToList();

        // Standalone width: n member productions + the non-strict phantom -> ceil(log2(n+1)).
        // (When the reference sits inside an optional run the emitter recomputes the width from
        // the whole state's production count and this value is unused.)
        var choice = new ValueEncoding.SubstitutionChoice(BitsForChoices(members.Count + 1), members);
        return (PascalCase(StripPrefix(head.TypeRef)), choice);
    }

    private static (string CSharpType, ValueEncoding Value, bool IsValueType) ResolveTypeRef(
        string typeRef, XsdSchema schema, List<EnumPlan> enums, string ownerName)
    {
        // Built-in xs:* / xsd:* first.
        if (typeRef.StartsWith("xs:",  StringComparison.Ordinal) ||
            typeRef.StartsWith("xsd:", StringComparison.Ordinal))
            return ResolveBuiltin(NormaliseBuiltin(typeRef));

        // Cross-namespace references carry a prefix (e.g. "v2gci_t:FooType"); the collected
        // set resolves them by local name.
        typeRef = StripPrefix(typeRef);

        // Named simpleType: walk through restriction.
        if (schema.SimpleTypes.TryGetValue(typeRef, out var st))
            return ResolveSimpleType(st, enums);

        // Named complexType → field is the corresponding C# record.
        if (schema.ComplexTypes.ContainsKey(typeRef))
        {
            var typeName = PascalCase(typeRef);
            return (typeName, new ValueEncoding.ComplexRef(typeName), false);
        }

        throw new InvalidOperationException($"Cannot resolve type reference '{typeRef}' for element '{ownerName}'.");
    }

    /// <summary>Resolve a (named or inline) simpleType's restriction to a value encoding.</summary>
    private static (string CSharpType, ValueEncoding Value, bool IsValueType) ResolveSimpleType(
        XsdSimpleType st, List<EnumPlan> enums)
    {
        // String enumeration → C# enum.
        if (st.Enumeration is { Count: > 0 } members)
        {
            var enumName = PascalCase(st.Name).TrimSuffix("Type").TrimSuffix("_inline");
            if (!enums.Any(e => e.Name == enumName))
                enums.Add(new EnumPlan(enumName, members));
            int enumWidth = BitsForChoices(members.Count);
            return (enumName, new ValueEncoding.EnumIndex(enumName, enumWidth, members), true);
        }

        // Bounded integer range → n-bit unsigned with bias, but ONLY when the range has ≤ 4096
        // values (EXI §7.1.10). A wider bounded range (e.g. RelativeTimeInterval's start, 0..16777214)
        // falls back to the base built-in's integer encoding — cbexigen encodes it as an EXI Unsigned
        // Integer, not a 24-bit n-bit field.
        if (st.MinInclusive is long min && st.MaxInclusive is long max && max >= min && max - min + 1 <= 4096)
        {
            long range = max - min + 1;
            int width = BitsForChoices(checked((int)range));
            var (csType, _, isVal) = ResolveBuiltin(st.Base);
            return (csType, new ValueEncoding.NBitUnsigned(width, min), isVal);
        }

        // Otherwise: inherit the base built-in's encoding (string, unsigned/signed integer, …).
        return ResolveBuiltin(NormaliseBuiltin(st.Base));
    }

    /// <summary>Resolve an element's type — its inline simpleType if present, else its type ref.</summary>
    private static (string CSharpType, ValueEncoding Value, bool IsValueType) ResolveElementType(
        XsdElement el, XsdSchema schema, List<EnumPlan> enums)
    {
        if (el.InlineSimpleType is not null)
            return ResolveSimpleType(el.InlineSimpleType, enums);

        // A plain element reference (e.g. a repeating ref="SalesTariffEntry") resolves to the
        // referenced global element's type.
        if (el.Ref is not null)
        {
            var target = schema.GlobalElements.FirstOrDefault(g => g.Ref is null && g.Name == el.Ref)
                ?? throw new InvalidOperationException($"element ref '{el.Ref}' not found.");
            return ResolveTypeRef(target.TypeRef, schema, enums, target.Name);
        }

        return ResolveTypeRef(el.TypeRef, schema, enums, el.Name);
    }

    private static (string CSharpType, ValueEncoding Value, bool IsValueType) ResolveBuiltin(string xsType)
    {
        return xsType switch
        {
            "xs:string"        => ("string", new ValueEncoding.StringValue(), false),
            "xs:anyURI"        => ("string", new ValueEncoding.StringValue(), false),
            // String-ish built-ins used by attributes (xs:ID / NCName / token …).
            "xs:ID"            => ("string", new ValueEncoding.StringValue(), false),
            "xs:IDREF"         => ("string", new ValueEncoding.StringValue(), false),
            "xs:NCName"        => ("string", new ValueEncoding.StringValue(), false),
            "xs:Name"          => ("string", new ValueEncoding.StringValue(), false),
            "xs:token"         => ("string", new ValueEncoding.StringValue(), false),
            "xs:normalizedString" => ("string", new ValueEncoding.StringValue(), false),
            // cbexigen encodes unsignedByte as a fixed 8-bit n-bit unsigned (its value
            // space is [0..255]), not as a multi-byte EXI Unsigned Integer.
            "xs:unsignedByte"  => ("byte",   new ValueEncoding.NBitUnsigned(8, 0), true),
            "xs:unsignedShort" => ("ushort", new ValueEncoding.UnsignedInt(),  true),
            "xs:unsignedInt"   => ("uint",   new ValueEncoding.UnsignedInt(),  true),
            "xs:unsignedLong"  => ("ulong",  new ValueEncoding.UnsignedInt(),  true),
            // xs:byte is bounded [-128..127] → 8-bit n-bit unsigned with bias (cbexigen model).
            "xs:byte"          => ("sbyte",  new ValueEncoding.NBitUnsigned(8, -128), true),
            // Wider signed built-ins → EXI Integer (sign bit + Unsigned Integer magnitude).
            "xs:short"         => ("short",  new ValueEncoding.SignedInt(), true),
            "xs:int"           => ("int",    new ValueEncoding.SignedInt(), true),
            "xs:long"          => ("long",   new ValueEncoding.SignedInt(), true),
            "xs:integer"       => ("long",   new ValueEncoding.SignedInt(), true),
            "xs:boolean"       => ("bool",   new ValueEncoding.NBitUnsigned(1, 0), true),
            // hexBinary and base64Binary are identical on the wire (length + raw octets).
            "xs:hexBinary"     => ("byte[]", new ValueEncoding.Binary(), false),
            "xs:base64Binary"  => ("byte[]", new ValueEncoding.Binary(), false),
            _ => throw new NotSupportedException($"Unsupported XSD built-in '{xsType}'."),
        };
    }

    /// <summary>Map both <c>xs:</c> and <c>xsd:</c> prefixed names to the canonical <c>xs:</c> form.</summary>
    private static string NormaliseBuiltin(string typeRef) =>
        typeRef.StartsWith("xsd:", StringComparison.Ordinal)
            ? "xs:" + typeRef.Substring("xsd:".Length)
            : typeRef;

    /// <summary>⌈log₂(n)⌉, with the EXI convention that n=1 needs 0 bits.</summary>
    private static int BitsForChoices(int n)
    {
        if (n <= 1) return 0;
        int bits = 0;
        int v = n - 1;
        while (v > 0) { bits++; v >>= 1; }
        return bits;
    }

    private static string PascalCase(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    /// <summary>Drop an XML namespace prefix (<c>"ns:Local"</c> → <c>"Local"</c>).</summary>
    private static string StripPrefix(string s)
    {
        int i = s.IndexOf(':');
        return i < 0 ? s : s.Substring(i + 1);
    }

    /// <summary>
    /// The full ordered particle list of a complex type: for an extension, the base type's
    /// (recursively flattened) particles followed by this type's own. Non-derived types just
    /// return their own sequence.
    /// </summary>
    private static IReadOnlyList<XsdElement> FlattenParticles(XsdComplexType ct, XsdSchema schema)
    {
        if (ct.BaseTypeRef is null)
            return ct.Sequence;

        var baseLocal = StripPrefix(ct.BaseTypeRef);
        if (!schema.ComplexTypes.TryGetValue(baseLocal, out var baseCt))
            throw new InvalidOperationException(
                $"complexType '{ct.Name}': unknown xs:extension base '{ct.BaseTypeRef}'.");

        var result = new List<XsdElement>(FlattenParticles(baseCt, schema));
        result.AddRange(ct.Sequence);
        return result;
    }
}

internal static class StringExt
{
    public static string TrimSuffix(this string s, string suffix) =>
        s.EndsWith(suffix, StringComparison.Ordinal) && s.Length > suffix.Length
            ? s.Substring(0, s.Length - suffix.Length) : s;
}
