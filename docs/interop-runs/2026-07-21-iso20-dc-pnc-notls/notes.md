# Interop run — ISO 15118-20 DC, Plug & Charge, no TLS

- **Date:** 2026-07-21
- **Josev:** SwitchEV/iso15118 @ `d645255`, **rebuilt on Debian trixie** (`python:3.10-trixie`, OpenJDK 21),
  EXI codec `EXICodec.jar` 1.55
- **Scenario:** Josev EVCC ↔ Josev SECC, ISO 15118-20, **DC**, **Plug & Charge (PnC)**,
  `SECC_ENFORCE_TLS=False`, `useTls:false`
  (`EVCC_CONFIG_PATH=…/examples/evcc/iso15118_20/evcc_config_dc.json`)
- **Outcome:** ✅ full PnC DC charge loop to `SessionStop`; our codec cross-validates **29 of 30** distinct
  frames byte-for-byte. One real codec gap surfaced (signed `AuthorizationReq` with a `Transforms` element).

## Why -20 without TLS

ISO 15118-20 mandates TLS 1.3 on the wire, but Josev's `-20 DC` example config sets `useTls:false` for
testing, and record mode (`MESSAGE_LOG_EXI=True`) logs the **plaintext EXI** — TLS only wraps the transport,
so it is irrelevant to cross-validating the *message* encoding. This capture therefore needs no TLS backend
at all; our BouncyCastle TLS backend matters only for *live over-the-wire* interop, which is a separate,
optional step. The negotiated application protocol is genuinely `ISO_15118_20_DC` (SAP `ProtocolNamespace`
`urn:iso:std:iso:15118:-20:DC`), and this is a PnC session — it exercises `AuthorizationSetup`, a signed
`AuthorizationReq`, and the `PnC_AReqAuthorizationMode` / `SignedInfo` signing fragments.

## Cross-validation result (checked in as a regression test)

`Vanaheimr.V2G.Exi.Tests/Interop/JosevCapturedFrames20Tests.cs` (runs in CI, bytes baked in) — our codec
**decodes and re-encodes each of Josev's -20 frames to the identical bytes**, spanning the SAP handshake and
both -20 schema sets:

- **SAP (2):** `supportedAppProtocolReq` (negotiating -20 DC) / `…Res` — byte-exact.
- **CommonMessages (17 of 18):** SessionSetup, AuthorizationSetup, Authorization**Res**, ServiceDiscovery,
  ServiceDetail, ServiceSelection, ScheduleExchange, PowerDelivery, SessionStop (Req+Res each) — byte-exact.
- **DC (10):** DC_ChargeParameterDiscovery, DC_CableCheck, DC_PreCharge, DC_ChargeLoop, DC_WeldingDetection
  (Req+Res each) — byte-exact.

On the frames it can decode, **our codec ≡ EXIficient (Josev)** — an independent conformance signal (Josev
shares no lineage with the cbV2G oracle our vectors come from).

## Known finding: signed `AuthorizationReq` with a `Transforms` element

The ~1.3 KB **signed** `AuthorizationReq` is the one frame that does *not* round-trip. Its header
`<Signature>`/`<SignedInfo>`/`<Reference>` includes a `<Transforms>` element carrying the
`http://www.w3.org/TR/canonical-exi/` transform. Our generated CommonMessages decoder throws
`invalid optional-run event code` on it. Root cause (verified against `xmldsig-core-schema.xsd`):

- `TransformType`'s content is `<choice minOccurs="0" maxOccurs="unbounded">` (optional, repeatable), but the
  **source generator emitted it as a *mandatory* single choice** (`Decode_TransformType` does a 2-bit choice
  read with `default: throw`, no empty/EE alternative). A `Transform` with only its `Algorithm` attribute and
  no children — exactly what the EXI-canonicalisation transform is — misaligns the bit cursor, surfacing as
  the parent `ReferenceType` optional-run failure.
- `TransformsType`'s `maxOccurs="unbounded"` list also got a broken bound/terminator
  (`Encode_TransformsType`: `if (list.Count is < 1 or > 0)` always throws; `Decode_TransformsType`'s loop
  terminator `if (ec != 0 || list.Count >= 0)` is always true).

cbV2G (our vector oracle) never emits `Transforms` inside a `Reference`, so this grammar path was never
validated until this Josev capture. **It is a genuine codec gap**, not a test artefact: a spec-valid signed
-20 message from any stack that includes the canonical-EXI transform currently fails to decode. Captured as
the `[Ignore]`d `Josev20_SignedAuthorizationReq_WithTransforms_RoundTripsIdentically` test (bytes baked in,
ready to un-ignore once the generator is fixed). Fixing it is a focused source-generator task (regenerate +
re-validate all -20 sets against cbV2G to prove no regression, then byte-diff this frame).

## Notes

- SessionSetup / V2G headers carry a per-session random SessionID and a wall-clock TimeStamp, so these exact
  bytes are a snapshot of this run — the roundtrip assertion is on Josev's captured bytes, which is
  deterministic per frame.
- The DC loop repeats CableCheck/PreCharge/ChargeLoop/WeldingDetection; the captured bytes are the first
  instance of each request/response type. Full ordered log in [`frames.log`](frames.log).

## Next

- Fix the xmldsig `TransformType`/`TransformsType` generator grammar, then flip the ignored test to a
  byte-exact round-trip.
- (Optional) live over-the-wire -20 interop via the BouncyCastle TLS backend + the `[Explicit]`
  `JosevInteropTests` hook — record mode already gives the codec-level signal.
