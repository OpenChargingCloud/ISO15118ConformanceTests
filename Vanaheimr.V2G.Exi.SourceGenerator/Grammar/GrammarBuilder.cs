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
    public sealed record StringValue : ValueEncoding;
    public sealed record NBitUnsigned(int BitWidth, long Bias) : ValueEncoding;
    public sealed record EnumIndex(string EnumName, int BitWidth, IReadOnlyList<string> Members) : ValueEncoding;
    public sealed record ComplexRef(string TypeName) : ValueEncoding;
}

/// <summary>
/// Per-child plan inside a sequence — combines the value encoding with the EXI
/// event-code wrapping (mandatory / optional / repeating).
/// </summary>
internal sealed record ChildPlan(
    string         FieldName,        // PascalCase as in the message record
    string         CSharpType,       // "uint", "byte", "string", "AppProtocolEntry"
    bool           IsCSharpNullable, // for optional value-types only
    ChildShape     Shape,
    ValueEncoding  Value);

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
    int                      ListMax = 0);

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
    IReadOnlyList<EnumPlan>         Enums);

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

        // Build per-named-complexType plans.
        var complex = new Dictionary<string, SequencePlan>();
        foreach (var kv in schema.ComplexTypes)
            complex[kv.Key] = BuildSequence(kv.Key, kv.Value, schema, enums);

        // Build global-element plans (each wraps a sequence).
        var globals = new List<GlobalElementPlan>();
        foreach (var ge in schema.GlobalElements)
        {
            var typeName = PascalCase(ge.Name);

            SequencePlan body;
            if (ge.InlineType is not null)
            {
                body = BuildSequence(typeName, ge.InlineType, schema, enums);
                complex[typeName] = body;
            }
            else if (complex.TryGetValue(ge.TypeRef, out var named))
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

        return new SchemaPlan(schema.TargetNamespace, globals, complex, enums);
    }

    private static SequencePlan BuildSequence(
        string ctName, XsdComplexType ct, XsdSchema schema, List<EnumPlan> enums)
    {
        // Detect "single repeating element" pattern (e.g. AppProtocolType list inside Req).
        if (ct.Sequence.Count == 1 && ct.Sequence[0].MaxOccurs > 1)
        {
            var only = ct.Sequence[0];
            var (csType, val, _) = ResolveTypeRef(only.TypeRef, schema, enums, only.Name);

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
                ListMax         : only.MaxOccurs);
        }

        // Otherwise, treat each child individually.
        var children = new List<ChildPlan>();
        foreach (var el in ct.Sequence)
        {
            if (el.MaxOccurs > 1)
                throw new NotSupportedException(
                    $"complexType '{ctName}': repeating element '{el.Name}' is only supported as the single member of a sequence in this prototype.");

            var (csType, val, isValueType) = ResolveTypeRef(el.TypeRef, schema, enums, el.Name);
            var shape = el.MinOccurs == 0 ? ChildShape.OptionalSingle : ChildShape.RequiredSingle;

            children.Add(new ChildPlan(
                FieldName       : PascalCase(el.Name),
                CSharpType      : csType,
                IsCSharpNullable: shape == ChildShape.OptionalSingle && isValueType,
                Shape           : shape,
                Value           : val));
        }

        return new SequencePlan(PascalCase(ctName), children);
    }

    private static (string CSharpType, ValueEncoding Value, bool IsValueType) ResolveTypeRef(
        string typeRef, XsdSchema schema, List<EnumPlan> enums, string ownerName)
    {
        // Built-in xs:* / xsd:* first.
        if (typeRef.StartsWith("xs:",  StringComparison.Ordinal) ||
            typeRef.StartsWith("xsd:", StringComparison.Ordinal))
            return ResolveBuiltin(NormaliseBuiltin(typeRef));

        // Named simpleType: walk through restriction.
        if (schema.SimpleTypes.TryGetValue(typeRef, out var st))
        {
            // String enumeration → C# enum.
            if (st.Enumeration is { Count: > 0 } members)
            {
                var enumName = PascalCase(st.Name).TrimSuffix("Type");
                if (!enums.Any(e => e.Name == enumName))
                    enums.Add(new EnumPlan(enumName, members));
                int width = BitsForChoices(members.Count);
                return (enumName, new ValueEncoding.EnumIndex(enumName, width, members), true);
            }

            // Bounded integer range → n-bit unsigned with bias.
            if (st.MinInclusive is long min && st.MaxInclusive is long max && max >= min)
            {
                long range = max - min + 1;
                int width = BitsForChoices(checked((int)range));
                var (csType, _, isVal) = ResolveBuiltin(st.Base);
                return (csType, new ValueEncoding.NBitUnsigned(width, min), isVal);
            }

            // Otherwise: inherit the base built-in's encoding (string or unsigned integer).
            return ResolveBuiltin(st.Base);
        }

        // Named complexType → field is the corresponding C# record.
        if (schema.ComplexTypes.ContainsKey(typeRef))
        {
            var typeName = PascalCase(typeRef);
            return (typeName, new ValueEncoding.ComplexRef(typeName), false);
        }

        throw new InvalidOperationException($"Cannot resolve type reference '{typeRef}'.");
    }

    private static (string CSharpType, ValueEncoding Value, bool IsValueType) ResolveBuiltin(string xsType)
    {
        return xsType switch
        {
            "xs:string"        => ("string", new ValueEncoding.StringValue(), false),
            "xs:unsignedByte"  => ("byte",   new ValueEncoding.UnsignedInt(),  true),
            "xs:unsignedShort" => ("ushort", new ValueEncoding.UnsignedInt(),  true),
            "xs:unsignedInt"   => ("uint",   new ValueEncoding.UnsignedInt(),  true),
            "xs:unsignedLong"  => ("ulong",  new ValueEncoding.UnsignedInt(),  true),
            "xs:boolean"       => ("bool",   new ValueEncoding.NBitUnsigned(1, 0), true),
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
}

internal static class StringExt
{
    public static string TrimSuffix(this string s, string suffix) =>
        s.EndsWith(suffix, StringComparison.Ordinal) && s.Length > suffix.Length
            ? s.Substring(0, s.Length - suffix.Length) : s;
}
