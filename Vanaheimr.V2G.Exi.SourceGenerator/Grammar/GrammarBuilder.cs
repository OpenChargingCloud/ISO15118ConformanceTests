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
}

/// <summary>One production of a <see cref="ValueEncoding.SubstitutionChoice"/>.</summary>
internal sealed record SubstMember(string ElementName, string CSharpTypeName, bool IsAbstractHead);

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
    int            ListMax = 0);

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
    SequencePlan  Body);

/// <summary>
/// The full plan for one schema, ready for emission.
/// </summary>
internal sealed record SchemaPlan(
    string                          TargetNamespace,
    IReadOnlyList<GlobalElementPlan> GlobalElements,
    IReadOnlyDictionary<string, SequencePlan> ComplexTypes,
    IReadOnlyList<EnumPlan>         Enums,
    IReadOnlyList<string>           OpaqueTypes); // empty placeholder records for opaque refs

internal sealed record EnumPlan(string Name, IReadOnlyList<string> Members);

/// <summary>
/// Lowers an <see cref="XsdSchema"/> to a <see cref="SchemaPlan"/> that the
/// emitter can consume mechanically.
/// </summary>
internal static class GrammarBuilder
{
    public static SchemaPlan Build(XsdSchema schema)
    {
        var enums = new List<EnumPlan>();
        var opaqueTypes = new List<string>();

        // Build per-named-complexType plans.
        var complex = new Dictionary<string, SequencePlan>();
        foreach (var kv in schema.ComplexTypes)
            complex[kv.Key] = BuildSequence(kv.Key, kv.Value, schema, enums, opaqueTypes);

        // Build global-element plans. Only true document roots become document-grammar
        // productions — an abstract substitution head (BodyElement) and its members
        // (SessionSetupReq, …) are reached through the substitution choice, not as roots.
        var globals = new List<GlobalElementPlan>();
        foreach (var ge in schema.GlobalElements)
        {
            if (ge.IsAbstract || ge.SubstitutionGroup is not null || ge.Ref is not null)
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

            globals.Add(new GlobalElementPlan(ge.Name, typeName, body));
        }

        return new SchemaPlan(schema.TargetNamespace, globals, complex, enums,
            opaqueTypes.Distinct().ToList());
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
                    children.Add(new ChildPlan(
                        FieldName       : PascalCase(el.Ref),
                        CSharpType      : s.BaseType,
                        IsCSharpNullable: false,
                        // cbexigen models the choice as a mandatory selection: the n-bit event
                        // code has no 'absent' production even when minOccurs=0.
                        Shape           : ChildShape.RequiredSingle,
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
                Value           : val));
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

        var choice = new ValueEncoding.SubstitutionChoice(BitsForChoices(members.Count), members);
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

        // Bounded integer range → n-bit unsigned with bias.
        if (st.MinInclusive is long min && st.MaxInclusive is long max && max >= min)
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
