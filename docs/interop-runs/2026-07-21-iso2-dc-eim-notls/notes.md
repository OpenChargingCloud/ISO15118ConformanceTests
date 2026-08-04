# Interop run — ISO 15118-2 DC, EIM, no TLS

- **Date:** 2026-07-21
- **Josev:** SwitchEV/iso15118 @ `d645255`, **rebuilt on Debian trixie** (`python:3.10-trixie`, OpenJDK 21),
  EXI codec `EXICodec.jar` 1.55
- **Scenario:** Josev EVCC ↔ Josev SECC, ISO 15118-2, **DC**, EIM, `SECC_ENFORCE_TLS=False`
  (`EVCC_CONFIG_PATH=…/evcc_config_eim_dc.json`)
- **Outcome:** ✅ full DC charge loop; our codec cross-validates every DC request byte-for-byte

## Trixie build confirmed

This run rebuilt Josev on a **current Debian (trixie)** instead of the EOL buster + apt-archive
workaround from the AC run — the recommended path in `tools/interop-josev/README.md`. It builds and runs
cleanly (only difference observed: JRE is OpenJDK 21 vs 11; the EXI codec JAR runs fine on it).

## Cross-validation result (checked in as a regression test)

`WWCP_ISO15118_EXI_Tests/Interop/JosevCapturedFramesDcTests.cs` (runs in CI, bytes baked in) — our codec
**decodes and re-encodes each of Josev's DC frames to the identical bytes**, covering the full DC loop and
its DC-specific `PhysicalValue` / `DC_EVStatus` content:

| Frame | Josev EXI (EXIficient) | Our codec |
|---|---|---|
| SessionSetupReq (EVCCID 7A8812C917C0) | `8098004011d019ea204b245f0000` | round-trips identically |
| ChargeParameterDiscoveryReq (DC_extended, full DC_EVChargeParameter) | `809802086d14c116a891219094c8…` | round-trips identically |
| CableCheckReq | `809802086d14c116a891219031000500` | round-trips identically |
| PreChargeReq (target 50 V / 1 A) | `809802086d14c116a89121917100…` | round-trips identically |
| PowerDeliveryReq (Start) | `809802086d14c116a89121915000…` | round-trips identically |
| CurrentDemandReq (full DC demand + timers) | `809802086d14c116a8912190d100…` | round-trips identically |
| WeldingDetectionReq | `809802086d14c116a891219211003200` | round-trips identically |

Full frame list (EXI + Josev's decoded JSON) in [`frames.log`](frames.log).

## Notes

- The DC session includes the charge loop (repeated CableCheck/PreCharge/CurrentDemand); the captured
  bytes are the first instance of each request type.
- SessionSetup / V2G headers carry a per-session random SessionID, so these bytes are a snapshot of this
  run — the roundtrip assertion is on Josev's exact captured bytes, which is deterministic.

## Next

-20 (TLS 1.3) — capture via Josev's -20 config; validate with our codec + the BouncyCastle TLS backend.
