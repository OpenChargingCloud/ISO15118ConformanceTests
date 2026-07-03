using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Vanaheimr.V2G.Exi.SourceGenerator.Xsd;

/// <summary>
/// Parses a tightly-scoped XSD subset into <see cref="XsdSchema"/>.
/// <para>
/// Supported: top-level <c>xs:element</c> with inline <c>xs:complexType</c>;
/// named <c>xs:complexType</c> with <c>xs:sequence</c>; named <c>xs:simpleType</c>
/// with <c>xs:restriction</c> over <c>xs:string</c> or any unsigned built-in,
/// carrying <c>xs:minInclusive</c>, <c>xs:maxInclusive</c>, <c>xs:maxLength</c>,
/// or <c>xs:enumeration</c>.
/// </para>
/// <para>
/// Unsupported constructs surface as <see cref="XsdReaderException"/> with the
/// path of the offending element, which the generator turns into a build-time
/// diagnostic. We deliberately fail loud rather than silently skipping; an XSD
/// feature we don't model is a real gap, not a soft warning.
/// </para>
/// </summary>
internal static class XsdReader
{
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";

    public static XsdSchema Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new XsdReaderException("XSD has no root element.");
        if (root.Name != Xs + "schema")
            throw new XsdReaderException($"Root must be xs:schema (got {root.Name}).");

        var schema = new XsdSchema
        {
            TargetNamespace = (string?)root.Attribute("targetNamespace") ?? "",
        };

        foreach (var st in root.Elements(Xs + "simpleType"))
            ParseNamedSimpleType(st, schema);

        foreach (var ct in root.Elements(Xs + "complexType"))
            schema.ComplexTypes[Required(ct, "name")] = ParseComplexType(ct);

        foreach (var el in root.Elements(Xs + "element"))
            schema.GlobalElements.Add(ParseElement(el));

        return schema;
    }

    private static void ParseNamedSimpleType(XElement st, XsdSchema schema)
    {
        var name = Required(st, "name");
        var restriction = st.Element(Xs + "restriction")
            ?? throw new XsdReaderException(
                $"simpleType '{name}': only restriction-based types are supported in this prototype.");

        var t = new XsdSimpleType { Name = name, Base = Required(restriction, "base") };

        foreach (var f in restriction.Elements())
        {
            if (f.Name == Xs + "minInclusive") t.MinInclusive = long.Parse(Required(f, "value"));
            else if (f.Name == Xs + "maxInclusive") t.MaxInclusive = long.Parse(Required(f, "value"));
            else if (f.Name == Xs + "maxLength")    t.MaxLength    = int .Parse(Required(f, "value"));
            else if (f.Name == Xs + "enumeration")
            {
                t.Enumeration ??= new List<string>();
                t.Enumeration.Add(Required(f, "value"));
            }
            else
                throw new XsdReaderException(
                    $"simpleType '{name}': unsupported facet {f.Name.LocalName}.");
        }

        // EXI canonical ordering for enums is lexicographic over the string form,
        // but we keep declaration order here; the emitter computes the lex-index
        // mapping at code-gen time.

        schema.SimpleTypes[name] = t;
    }

    private static XsdComplexType ParseComplexType(XElement ct)
    {
        var name = (string?)ct.Attribute("name") ?? "";
        var seq = ct.Element(Xs + "sequence")
            ?? throw new XsdReaderException(
                $"complexType '{name}': only xs:sequence is supported in this prototype.");

        var elements = seq.Elements(Xs + "element").Select(ParseElement).ToList();
        return new XsdComplexType(name, elements);
    }

    private static XsdElement ParseElement(XElement el)
    {
        var name = Required(el, "name");
        var typeRef = (string?)el.Attribute("type") ?? "";
        int minOccurs = int.Parse((string?)el.Attribute("minOccurs") ?? "1");
        var maxAttr = (string?)el.Attribute("maxOccurs") ?? "1";
        int maxOccurs = string.Equals(maxAttr, "unbounded", StringComparison.OrdinalIgnoreCase)
            ? int.MaxValue
            : int.Parse(maxAttr);

        XsdComplexType? inline = null;
        if (string.IsNullOrEmpty(typeRef))
        {
            var ct = el.Element(Xs + "complexType")
                ?? throw new XsdReaderException(
                    $"element '{name}': must have either a type attribute or an inline complexType.");
            inline = ParseComplexType(ct);
        }

        return new XsdElement(name, typeRef, minOccurs, maxOccurs, inline);
    }

    private static string Required(XElement el, string attr) =>
        (string?)el.Attribute(attr)
        ?? throw new XsdReaderException($"<{el.Name.LocalName}> missing required attribute '{attr}'.");
}

internal sealed class XsdReaderException : Exception
{
    public XsdReaderException(string message) : base(message) { }
}
