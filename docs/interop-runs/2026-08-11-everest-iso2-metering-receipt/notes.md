# 2026-08-11 — the ISO 15118-2 signed-metering path, followed end to end through three stacks

The entropy audit earlier today ended on a lesson: *some requirements have no observable in a single
message*. This is the opposite case and it came out of the same afternoon — a requirement with a
perfectly good observable that nobody had looked at, because the field is optional and a decoder that
prints what is present never mentions what is not.

| | |
|---|---|
| Requirement | `[V2G2-902]` — the `MeterInfo` the SECC sends **shall** be the meter's own output and nothing else. `[V2G2-903]` — the EVCC **shall** sign the `MeteringReceiptReq`. `[V2G2-904]` — the SECC/SA **may** verify it, or (NOTE 1) hand it to a secondary actor. `[V2G2-481]` — the refusal code, conditional |
| Measured | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), `EvseV2G`, from frames recorded 2026-08-02 |
| Read | [SwitchEV/iso15118](https://github.com/SwitchEV/iso15118) `d645255`; our own `Secc2` |
| Outcome | EVerest sends `MeterInfo` with **two of its five elements** and discards the receipt; Josev verifies the receipt but sends no `MeterInfo`; ours does both. Filed: [`everest-evsev2g-metering-chain.md`](../../reports/everest-evsev2g-metering-chain.md) |
| Artifacts | [`currentdemandres.decoded.xml`](currentdemandres.decoded.xml) · [`decode.sh`](decode.sh) |

## The measurement, and it needed no rig

`docs/interop-runs/2026-08-02-everest-iso2-dc-full-charge/frames.log` already held a complete `-2` DC
charge against their station. Entry `[30]` is a `CurrentDemandRes` of 85 bytes with
`44435f504f5745524d45544552` — `DC_POWERMETER` — legible as plain ASCII in the hex, so the presence
of a `MeterInfo` element needs no decoder at all. What needed a decoder is which of its children are
*missing*, and for that the frame went through **V2Gdecoder** (RISE-V2G + EXIficient) — a codec that
is neither ours nor theirs:

```xml
<ns5:MeterInfo>
  <ns6:MeterID>DC_POWERMETER</ns6:MeterID>
  <ns6:MeterReading>0</ns6:MeterReading>
</ns5:MeterInfo>
```

`MeterInfoType` is `MeterID`, `MeterReading`, `SigMeterReading`, `MeterStatus`, `TMeter` — one
mandatory, four optional. Their station sends two.

Entry `[31]`, the very next `CurrentDemandRes`, is 68 bytes and carries **no `MeterInfo`**: their
`meter_info_is_used` is a one-shot flag reset after each send (`iso_server.cpp:2048`). Kept in the
artifact as the second half of the decode because it is the natural control — the same message type,
17 bytes shorter, from the same session, showing exactly what the element costs.

## Where the signature is lost, on each side

**Going out.** `ISO15118_chargerImpl::handle_update_meter_info` is handed a whole
`types::powermeter::Powermeter` and reads `energy_Wh_import.total` — the unsigned one. The **signed**
reading is a sibling field on the same object (`types/powermeter.yaml:265-273`,
`energy_Wh_import_signed`, whose `.total` is a `SignedMeterValue` with `signed_meter_data`,
`signing_method`, `encoding_method`, `public_key`, `timestamp`), under EVerest's own comment
*"Extension for individual signed meter values"*. It is never read, and `v2g_ctx->meter_info`
(`v2g.hpp:259-263`) has no member it could go into. **`SigMeterReading` appears nowhere in the
`EvseV2G` module.**

**Coming back.** `handle_iso_metering_receipt` (`iso_server.cpp:1766-1808`) logs five fields at
TRACE, sets `ResponseCode = OK` unconditionally, and calls `publish_iso_metering_receipt_req` — which
is an empty body containing `// TODO: publish PnC only` (`:498-500`). `check_iso2_signature` has
exactly **one** call site in the module (`:1187`, the `AuthorizationReq`), and
`FAILED_MeteringSignatureNotValid` occurs only in `tests/din_server_test.cpp:414`.

So the EV signs an unauthenticated integer, and the signature it produces is dropped by an empty
function. The station knows its own `MeterID`, reading and tuple and could recompute them; the EV's
signature is the one thing in that message that exists nowhere else.

## The part that had to be got right, and nearly was not

**The first draft of this finding led with *"the station does not verify the metering receipt"*. That
is permitted.** `[V2G2-904]` is a `may`, not a `shall`, and `[V2G2-481]`'s duty to answer
`FAILED_MeteringSignatureNotValid` is qualified with *and the SECC requires that the signature is
valid*. A report built on the verification would have been answered in one line and rightly.

Reading two clauses further moved it: `[V2G2-902]` is a plain `shall` about the **outbound** half, and
`[V2G2-904]`'s NOTE 1 says what the `may` presupposes — that the SECC can pass the signed receipt to a
secondary actor which re-verifies it later. Neither happens here, and the fix for the first is a
sibling field the handler already has in hand.

Fourth time this month that reading one clause further changed a claim, and the second time it
*rescued* one instead of retiring it — the `[V2G20-2188]` and `[V2G20-1618]` lookups both cost a
finding, this one relocated it.

## The three-stack table

| stack | `SigMeterReading` sent | receipt verified |
|---|---|---|
| **Josev** (`-2`) | **no** — `meter_info=…get_meter_info_v2()` is commented out at both call sites (`iso15118_2_states.py:2147`, `:2494`), so no `MeterInfo` goes out at all | **yes** — `:1962-1979`, stopping the session with *"Unable to verify signature of MeteringReceiptReq"* |
| **EVerest `EvseV2G`** | **no** — two of five elements | **no**, and not forwarded either |
| **ours** (`Secc2`) | **yes** when `InstalledMeter` is set, `null` when there is no meter | **yes** — digest plus dual-grammar signature check, one `Iso2ReceiptResult` per receipt |

Each stack implements a different half. Worth stating in the filing because it makes the requirement
neither obscure nor uniformly met — and because Josev verifying a receipt for a record it never sends
is the mirror image of EVerest sending a record whose receipt it never reads.

**Not filed against Josev.** Their `-2` `MeterInfo` is commented out rather than half-built, and
nothing in their SECC sets `ReceiptRequired`, so the path is switched off rather than broken. That is
an unimplemented option, not a defect, and the difference is exactly the one this directory's *What is
deliberately not here* section is about.

## What this does not decide

- **`SigMeterReading` was never seen on any wire, from anyone.** Ours can emit it and no counterparty
  has asked for a receipt, so no session in this repository contains one. The claim is about what
  EVerest's station omits, not about what a signed record looks like in the field.
- **Their powermeter may not produce a signed value in the SIL.** `energy_Wh_import_signed` is
  optional on the type; whether their simulated meter fills it was not checked. That does not affect
  `[V2G2-902]` — a station that drops the field unconditionally cannot forward it when a real meter
  does — but it does mean the *observable* consequence needs a meter that signs.
- **The receipt round trip was not exercised against them.** `receipt_is_required` is reachable over
  their MQTT interface (the same route as the contactor injection on 2026-08-09), and driving it would
  turn "the publish function is empty" into "we sent a signed receipt and nothing appeared on any
  topic". That is a rig session of its own and the filing's checklist does not claim it.
- **The one-shot `meter_info_is_used` flag** — MeterInfo appears in one response and not the next — is
  visible in the artifact and deliberately left out of the filing. Whether a station should repeat the
  record in every charge-loop response is a different question with no requirement cited yet.
- **`[V2G2-902]` names `ChargingStatusRes`, and the frame measured is a `CurrentDemandRes`.** The
  clause is worded for the **AC** message and the text to hand states nothing equivalent for the DC
  one; the recorded session happened to be DC. The finding survives because both are filled from the
  same three-member `v2g_ctx->meter_info` by two blocks written identically
  (`iso_server.cpp:1717-1726` and `:2041-2050`), so the AC path the clause names has the same
  omission — but that is an argument about their code, not a measurement of the AC message, and the
  filing says so in the same words. **An AC capture would close it**, and one has never been recorded
  with `MeterInfo` in it. Cheapest outstanding item here by a distance.

## Reproduce

```bash
bash docs/interop-runs/2026-08-11-everest-iso2-metering-receipt/decode.sh
```

Needs a JRE and V2Gdecoder (`bash tools/interop-v2gdecoder/setup.sh`). Both frames are inlined in the
script, lifted verbatim from the 2026-08-02 run with the 8-byte V2GTP header stripped, so the decode
stands alone if that run's `frames.log` ever moves.
