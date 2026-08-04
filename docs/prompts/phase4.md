# Task: ISO 15118-20 — multi-schema codecs + V2GTP dispatch (Phase 4)

> **Update (post-Phase 4):** Ed448 — flagged below as a deliberate gap because .NET's
> `System.Security.Cryptography` has no Ed448 support — has since been added via the
> `BouncyCastle.Cryptography` NuGet package (`V2GSignature.SignEd448`/`VerifyEd448` in
> `WWCP_ISO15118_20.{CommonMessages,DC,AC}`). The step 6/DoD text below is kept
> as-is for historical accuracy; see `README.md`'s -20 signature section for the current state.

## Context

You're working in the repo `D:\Coding\OpenChargingCloud\Vanaheimr.V2G.Exi` — a .NET 10 library
for ISO 15118 EXI. State after Phase 0–3:

- `WWCP_ISO15118_EXI/` — EXI primitives (incl. string value tables, signed
  integer, binary), V2GTP header, hand-written AppProtocol codec (reference).
- `WWCP_ISO15118_EXI_SourceGenerator/` — Roslyn generator: collects all `.xsd` files
  of a project as ONE schema set, supports import/choice/extension/substitutionGroup/
  attribute/unbounded, emits document AND fragment codecs.
- `WWCP_ISO15118_2/` — generated -2 codec, all 17 message pairs
  validated against cbV2G; XMLDSig signatures (EXI fragments, ECDSA P-256/SHA-256)
  via `V2GSignatureBuilder`/`V2GSignatureVerifier`.
- `WWCP_ISO15118_EXI_Tests/` — NUnit, vector-driven; `tools/cbv2g-ref/` a CLI harness
  around libcbv2g (pinned commit) with the appHand and iso-2 modules.
- Docs: `docs/xsd-inventory-15118-2.md`, `docs/xsd-to-csharp-mapping.md`.

Read before starting: `README.md`, both docs, the generator architecture, and how
`WWCP_ISO15118_2` wires the XSDs in as AdditionalFiles.

## Preconditions (check these first)

Phases 2 and 3 are complete (full -2 codec incl. fragment machinery,
the cbv2g-ref harness builds). If anything is missing: stop and report.

## Background: what's different about -20

- **No V2G_Message wrapper.** Every message is its own global element;
  the header (SessionID hexBinary, TimeStamp unsignedLong, optional Signature)
  sits INSIDE the message.
- **Several independent schema sets**, one per namespace:
  CommonMessages, AC, DC, WPT, ACDP — all import CommonTypes + xmldsig.
  Each set has its OWN EXI document grammar. The receiver recognizes the
  set via the **V2GTP payload type** (each message set has its own ID; take
  the concrete values from the spec or libcbv2g's `exi_v2gtp.h` — don't guess).
- New data-type patterns, among them `RationalNumberType` (exponent+value, the
  counterpart of PhysicalValueType) and considerably larger/more deeply nested
  messages (ChargeLoop, ScheduleExchange).

## Goal

The schema sets **CommonMessages, DC, and AC** are fully generated and
vector-validated; a V2GTP dispatcher picks the right decoder based on the
payload type. **WPT and ACDP are explicitly out of scope** (but the
architecture must be able to accommodate them without rework — the proof is
that adding another schema set only means a new csproj + vectors).

## Steps

### 1. Source the schemas + take inventory

- The -20 XSDs (V2G_CI_CommonMessages, V2G_CI_CommonTypes, V2G_CI_AC, V2G_CI_DC
  + xmldsig) are available in the OSS ecosystem (the cbexigen repo or EVerest
  libiso15118); document source + commit. If not findable → stop and report.
- Inventory analysis as in Phase 2: list every XSD construct and facet
  actually used by the -20 schemas → `docs/xsd-inventory-15118-20.md`.
  The diff against the -2 inventory is your work list of generator gaps.

### 2. Close generator gaps (construct by construct)

- For every construct from the inventory diff: a synthetic mini-XSD +
  grammar unit test + emitter support, then move on. Keep the fail-loud
  philosophy (unknown construct = build diagnostic).
- Expected candidates (the inventory has the final say): deeper choice
  nesting, large maxOccurs values, additional built-ins. Don't implement
  anything speculatively.

### 3. Project structure: one assembly per message set

- New projects `WWCP_ISO15118_20.CommonMessages`, `….DC`, `….AC`
  (net10.0), each with its own XSD set (set XSD + CommonTypes + xmldsig) as
  AdditionalFiles + a generator reference.
- Deliberate tradeoff: this duplicates the CommonTypes types across assemblies
  (cbV2G does the same). That's fine — document it in
  `docs/xsd-to-csharp-mapping.md`. Do NOT build a shared CommonTypes assembly;
  the grammars are self-contained per set, and shared code would only create
  versioning problems.

### 4. V2GTP dispatcher

- Extend the `V2GTP` layer: a payload-type table (SAP, -2, -20 CommonMessages,
  -20 AC, -20 DC; values from the spec/libcbv2g), `TryDecode` returns the typed
  message object + set identifier; the encode side sets the payload type to
  match the given message type.
- Tests: correct mapping per set, a clean error for an unknown payload type,
  length-field validation.

### 5. Vector validation against cbV2G

- Extend `tools/cbv2g-ref/` with the iso-20 modules (libcbv2g has its own
  encoder/decoder per set).
- Vector files per set (`Iso15118_20.CommonMessages.vectors.json`, …), same
  pattern as before (referenceEncoder pinned, encode diff, decode of cbV2G
  bytes, roundtrip).
- Coverage for CommonMessages: SessionSetup, AuthorizationSetup, Authorization
  (EIM and PnC variants), ServiceDiscovery, ServiceDetail, ServiceSelection,
  ScheduleExchange (Scheduled + Dynamic mode!), PowerDelivery, SessionStop —
  plus the remaining pairs of the schema. DC: ChargeParameterDiscovery,
  CableCheck, PreCharge, ChargeLoop, WeldingDetection. AC: ChargeParameterDiscovery,
  ChargeLoop. Per message: happy path + optional-field variants + boundary values.
- Tackle the complex ones first (ScheduleExchangeRes Dynamic/Scheduled,
  DC_ChargeLoop with DisplayParameters) — they surface the most gaps.

### 6. Lift signatures to -20

- Generate a fragment encoder for the CommonMessages set (reuse the
  Phase 3 machinery); diff fragment bytes against EXIficient.
- Signature suite: -20 uses stronger suites than -2. Implement the ECDSA
  variant .NET natively supports (secp521r1/SHA-512); if the spec additionally
  calls for Ed448: do NOT implement it, document it as a known gap instead
  (.NET has no Ed448).
- `RationalNumberType` helper analogous to PhysicalValueType (decimal
  conversion, rounding tests).

### 7. Documentation

- README: architecture picture (assemblies per set, dispatcher), the -20
  coverage matrix (message × validated-against), known gaps (WPT/ACDP,
  possibly Ed448), "Next milestones" → Phase 5 (simulation).

## Guardrails

- Only change wire semantics based on concrete diffs against cbV2G/EXIficient.
- No hand-written codec code for -20 — everything through the generator;
  always back generator fixes with a mini-XSD grammar test.
- `dotnet test -c Release` stays green without a C toolchain/Java/network.
- All existing tests (-2, AppProtocol, grammar tests) stay green.
- Watch build time: the generated code gets large; keep splitting the output
  into multiple hint files, keep the generator pipeline cleanly incremental
  (no unnecessary recomputation per edit).
- Small commits, only on a green build.

## Definition of Done

1. `docs/xsd-inventory-15118-20.md` exists; the generator runs without
   diagnostics over the CommonMessages, DC, and AC sets; three assemblies compile.
2. All message pairs of the three sets: encode/decode/roundtrip against
   cbV2G@<sha>, both directions, vectors checked in.
3. V2GTP dispatcher with payload-type tests (incl. error paths).
4. Fragment bytes (CommonMessages) byte-identical to EXIficient@<version>;
   generating + verifying a secp521r1/SHA-512 signature tested.
5. RationalNumber helper with rounding tests.
6. Existing tests green; README + docs updated.
7. Closing report: generator gaps from the inventory diff, oracle decisions,
   documented deliberate gaps (WPT/ACDP, possibly Ed448).
