# Task: lift the source generator to the real ISO 15118-2 schema world (Phase 2)

> **Update after Phase 0 (2026-07-03):** the EXI grammar model has already been
> corrected. GrammarBuilder and CodecEmitter now produce cbexigen/cbV2G's
> **non-strict** schema-informed grammar (2-bit document selector; per simple
> field an SE, value-start, and EE event bit; 2-bit loop/optional codes; enum
> index = XSD declaration order; unsignedByte → nbit(8)). Details in
> `docs/roadmap.md`, the README section "The wire model", and the memory note
> `exi-grammar-model-nonstrict`. The "real grammar construction per §8.5.4" in
> this phase builds on that instead of rediscovering it — keep verifying every
> construct against cbV2G.

## Context

You're working in the repo `D:\Coding\OpenChargingCloud\Vanaheimr.V2G.Exi` — a .NET 10 library
intended to parse and serialize ISO 15118-2 and 15118-20 EXI messages. Architecture:

- `WWCP_ISO15118_EXI/` — EXI primitives (BitReader/BitWriter, ExiPrimitives),
  V2GTP header, hand-written SupportedAppProtocol codec (stays untouched,
  serves as the reference for diff tests).
- `WWCP_ISO15118_EXI_SourceGenerator/` — Roslyn `IIncrementalGenerator` (netstandard2.0):
  `Xsd/XsdReader.cs` (XSD parser), `Grammar/GrammarBuilder.cs` (XSD → grammar plan),
  `Emit/CodecEmitter.cs` (plan → C#). Today it only understands the tiny subset
  the AppProtocol schema needs: global elements, sequence, simpleType restrictions,
  bounded repetition. Philosophy: unknown constructs produce a loud
  build diagnostic, never a silent skip — keep it that way!
- `WWCP_ISO15118_EXI_Tests/` — NUnit, vector-driven (JSON + bit-exact hex diff).

Read before starting: `README.md`, the complete SourceGenerator, the hand-written
AppProtocol codec (the XML doc comments explain the EXI grammar model), and the
test infrastructure.

## Preconditions (check these first)

1. **Phase 0**: a CLI harness around libcbv2g (EVerest, pinned commit) exists
   under `tools/cbv2g-ref/` for differential vectors.
2. **Phase 1**: `ExiPrimitives` supports signed integer, binary (hex/base64Binary),
   boolean, and string value tables (local/global) with a stream context.

If either precondition is missing, wholly or partly: **stop and report it**,
instead of building it on the side — they're their own work packages.

## Goal

The generator translates the complete ISO 15118-2 schema set
(`V2G_CI_MsgDef.xsd` + `V2G_CI_MsgHeader.xsd` + `V2G_CI_MsgBody.xsd` +
`V2G_CI_MsgDataTypes.xsd` + `xmldsig-core-schema.xsd`) into a new assembly
`WWCP_ISO15118_2`, and the first messages (at minimum
SessionSetupReq/Res, ServiceDiscoveryReq/Res) are validated byte-exact against cbV2G.
Full message coverage and XMLDSig signature computation are Phase 3 —
but the entire schema set must run through the generator without diagnostics and
compile.

## Steps

### 1. Source the schemas and take inventory

- The -2 XSDs ship with several OSS projects (e.g. RISE-V2G under
  `RISE-V2G-Shared/src/main/resources/schemas`, and around the cbexigen ecosystem).
  Put them under `WWCP_ISO15118_2/Schemas/` and document
  source + commit in a README next to them. If you can't find them: stop and report.
- Write a small throwaway analysis script (may live in the scratchpad) that lists
  every XSD construct and facet actually used across the five XSDs
  (import, choice, extension, abstract, substitutionGroup, attribute, unbounded,
  anonymous types, built-ins used, …). This inventory is your binding requirements
  list — implement exactly that, no more. Save the result as
  `docs/xsd-inventory-15118-2.md`.

### 2. Extend XsdReader

At minimum (the inventory makes the final call):
- `xs:import`/`xs:include` across multiple files and **multiple namespaces**.
  Watch the generator architecture: today generation happens per AdditionalFile;
  going forward, all `.xsd` files in a set must be collected (`Collect()`) and
  resolved as ONE schema set (mapped by targetNamespace; schemaLocation only as a hint).
- `xs:attribute` (incl. use=required/optional, xs:ID → string).
- `xs:choice` (including occurrence constraints, dsig uses choice+unbounded).
- `xs:complexContent`/`xs:extension` (type inheritance — used throughout -2,
  e.g. BodyBaseType as the base of all message bodies).
- Abstract elements + `substitutionGroup` (among others `BodyElement`, `TimeInterval`).
- `maxOccurs="unbounded"` and arbitrary bounded values at any position
  (no longer only as a single child).
- Anonymous inner complexTypes.

### 3. GrammarBuilder: real EXI grammar construction

Replace the ad-hoc patterns with grammar construction per W3C EXI 1.0
(Second Edition) §8.5.4 (schema-informed grammars), strict mode:
- Per complexType: AT events first (sorted lexicographically by QName),
  then the content per the particle model; EE placement and event-code bit
  widths exactly per spec (n productions → ⌈log₂ n⌉ bits, 1 production → 0 bits).
- Choice/optionality/repetition as productions with correct event codes.
- Substitution groups: SE productions for all members at the head-reference site.
- Strict mode: no built-in extensions, no xsi:type/xsi:nil (not needed by the
  -2 schemas).
- **For every ordering/detail question (sortings, event-code assignment), cbV2G's
  byte output is the arbiter** — build small vectors early rather than arguing
  against the spec prose for a long time.
- Write grammar unit tests on synthetic mini-XSDs (one per construct):
  expected production tables and event-code widths as assertions. This keeps
  grammar construction testable independently of the emitter.

### 4. Extend CodecEmitter

- C# mapping: complexType → record; extension hierarchies and substitution groups
  need polymorphism (abstract base record + derived records); choice →
  closed hierarchy or index wrapper — decide consistently and document
  the mapping rules in `docs/xsd-to-csharp-mapping.md`.
- hexBinary/base64Binary → `byte[]`; signed built-ins → sbyte/short/int/long.
- Thread the Phase 1 value-table context through all generated encode/decode paths.
- Split the output into multiple hint files (per namespace or type group) — the
  -2 codec gets large, a single .g.cs becomes unwieldy.
- Stay AOT-friendly: no reflection, no LINQ in hot paths.

### 5. New project + differential validation

- Project `WWCP_ISO15118_2` (net10.0) with the five XSDs as
  `AdditionalFiles` and a generator reference (OutputItemType="Analyzer").
- Extend `tools/cbv2g-ref/` with libcbv2g's iso-2 module
  (encode/decode for `iso2_exiDocument`).
- New vector file `Iso15118_2.vectors.json` (same format, `referenceEncoder`
  pinned): SessionSetupReq (SessionID = 8×0x00 in the header, EVCCID), SessionSetupRes
  (ResponseCode, EVSEID, optional EVSETimeStamp — exercises signed long + optional),
  ServiceDiscoveryReq (both optional fields × present/absent),
  ServiceDiscoveryRes. Each with encode, decode, and roundtrip; plus the
  reverse direction: cbV2G-encoded bytes → our decoder.
- Important: all -2 messages sit inside the `V2G_Message` wrapper (Header + Body,
  body content via BodyElement substitution) — so the vectors automatically
  also validate the document grammar, the header (hexBinary SessionID), and
  substitution dispatch.

### 6. Documentation

- README: architecture picture (generator capabilities, new assembly), current
  state of -2 coverage (which messages are validated), "Next milestones" → Phase 3.

## Guardrails

- No hand-written -2 codec — everything runs through the generator. The
  hand-written AppProtocol codec and all existing tests
  (incl. `GeneratedCodecDiffTests`) stay green.
- Work construct by construct, incrementally: first a synthetic mini-XSD + grammar
  test + emitter support, then the next construct; the real schema is the
  integration test at the end of each iteration.
- Keep fail-loud: whatever the generator can't do becomes a build diagnostic.
- `dotnet test -c Release` stays runnable without a C toolchain/Java/network
  (vectors are checked in; the cbV2G CLI is only used for regeneration).
- Never change wire semantics speculatively — only based on a concrete diff.

## Definition of Done

1. All five -2 XSDs run through the generator without diagnostics; the generated
   assembly compiles.
2. Grammar unit tests for attribute ordering, choice, extension,
   substitutionGroup, unbounded, and optional elements are green.
3. SessionSetupReq/Res and ServiceDiscoveryReq/Res: encode/decode/roundtrip
   byte-exact against cbV2G@<sha> (both directions), vectors checked in.
4. All existing tests still green.
5. `docs/xsd-inventory-15118-2.md` and `docs/xsd-to-csharp-mapping.md` exist.
6. README updated.
7. Closing report: which EXI grammar details diverged from the naive expectation
   (sortings, event codes) and how they were verified against cbV2G.
