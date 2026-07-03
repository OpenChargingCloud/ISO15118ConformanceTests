# ISO 15118-2 XSD inventory (Phase 2, step 1)

Mechanical inventory of every XSD construct/facet **actually used** by the five
ISO 15118-2 schema files. This is the binding requirements list for the generator —
implement exactly these constructs, construct by construct, verifying each against cbV2G.

Source schemas: `Vanaheimr.V2G.Exi.Iso15118_2/Schemas/` (provenance: RISE-V2G
`055806d`, see the README next to them). Produced by a throwaway ElementTree script
over the five files (`V2G_CI_MsgDef`, `V2G_CI_MsgHeader`, `V2G_CI_MsgBody`,
`V2G_CI_MsgDataTypes`, `xmldsig-core-schema`).

## Constructs used (frequency)

| construct | count | notes |
|---|---|---|
| `element` | 334 | global + local; 334 total |
| `complexType` | 106 | **1 anonymous** |
| `enumeration` | 97 | string enums → C# enums (declaration-order index) |
| `sequence` | 94 | the dominant content model |
| `extension` (complexContent) | 51 | **type inheritance — 34× `base=BodyBaseType`** |
| `complexContent` | 47 | |
| `restriction` (simpleType) | 40 | ranges, maxLength, enums, binary bases |
| `simpleType` | 40 | **2 anonymous** |
| `attribute` | 30 | use: 11 required, 14 optional, 5 defaulted; `xs:ID`×9 |
| `maxLength` | 13 | on string/binary |
| `any` | 12 | **only in xmldsig** (wildcard) |
| `choice` | 7 | 6 in xmldsig, 1 in MsgDataTypes |
| `import` | 6 | multi-file, multi-namespace |
| `maxInclusive`/`minInclusive` | 6/6 | bounded integers → n-bit |
| `simpleContent` (extension) | 4 | value + attributes |
| `minLength`/`length` | 2/1 | |

## Substitution groups (abstract head → member count)

`BodyElement` (**34** members — every message body), `EVChargeParameter` (2),
`EVSEChargeParameter` (2), `EVSEStatus` (2), `Entry` (2), `EVStatus` (1),
`EVPowerDeliveryParameter` (1), `SASchedules` (1), `TimeInterval` (1).
Abstract elements: all nine heads above.

## maxOccurs values in use

`unbounded` (5), and bounded: 1024 (2), 255, 24, 20, 16 (2), 8, 6, 4, 3 (3), 2.
`minOccurs`: 0 (64×, i.e. lots of optionals), 1 (explicit once; default otherwise).

## Built-in types referenced

`xs:boolean`, `xs:ID`, `xs:short`, `xs:long`, `xs:int`, `xs:byte`, `xs:unsignedByte`,
`xs:unsignedShort`, `xs:unsignedInt`, `xs:unsignedLong`, `xs:string`, `xs:hexBinary`,
`xs:base64Binary`, `xs:integer` (as a restriction base).

## Derived generator feature checklist

Ordered roughly by how many messages they unblock. Each gets a synthetic mini-XSD +
grammar unit test before touching the real schema.

1. **Multi-file / multi-namespace `import`** — collect all `.xsd` of a set, resolve by
   `targetNamespace`. (Architecture change: one schema-set, not per-file.)
2. **`complexContent`/`extension`** — flatten a derived type's inherited particles
   before its own (BodyBaseType is empty, so mostly a structural pass, but general).
3. **`substitutionGroup` + abstract elements** — a reference to an abstract head expands
   to SE productions for every concrete member (sorted); C# → abstract base record +
   derived records (polymorphism).
4. **`attribute`** (AT events, lexicographic QName order, before content) + `xs:ID`→string,
   `use=required|optional`.
5. **`maxOccurs`** unbounded and arbitrary bounded values, at any position (not just as a
   lone child).
6. **`choice`** — productions with correct event codes.
7. **`simpleContent` extension** — a typed value carrying attributes.
8. **Anonymous** complexType (1) / simpleType (2).
9. New built-ins: signed (`byte`/`short`/`int`/`long` → `sbyte`/`short`/`int`/`long`),
   `hexBinary`/`base64Binary` → `byte[]`, `boolean`, `xs:ID` → string.

## The XMLDSig complication (scope call for Phase 2)

`xmldsig-core-schema.xsd` is the outlier: it is where **all 12 `xs:any`**, **6 of 7
`choice`**, and most **mixed** content live, plus recursive types (`Object`, `SignatureType`).
The -2 schemas `import` it because the `V2G_Message` **Header carries an optional
`ds:Signature`**. But:

- SessionSetup and ServiceDiscovery (the Phase 2 target messages) do **not** sign — their
  Signature is absent, so only the optional-element "absent" bit is exercised.
- Full XMLDSig grammar (xs:any / mixed) + signature computation is explicitly **Phase 3**.

So the open decision is how to make the set generate without diagnostics *now* without
implementing xs:any/mixed: e.g. model `ds:Signature` as an opaque, structurally-typed
element that we can encode-absent / round-trip, and defer full fidelity to Phase 3. This
is flagged for the user before the generator work proceeds.
