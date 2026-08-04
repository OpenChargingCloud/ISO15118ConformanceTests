# Task: complete ISO 15118-2 + XMLDSig over EXI fragments (Phase 3)

## Context

You're working in the repo `D:\Coding\OpenChargingCloud\Vanaheimr.V2G.Exi` — a .NET 10 library
for ISO 15118-2/-20 EXI. Architecture:

- `WWCP_ISO15118_EXI/` — EXI primitives incl. string value tables, signed
  integer, binary; V2GTP; hand-written AppProtocol codec (reference, untouched).
- `WWCP_ISO15118_EXI_SourceGenerator/` — Roslyn generator: five -2 XSDs
  (MsgDef/MsgHeader/MsgBody/MsgDataTypes/xmldsig) → `WWCP_ISO15118_2`.
  Supports import/choice/extension/substitutionGroup/attribute/unbounded.
- `WWCP_ISO15118_EXI_Tests/` — NUnit, vector-driven; `tools/cbv2g-ref/` is a
  CLI harness around libcbv2g (pinned commit) with the appHand and iso-2 modules.
- Docs: `docs/xsd-inventory-15118-2.md`, `docs/xsd-to-csharp-mapping.md`.

Read before starting: `README.md`, both docs, the vector test infrastructure, and the
generated -2 code (understand its shape before extending it).

## Preconditions (check these first)

- Phase 2 is complete: the -2 schema set generates without diagnostics,
  SessionSetupReq/Res + ServiceDiscoveryReq/Res validated byte-exact against cbV2G.
- `tools/cbv2g-ref/` builds and can do iso-2 encode/decode.

If any of this is missing: stop and report it — don't build it on the side.

## Goal

1. **All 17 request/response pairs** of ISO 15118-2 (2013) are validated with vectors
   against cbV2G: SessionSetup, ServiceDiscovery, ServiceDetail,
   PaymentServiceSelection, PaymentDetails, Authorization, ChargeParameterDiscovery,
   PowerDelivery, MeteringReceipt, SessionStop, CertificateInstallation,
   CertificateUpdate (common); ChargingStatus (AC); CableCheck, PreCharge,
   CurrentDemand, WeldingDetection (DC).
2. **XMLDSig signatures** can be created and verified: EXI fragment encoding
   of the referenced elements, EXI encoding of the SignedInfo, ECDSA secp256r1/SHA-256.

## Part A — Message coverage

### A1. Build the vector corpus systematically

- Per message, at minimum: happy path, every optional-field combination
  (present/absent), boundary values for bounded integers and enums, empty vs.
  fully populated lists (e.g. SAScheduleList, ParameterSets, MeterInfo).
- Tackle the most complex candidates first, since they surface the most generator
  bugs: ChargeParameterDiscoveryReq/Res (substitution of the abstract
  EVChargeParameter/EVSEChargeParameter, SalesTariff), PowerDeliveryReq (ChargingProfile),
  CertificateInstallationRes (nested dsig types, base64-heavy),
  CurrentDemandReq/Res (many PhysicalValues, optional fields).
- Every vector: encode diff against cbV2G, decode of cbV2G bytes, roundtrip.
- Expect generator gaps: constructs SessionSetup didn't exercise
  (deeply nested choice, dsig types as fields). Fix them in the generator,
  never via hand-written special cases in the generated code.

### A2. Ergonomics layer (keep it small)

- `PhysicalValueType` helper: construction from decimal + unit, back-conversion
  multiplier/value → decimal, rounding behavior documented and tested.
- No further convenience APIs in this phase — the simulation layer (Phase 5)
  defines what's actually needed.

## Part B — XMLDSig over EXI fragments

Background (ISO 15118-2 §7.10 / Annex J): signed elements are NOT canonicalized
as XML, but encoded as an **EXI fragment** (schema-informed, strict,
bit-packed); on top of that, SHA-256 → the Reference's DigestValue. The SignedInfo
is itself encoded as an EXI fragment with the **xmldsig schema**; these bytes
are signed with ECDSA (secp256r1, SHA-256). Signed elements in -2:
AuthorizationReq and MeteringReceiptReq (each 1 Reference, via the Id attribute),
SalesTariff (inside ChargeParameterDiscoveryRes), and in CertificateInstallationRes/
CertificateUpdateRes a **multi-reference case** (ContractSignatureCertChain,
ContractSignatureEncryptedPrivateKey, DHpublickey, eMAID in ONE signature).

### B1. Fragment grammars in the generator

- EXI spec §8.5.3: fragment grammar = FragmentContent with SE productions for
  the global element declarations (sorted lexicographically) + ED.
- Extend the generator so it additionally emits a fragment encoder/decoder per
  schema set (`EncodeFragment(element)` /
  `DecodeFragment(bytes)`), at minimum for the signable elements named above
  and for SignedInfo (the xmldsig schema set).
- Settle via the oracle (not by speculation): the fragment's header byte,
  value-table state (fresh per fragment), and whether the digest runs over the
  stream including the header. EXIficient can do fragments
  (`-fragment -schema … -strict`) — use it as the reference.

### B2. Crypto wiring

- Only `System.Security.Cryptography` (ECDsa, P-256, SHA-256) — no
  third-party crypto.
- API sketch: `V2GSignatureBuilder` (takes signable elements with an Id, produces
  a SignatureType with References + SignatureValue) and `V2GSignatureVerifier`
  (checks digests + signature against a public key). Verify the SignatureValue
  format (r‖s concatenation, 32 bytes each) against the oracle.
- Test keys: check in a one-time-generated P-256 key pair as PEM under
  `Tests/TestData/`, clearly marked "test only".

### B3. Validation against independent stacks

- **Fragment bytes**: diff against EXIficient (CLI, pinned version) for every
  signable element; check in vectors as before (`referenceEncoder` field).
- **End-to-end signature flow**: use our code to produce a signed
  AuthorizationReq and verify it with an independent stack — a small Python
  script against Josev (the `iso15118` repo, its signature utilities) or
  RISE-V2G's SecurityUtils is practical. Conversely: check a signature produced
  there with our verifier. Once in each direction is enough.
- The multi-reference case (CertificateInstallationRes) must be explicitly tested.

## Guardrails

- Only change wire semantics based on concrete diffs against cbV2G/EXIficient.
- Encryption (ECDH/AES for ContractSignatureEncryptedPrivateKey) and
  PKI/certificate-chain validation are OUT of scope — this is about encoding
  and signing, not the full PnC key-management story.
- `dotnet test -c Release` stays green without a C toolchain/Java/Python/network;
  external oracles are only used for vector (re-)generation.
- Existing tests (AppProtocol, GeneratedCodecDiffTests, grammar unit tests)
  stay green; secure generator fixes construct by construct with mini-XSD tests.
- Small commits, only on a green build.

## Definition of Done

1. All 17 message pairs: encode/decode/roundtrip against cbV2G@<sha>,
   both directions, vectors checked in.
2. Fragment encoder generated; fragment bytes for all signable elements
   byte-identical to EXIficient@<version>.
3. Signing + verifying works; cross-validation with at least one
   independent stack in both directions documented; the multi-reference case
   tested.
4. PhysicalValueType helper with rounding tests.
5. All existing tests green; README + docs updated
   (coverage matrix: message × validated-against).
6. Closing report: generator gaps found, EXI detail questions and how they were
   settled via the oracle.
