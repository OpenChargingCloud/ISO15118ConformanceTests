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

## The XMLDSig complication — resolved (construct 8)

`xmldsig-core-schema.xsd` is the outlier: it is where **all 12 `xs:any`**, **6 of 7
`choice`**, and most **mixed** content live, plus recursive types (`Object`, `SignatureType`).
The -2 schemas `import` it because the `V2G_Message` **Header carries an optional
`ds:Signature`**, and PaymentDetails references `ds:X509IssuerSerialType`.

Resolution (implemented): XMLDSig is treated as an **opaque namespace**.

- **`ds:Signature`** (the header reference) is modelled as an opaque, structurally-typed
  element: an empty placeholder record and a nullable field. It is only ever encoded/decoded
  as *absent* (its optional-element slot), which is exactly what the Phase 2 messages need
  since they never sign; a present instance fails loud. Full XMLDSig grammar (xs:any / mixed)
  and signature computation remain **Phase 3**.
- The signature subtree (`SignatureType`, `SignedInfoType`, `Object`, …, everything reachable
  only through the opaque Signature element) is **not** modelled — by design, not silently
  skipped.
- **Self-contained data types** from the namespace — a plain `xs:sequence` of built-in-typed
  fields with no reference into the subtree — *are* exposed, because -2 genuinely uses them.
  This is exactly `X509IssuerSerialType` (`X509IssuerName` string + `X509SerialNumber` integer).
  Its unprefixed built-in field types resolve via namespace-aware QName resolution (the schema's
  default namespace is the XSD namespace).

## Trailing/interleaved optionals — the non-strict grammar rule (construct 8)

The message header (`SessionID` required, then optional `Notification` and `Signature`) needed a
general **run of optionals**, not just a single trailing optional. cbexigen's non-strict grammar,
verified against `MessageHeaderType` (grammar 195/196/3) and `CurrentDemandResType`/
`ChargingStatusResType`: at each state the productions are the remaining optionals plus one
terminator (the next required element, or the element EE), and the event-code width is
`ceil(log2(productions + 1))`. The terminator's SE is folded into the run's event codes (when all
remaining optionals are absent it takes the highest code); reached via the last optional it sits
at its own 1-bit state. This subsumes the previously byte-verified single-trailing-optional (2-bit)
and required-single (1-bit) cases.

## Attribute grammar unified with the optional run (construct 9)

An optional attribute is not a separate grammar prefix — cbexigen makes the AT event the first
production of the content's initial state (verified against `AuthorizationReqType` grammar 222/223:
`{Id, GenChallenge, EE}` is one 2-bit state). So the optional attribute is modelled as the *leading
optional* of the content run, differing only in value encoding (a bare AT string: no
SE / value-start / child-EE). This unifies the previously separate attribute path with the
optional-run machine and lifts its restriction that the first content child be required — now
**attribute + optional content** works, while **attribute + required content**
(`CertificateChainType`) stays byte-identical.

Not yet supported (later constructs, surfaced by the integration gate in order): an optional run
terminated by a **substitution reference** (`ChargeParameterDiscoveryResType` →
`EVPowerDeliveryParameter`), **attribute + choice** (`ParameterType`), and **attribute + repeating**
(`SalesTariffType`).
