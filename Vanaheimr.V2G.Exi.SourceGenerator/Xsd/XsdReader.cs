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

    /// <summary>Parse a single XSD document into its own schema.</summary>
    public static XsdSchema Parse(string xml)
    {
        var schema = new XsdSchema();
        AppendDocument(schema, xml, isFirst: true);
        return schema;
    }

    /// <summary>
    /// Parse a set of XSD documents (linked by <c>xs:import</c>) into ONE schema model.
    /// Named types and global elements from every file are merged; <c>xs:import</c> /
    /// <c>xs:include</c> are dependency declarations and need no action because all types
    /// are resolved across the collected set.
    /// </summary>
    public static XsdSchema ParseSet(IEnumerable<string> documents)
    {
        var schema = new XsdSchema();
        bool first = true;
        foreach (var xml in documents)
        {
            AppendDocument(schema, xml, isFirst: first);
            first = false;
        }
        return schema;
    }

    private static void AppendDocument(XsdSchema schema, string xml, bool isFirst)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new XsdReaderException("XSD has no root element.");
        if (root.Name != Xs + "schema")
            throw new XsdReaderException($"Root must be xs:schema (got {root.Name}).");

        // The first document's targetNamespace names the set (used only for diagnostics; the
        // emitter picks its own C# namespace).
        if (isFirst)
            schema.TargetNamespace = (string?)root.Attribute("targetNamespace") ?? "";

        foreach (var st in root.Elements(Xs + "simpleType"))
            ParseNamedSimpleType(st, schema);

        foreach (var ct in root.Elements(Xs + "complexType"))
            schema.ComplexTypes[Required(ct, "name")] = ParseComplexType(ct);

        foreach (var el in root.Elements(Xs + "element"))
            schema.GlobalElements.Add(ParseElement(el));
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
        bool isAbstract = string.Equals((string?)ct.Attribute("abstract"), "true", StringComparison.Ordinal);

        // xs:complexContent / xs:extension base="..."
        var complexContent = ct.Element(Xs + "complexContent");
        if (complexContent is not null)
        {
            var ext = complexContent.Element(Xs + "extension")
                ?? throw new XsdReaderException(
                    $"complexType '{name}': only xs:extension is supported inside xs:complexContent.");
            var baseRef = Required(ext, "base");
            var seq = ext.Element(Xs + "sequence");
            var els = seq?.Elements(Xs + "element").Select(ParseElement).ToList()
                      ?? new List<XsdElement>();
            return new XsdComplexType(name, els, baseRef, isAbstract);
        }

        // Direct xs:sequence, or an empty complexType (e.g. the abstract BodyBaseType).
        var directSeq = ct.Element(Xs + "sequence");
        if (directSeq is null)
        {
            if (!ct.Elements().Any(e => e.Name.Namespace == Xs && e.Name.LocalName != "annotation"))
                return new XsdComplexType(name, new List<XsdElement>(), null, isAbstract);
            throw new XsdReaderException(
                $"complexType '{name}': only xs:sequence or xs:complexContent/xs:extension is supported.");
        }

        var elements = directSeq.Elements(Xs + "element").Select(ParseElement).ToList();
        return new XsdComplexType(name, elements, null, isAbstract);
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
