# XSD → C# mapping (source generator)

How `WWCP_ISO15118_EXI_SourceGenerator` turns an XSD schema set into C# records and a
codec. These are the binding rules; they are exercised construct-by-construct in
`GeneratorGrammarTests` and end-to-end against cbV2G in `Iso15118_2VectorTests`.

The generated output is one static codec class plus a record per complex type, in a
namespace and codec-class name set by the `ExiGeneratedNamespace` /
`ExiCodecClassName` MSBuild properties (so AppProtocol and ISO 15118-2 can coexist).

## Types

| XSD | C# | notes |
|---|---|---|
| `complexType` (leaf) | `sealed record` | positional record; one parameter per particle |
| `complexType` extended by others | `record` (un-sealed) | e.g. `ServiceType`, base of `ChargeServiceType` |
| `complexType abstract="true"` | `abstract record` | substitution heads, extension bases; never encoded directly |
| `complexContent`/`extension` | derived `record : Base(baseArgs)` | base particles flatten *before* the derived ones; the base's fields are forwarded to its constructor |
| `simpleContent`/`extension` | record with a `Value` field + attribute fields | a typed value carrying attributes |
| `xs:choice` | one nullable field per alternative | mutually exclusive; exactly one is non-null |
| named `simpleType` (enumeration) | `enum : byte` | members in **XSD declaration order**; the enum value *is* the EXI n-bit index |

## Substitution groups

A `substitutionGroup` becomes an abstract base record (the head's type) plus a concrete
record per member. A reference to the head (`<xs:element ref="Head"/>`) is a polymorphic
field typed as the base; encode dispatches on the runtime type, decode on the event code.
The members (concrete + the abstract head) are **flattened into the enclosing grammar
state as individual productions**, sorted by element name — not modelled as a nested
selector (see `docs/xsd-inventory-15118-2.md`, construct 10).

## Attributes

| `use` | C# parameter | wire |
|---|---|---|
| `optional` | `T? Name` (leading, before content) | the AT event is the first production of the content's initial grammar state — the attribute is the leading optional of the content run |
| `required` | `T? Name` | a 1-bit AT prefix, always present |

Attributes are ordered lexicographically by QName. Only string-typed attributes
(`xs:ID`, `xs:anyURI`, `xs:string`, …) are supported.

## Element / attribute value types

| XSD built-in | C# | EXI encoding |
|---|---|---|
| `xs:string`, `xs:anyURI`, `xs:ID`, `xs:token`, `xs:NCName`, … | `string` | length-prefixed, miss-only |
| `xs:boolean` | `bool` | 1-bit n-bit unsigned |
| `xs:unsignedByte` | `byte` | 8-bit n-bit unsigned |
| `xs:byte` | `sbyte` | 8-bit n-bit unsigned, bias −128 |
| `xs:unsignedShort` / `Int` / `Long` | `ushort` / `uint` / `ulong` | EXI Unsigned Integer |
| `xs:short` / `int` / `long` / `integer` | `short` / `int` / `long` / `long` | EXI (signed) Integer |
| `xs:hexBinary`, `xs:base64Binary` | `byte[]` | length + raw octets (identical on the wire) |
| `simpleType` restricting an integer with `minInclusive`/`maxInclusive` | the base built-in's C# type | n-bit unsigned with bias = `minInclusive` |

Value-space facets that do not change the wire form (`length`, `minLength`, `pattern`,
`whiteSpace`, `totalDigits`, `fractionDigits`) are recognised and ignored.

## Occurrence

| XSD | C# | wire |
|---|---|---|
| `minOccurs="0"` (single) | `T?` | joins the content's optional run; present/absent chosen by event code |
| `minOccurs≥1`, `maxOccurs="1"` | `T` | required element |
| `maxOccurs>1` | `IReadOnlyList<T>` | first item is a production of the enclosing state, further items and the EE loop at a 2-bit code; a `maxOccurs=2` list is bounded-unrolled (1-bit EE when full) |

## Opaque namespace (XMLDSig)

XMLDSig is not modelled. The header's optional `ds:Signature` becomes an empty
placeholder record and a nullable field that is only ever encoded/decoded as *absent*
(a present instance fails loud — deferred to Phase 3). Self-contained data types the -2
schemas genuinely reference (`X509IssuerSerialType`) *are* modelled; the signature
subtree (xs:any / mixed / recursive) is not.

## Document grammar

The codec exposes one `TryEncode` extension method per decodable document root and a
`DecodeAny` dispatcher. The document element selector enumerates **every** global element
of the collected set (heads, members, opaque elements), sorted by name then namespace;
its width and each root's index come from that full list (for -2, `V2G_Message` is index
76 of 80 — a 7-bit selector).
