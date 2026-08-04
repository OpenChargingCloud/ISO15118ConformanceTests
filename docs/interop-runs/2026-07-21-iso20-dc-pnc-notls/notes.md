# Interop run — ISO 15118-20 DC, Plug & Charge, no TLS

- **Date:** 2026-07-21
- **Josev:** SwitchEV/iso15118 @ `d645255`, **rebuilt on Debian trixie** (`python:3.10-trixie`, OpenJDK 21),
  EXI codec `EXICodec.jar` 1.55
- **Scenario:** Josev EVCC ↔ Josev SECC, ISO 15118-20, **DC**, **Plug & Charge (PnC)**,
  `SECC_ENFORCE_TLS=False`, `useTls:false`
  (`EVCC_CONFIG_PATH=…/examples/evcc/iso15118_20/evcc_config_dc.json`)
- **Outcome:** ✅ full PnC DC charge loop to `SessionStop`; our codec cross-validates **all 30** distinct
  frames byte-for-byte. A real codec gap surfaced (signed `AuthorizationReq` with a `Transforms` element) and
  was **fixed in the source generator** — see below.

## Why -20 without TLS

ISO 15118-20 mandates TLS 1.3 on the wire, but Josev's `-20 DC` example config sets `useTls:false` for
testing, and record mode (`MESSAGE_LOG_EXI=True`) logs the **plaintext EXI** — TLS only wraps the transport,
so it is irrelevant to cross-validating the *message* encoding. This capture therefore needs no TLS backend
at all; our BouncyCastle TLS backend matters only for *live over-the-wire* interop, which is a separate,
optional step. The negotiated application protocol is genuinely `ISO_15118_20_DC` (SAP `ProtocolNamespace`
`urn:iso:std:iso:15118:-20:DC`), and this is a PnC session — it exercises `AuthorizationSetup`, a signed
`AuthorizationReq`, and the `PnC_AReqAuthorizationMode` / `SignedInfo` signing fragments.

## Cross-validation result (checked in as a regression test)

`WWCP_ISO15118_EXI_Tests/Interop/JosevCapturedFrames20Tests.cs` (runs in CI, bytes baked in) — our codec
**decodes and re-encodes each of Josev's -20 frames to the identical bytes**, spanning the SAP handshake and
both -20 schema sets:

- **SAP (2):** `supportedAppProtocolReq` (negotiating -20 DC) / `…Res` — byte-exact.
- **CommonMessages (18):** SessionSetup, AuthorizationSetup, Authorization (incl. the signed `AuthorizationReq`,
  see below), ServiceDiscovery, ServiceDetail, ServiceSelection, ScheduleExchange, PowerDelivery, SessionStop
  (Req+Res each) — byte-exact.
- **DC (10):** DC_ChargeParameterDiscovery, DC_CableCheck, DC_PreCharge, DC_ChargeLoop, DC_WeldingDetection
  (Req+Res each) — byte-exact.

**Our codec ≡ EXIficient (Josev)** across the whole session — an independent conformance signal (Josev shares
no lineage with the cbV2G oracle our vectors come from).

## Fixed: signed `AuthorizationReq` with a `Transforms` element (source-generator gap)

The ~1.3 KB **signed** `AuthorizationReq` initially did *not* round-trip. Its header
`<Signature>`/`<SignedInfo>`/`<Reference>` includes a `<Transforms>` element carrying the
`http://www.w3.org/TR/canonical-exi/` transform, and our generated CommonMessages decoder threw
`invalid optional-run event code`. Root cause (verified against `xmldsig-core-schema.xsd` **and** cbexigen's
own generated `decode_iso20_TransformType`):

- `TransformType`'s content is `<choice minOccurs="0" maxOccurs="unbounded">` (mixed, optional, repeatable),
  but the source generator's **direct-`xs:choice` path dropped the choice's `minOccurs`/`maxOccurs`** and
  emitted a *mandatory* single choice — a 2-bit dispatch with no END-Element alternative. A `Transform` with
  only its `Algorithm` attribute and no children (exactly the EXI-canonicalisation transform) encodes as EE at
  that state; the missing EE production misaligned the bit cursor, surfacing as the parent `ReferenceType`
  optional-run failure. cbexigen models the same content as a 3-bit dispatch `{XPath=0, EE=2, ANY=3}`.
- `TransformsType`'s `maxOccurs="unbounded"` list (a single `ref="Transform"` child) got its bound recorded on
  the child but read by the emitter from the plan, leaving `ListMax=0` — so `Encode_TransformsType`'s
  `count is < 1 or > 0` guard rejected every list.

**Fix** (`WWCP_ISO15118_EXI_SourceGenerator`): the XSD reader now models an optional/repeatable direct choice
as an EE-terminated optional run (the same shape the emitter already produces byte-exact for the mixed
`SignatureMethod`/`DigestMethod` content), and the grammar builder promotes a lone repeating child's bound to
the plan level. Regenerated `Decode/Encode_TransformType` now match cbexigen's grammar exactly (empty Transform
= 3-bit `010`), and the signed frame round-trips byte-for-byte. **All existing cbV2G vectors across every -2
and -20 set still pass byte-exact** (no regression) — two independent oracles (cbV2G + EXIficient) confirm the
grammar. Regression-guarded by `Josev20_SignedAuthorizationReq_WithTransforms_RoundTripsIdentically`.

## Notes

- SessionSetup / V2G headers carry a per-session random SessionID and a wall-clock TimeStamp, so these exact
  bytes are a snapshot of this run — the roundtrip assertion is on Josev's captured bytes, which is
  deterministic per frame.
- The DC loop repeats CableCheck/PreCharge/ChargeLoop/WeldingDetection; the captured bytes are the first
  instance of each request/response type. Full ordered log in [`frames.log`](frames.log).

## Next

- (Optional) live over-the-wire -20 interop via the BouncyCastle TLS backend + the `[Explicit]`
  `JosevInteropTests` hook — record mode already gives the codec-level signal.
