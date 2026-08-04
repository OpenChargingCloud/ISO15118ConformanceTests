# ISO 15118-2 XSD inventory (Phase 2, step 1)

Mechanical inventory of every XSD construct/facet **actually used** by the five
ISO 15118-2 schema files. This is the binding requirements list for the generator —
implement exactly these constructs, construct by construct, verifying each against cbV2G.

Source schemas: `WWCP_ISO15118_2/Schemas/` (provenance: RISE-V2G
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

## Substitution references flattened into the run (construct 10)

A substitution-group reference is not one production with a nested selector — cbexigen inlines its
members (each concrete member **and** the abstract head, sorted by element name) as individual
productions in the enclosing grammar state, mixed with sibling optionals and the EE. Verified
against `PowerDeliveryReqType` (grammar 199/200: `{ChargingProfile, DC_EVPowerDeliveryParameter,
EVPowerDeliveryParameter-head, EE}` is one 3-bit state) and `ChargeParameterDiscoveryResType`
(grammar 284/285: an optional `SASchedules` reference and a required `EVSEChargeParameter`
reference share the state, 5 productions → 3 bits). So the optional-run machine now works on
**productions**, not particles: each particle contributes one production except a substitution
reference, which contributes one per member; the width stays `ceil(log2(totalProductions + 1))`.
An optional reference (minOccurs=0) joins the run and gains an EE alternative; a required one is the
run's terminator. Dispatch is by runtime type; the abstract head reserves its event-code slot but
has no branch (unreachable). Standalone required references keep the direct selector, with the
width corrected to `ceil(log2(members + 1))`. Abstract types (heads, extension bases) no longer emit
their own (dead, uncompilable) codec methods.

## Optional bounded-repeating element inside a run (construct 11)

A `minOccurs=0 maxOccurs=n` element (`SalesTariffEntryType` → `ConsumptionCost`) is not
count-unrolled. cbexigen makes the **first** item a production of the enclosing run's grammar state
(grammar 39: `{EPriceLevel, ConsumptionCost, EE}`, 2 bits) and loops the rest through a self-looping
`{item = 0, EE = 1}` 2-bit state (grammar 40/42); the maxOccurs bound is enforced by the array
length, not the grammar. So an optional repeating element joins the run as its last member,
contributing one production (the first-item SE); encoding then walks the list in a C# loop and
decoding reads until the 2-bit EE. The byte-verified required list (`minOccurs≥1`, e.g. AppProtocol)
keeps the 1-bit-first / 2-bit-loop path.

## Required repeating terminator — full set generates and compiles (construct 12)

The last blocker was `SalesTariffType`: an optional run `{SalesTariffDescription, NumEPriceLevels}`
terminated by a **required** (`minOccurs≥1`) repeating element `SalesTariffEntry`, under an optional
`Id` attribute. cbV2G grammar 58-63: since the terminator is required there is no run-level EE
production; its first item is the highest-code production of each state (`{Desc, Num, Entry}` → 2
bits, then `{Num, Entry}`, then `{Entry}` → 1 bit), further items and the terminating EE use the
2-bit loop. This is `EmitEncodeRunTailRepeating` / the repeating case of the run's decoder.

With this, **Phase 2 Definition of Done #1 is met**: all five ISO 15118-2 XSDs run through the
generator with zero diagnostics *and* the generated codec compiles (`SchemaSetIntegrationTests`,
both tests). Two latent bugs surfaced by adding the full-set compile gate were fixed alongside:

- a concrete type that others extend (e.g. `ServiceType`, base of `ChargeServiceType`) must not be
  emitted `sealed`;
- `xs:boolean` encodes/decodes via `x ? 1u : 0u` / `ReadBits(1) != 0` (C# has no `(uint)bool`).

`ParameterType` (required attribute + `xs:choice`) needed no new work — it already goes through the
required-attribute + choice path (construct 6).

## Phase 2 completion report

All Definition-of-Done items are met. The whole set generates without diagnostics and compiles
(#1); every construct has a grammar unit test (#2); SessionSetupReq/Res and ServiceDiscoveryReq/Res
are **byte-exact against cbV2G@03350be** — encode diffed against checked-in vectors and
encode→decode→re-encode round-trips (#3, `Iso15118_2VectorTests` / `Iso15118_2RoundtripTests`);
`docs/xsd-inventory-15118-2.md` and `docs/xsd-to-csharp-mapping.md` exist (#5); the README records
the -2 coverage (#6).

### EXI grammar details that deviated from the naive expectation

Each was pinned to cbV2G's byte output, never to spec prose:

1. **Non-strict widths carry a phantom production.** An n-production state is `ceil(log2(n+1))` bits
   wide, not `ceil(log2(n))` — a required single SE is 1 bit, a lone trailing optional is 2 bits.
   Definitively shown by PowerDeliveryReqType grammar 199 (4 productions, 3 bits).
2. **Optionals interleave as productions, not nested choices.** A run of optionals (and an optional
   attribute, whose AT event is the run's first production) shares one grammar state with the next
   required particle or the EE; event codes renumber as the cursor advances.
3. **Substitution members flatten into the enclosing state.** A `ref` to a head contributes one
   production per member *and* the abstract head (sorted by element name), inline — not a 1-bit SE
   plus a nested selector.
4. **`unsignedByte` → 8-bit n-bit unsigned; `byte` → 8-bit with bias −128.** Not multi-byte EXI
   integers.
5. **The document selector counts every global element of the set.** Sorted by name then namespace;
   `V2G_Message` is index 76 of 80 (7 bits), even though it is the only decodable root.
6. **`maxOccurs=2` is bounded-unrolled.** A full 2-item list ends with a 1-bit EE (the max-reached
   state); `maxOccurs≥3` self-loops and ends with the 2-bit loop EE. This was the one bug the
   differential vectors caught after the grammar tests were all green — the value of diffing bytes,
   not just structure.
7. **XMLDSig is opaque by decision, not omission.** The header's `ds:Signature` is encode-absent;
   the self-contained `X509IssuerSerialType` (needed by PaymentDetails) is modelled; the signature
   subtree is deferred to Phase 3.

### Not yet asserted at byte level *(historical — resolved in Phase 3)*

At the time of this inventory only the four Phase 2 target messages had checked-in cbV2G vectors.
Phase 3 closed this: **all 17 message pairs are byte-exact against cbV2G** (extended `main_iso2.c`
oracle), including XMLDSig signatures over EXI fragments — see the section below and `README.md`.

## The XMLDSig SignedInfo subtree — modelled (Phase 3, part B)

The digest a signature covers is an EXI *fragment* of the signed element, and the signature itself
is over a `SignedInfo` fragment — so `SignedInfo` and everything it contains must be modelled
concretely, unlike the rest of the (still opaque) dsig namespace. `XsdReader` whitelists the subtree
(`SignedInfoType`, `CanonicalizationMethodType`, `SignatureMethodType`, `ReferenceType`,
`TransformsType`, `TransformType`, `DigestMethodType`, plus `DigestValueType` / `HMACOutputLengthType`
and their global elements) and parses those types like any other; the grammar builder resolves a
plain (non-substitution, non-opaque) `ref` to a modelled global as that element's type. These dsig
globals occupy a document-grammar production (as before) but are never document roots — reached only
through a fragment or a containing type.

`EncodeFragment_SignedInfo` is **byte-exact against cbV2G@03350be** (`Fragment_SignedInfo`,
`Iso15118_2FragmentTests`): EXI-canonical C14N, ECDSA-SHA256 method, one Reference (no Transforms)
over a 32-byte digest.

### One more deviation the byte diff caught

8. **An `xs:any` wildcard is *two* productions, with the element EE between them.** cbexigen expands
   a wildcard into a generic wildcard event *and* the typed element it simplifies to (the synthetic
   `ANY` base64 field), ordered `… generic, EE, typed`. So a state containing an `ANY` is one
   production wider than the naive count: `SignatureMethodType` grammar 27 is
   `{HMACOutputLength, ANY(generic), EE, ANY(typed)}` → **3 bits**, not 2. For a lone trailing `ANY`
   (as in `Canonicalization-`/`DigestMethodType`) this happens to coincide with the naive 2-bit EE,
   so it only surfaced once `SignatureMethodType` put an optional *before* the wildcard — found by the
   `SignedInfo` fragment diff. The generator marks the synthetic `ANY` as a wildcard
   (`ChildPlan.IsWildcardAny`), counts it as two productions, and reserves the generic slot without
   emitting a branch (a generic wildcard event is never encoded and fails loud on decode).

## The full Signature — modelled end to end (Phase 3, part B)

With `SignedInfo` in place, the enclosing `SignatureType` (`SignedInfo`, `SignatureValue`, then the
optional `KeyInfo` and repeating `Object`) and `SignatureValueType` are whitelisted too, so the
header's `ds:Signature` is a *real* optional element rather than an opaque placeholder. `KeyInfo` and
`Object` stay opaque and encode-absent — `Object`'s `maxOccurs="unbounded"` is modelled as an opaque
optional single, which is byte-identical while absent (its first-occurrence SE is one production of
`SignatureType` state 124; the repeat only matters once present, which ISO 15118-2 never does). A
present `KeyInfo`/`Object` fails loud.

A complete signed `AuthorizationReq` (header `SignedInfo` + 64-byte `SignatureValue`) is **byte-exact
against cbV2G** (`AuthorizationReq_Signed` vector) and round-trips. The signing itself
(`V2GSignature`, ECDSA-P256 over the SignedInfo fragment, `r‖s` SignatureValue) sits on top; an
end-to-end test signs, encodes, decodes and verifies against a generated key.

9. **simpleContent with an *optional* attribute is an optional run, not a fixed prefix.** A required
   attribute is written unconditionally (a 1-bit AT), but an optional `Id` (as on `SignatureValueType`)
   joins the content grammar as the leading optional of a run whose terminator is the CONTENT value —
   `SignatureValueType` state 96 is `{Id, CONTENT}` (2 bits), state 97 `{CONTENT}` (1 bit), then a
   separate 1-bit EE. The CONTENT value is written bare (its event code doubles as the value marker; no
   value-start / child EE). The earlier simpleContent path only handled required/no attributes and
   mis-built the record constructor for the optional case — fixed with a dedicated emitter.
