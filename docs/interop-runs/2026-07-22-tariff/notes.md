# Interop run — **Signed tariffs + ChargingProfile** (-2 SalesTariff §7.9.2.5, -20 AbsolutePriceSchedule), live vs Josev

- **Date:** 2026-07-22/23
- **Scope:** the last declared non-goal, "SalesTariff/smart-charging detail": digitally signed tariff
  offers, price-aware tuple choice, PMax-shaped ChargingProfiles, and SECC-side profile validation —
  with the validation limits documented honestly (and one of them dissolving live, see below).

## What was built

- **-2 SECC** (`--tariff-cert`): two-tuple SAScheduleList — tuple 1 flat 11 kW at EPriceLevels 2→3,
  tuple 2 capped 7.4 kW on level 1 opening to 22 kW on level 2 after 30 min. Both SalesTariffs are
  digitally signed into ONE ChargeParameterDiscoveryRes header signature (one reference per tariff,
  §7.9.2.5, combined-grammar/cbV2G form via `V2GSignature`). `PowerDeliveryReq(Start)` is validated:
  unknown tuple id → `FAILED_TariffSelectionInvalid`; a ChargingProfile entry above the PMax active at
  its start → `FAILED_ChargingProfileInvalid` ([V2G2-761]) — both without advancing the phase, so the
  EV may retry. The charging-status responses echo the tuple the EV actually chose.
- **-2 EVCC** (`--tariff-cert`): verifies signed SalesTariffs (reference digest over the re-encoded
  EXI fragment + ECDSA, dual-grammar like every other check), picks the tuple with the lowest average
  EPriceLevel, and shapes its ChargingProfile to that tuple's PMaxSchedule step for step.
- **-20 SECC/EVCC**: in Scheduled mode the flat one-level PriceLevelSchedule gives way to a rich
  `AbsolutePriceSchedule` (power-banded EUR/kWh PriceRuleStacks: ≤11 kW 25 ct, above 35 ct; +30 min:
  30/45 ct), signed ECDSA-P521/SHA-512 (the -20 mandatory suite) into the response header; the EVCC
  verifies digest + signature.

## The three live runs

### 1. Reverse -2 ✅ — Josev EVCC consumes our signed two-tuple offer and charges price-aware

```
our SECC : -2 SmartCharging: EV chose tuple 2 (offered), ChargingProfile 4 entries, within PMax: OK.
           ✓ Session complete in 14692 ms.
```

Josev's own codec log shows the full decode: both tariffs, both references, the signature — and its
PowerDeliveryReq picks **SAScheduleTupleID 2** (the cheap tuple) with a profile shaped to our 7.4/22 kW
steps (4 entries incl. zero-power boundary markers), which our PMax validation accepts.

**New Josev gap on the way there:** the first attempt died with an empty-messaged
`V2GMessageValidationError` in ChargeParameterDiscovery — Josev's pydantic `Reference` model requires
`Transforms` (schema-optional) for -2 too, exactly like its -20 CertificateInstallation counterpart.
Our tariff references now carry `Transforms=[EXI C14N]` (documented in `V2GSignature.BuildSignedInfo`).

### 2. Reverse -20 ✅ — Josev AC EVCC consumes the signed AbsolutePriceSchedule

```
our SECC : Tariff: signing with CN=VanaheimrDevTariffEMSP, 521-bit EC.   ✓ Session complete in 15909 ms.
```

Full Scheduled-mode session; Josev decodes the AbsolutePriceSchedule (its log shows the EUR/kWh price
rule stacks) and completes. It never looks at the signature — no -20 implementation we know of signs
or verifies price schedules, so the -20 signature half remains in-repo-only validation (CI-guarded).

### 3. Forward -2 ✅✅ — our EVCC verifies a REAL MO-signed Josev SalesTariff

The surprise that upgraded the honesty story: **Josev's SECC signs its SalesTariff with the MO Sub-CA2
key** (its code even notes "this signature should actually be provided by the mobility operator") — the
exact spec role. With the MO Sub-CA2 certificate extracted from Josev's shipped PKI:

```
Tariff: verifying with DC=MO, C=UK, O=Switch, CN=PKI-Ext_CRT_MO_SUB2_VALID, 256-bit EC.
-2 Tariff: 1 tuple(s), signature present, digests OK, ECDSA OK (grammar=xmldsig-standalone);
           chose tuple 1, profile 1 entry.   ✓ Session complete in 2683 ms.
```

- `digests OK` — our re-encoded SalesTariff fragment reproduces Josev's digest byte-exactly, live.
- `ECDSA OK (xmldsig-standalone)` — the EVCC-side tariff verification has a genuine external oracle.
- Josev's SECC accepted our PMax-shaped ChargingProfile (external check of the profile builder).

## The honest validation ledger

| Path | External oracle? |
| --- | --- |
| -2 EVCC verifies a signed SalesTariff | **YES — live** (Josev MO-Sub-CA2-signed tariff verified, run 3) |
| -2 signed offer consumed by a real EV + profile round-trip | **YES — live** (run 1, both directions of the profile check) |
| -2 SECC *signing form* (combined grammar) verified by a peer | **No** — Josev's EVCC-side verification is a code `# TODO`; our EVCC (loopback/CI) is the only verifier |
| -20 AbsolutePriceSchedule consumed by a real EV | **YES — live** (run 2) |
| -20 price-schedule *signature* verified by a peer | **No** — nothing external signs or verifies these; CI-guarded only |

## CI

`Secc2TariffTests` (offer + one-header-signature digests/ECDSA, `FAILED_TariffSelectionInvalid`,
`FAILED_ChargingProfileInvalid` + retry, plain-offer regression),
`Iso2LoopbackTests.AcTariffSession_SignedTariffVerified_CheapestTupleProfiled`,
`Iso20LoopbackTests.DcTariffSession_SignedAbsolutePriceSchedule_VerifiesAtEv`.
Scripts: [`reverse-tariff-sdp.sh`](../../../tools/interop-josev/reverse-tariff-sdp.sh) (`2|20`),
[`live-evcc-tariff.sh`](../../../tools/interop-josev/live-evcc-tariff.sh),
[`live-evcc-tariff-verify.sh`](../../../tools/interop-josev/live-evcc-tariff-verify.sh).
Logs: `{secc,evcc}-tariff-2.log`, `{secc,evcc}-tariff-20.log`, `{josev-secc,our-evcc}-tariff.log`.
