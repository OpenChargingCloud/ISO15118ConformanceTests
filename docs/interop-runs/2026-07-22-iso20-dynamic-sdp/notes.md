# Interop run — ISO 15118-20 **Dynamic control mode** (DC, DC_BPT, AC_BPT), live via `--sdp`: Josev EVCC → our SECC

- **Date:** 2026-07-22
- **Our side:** `secc --protocol 20 --mode dc|ac --dynamic --sdp --interface eth0` (plain TCP).
- **Josev:** EVCC, docker host-mode, `evcc_config_{dc,dc_bpt,ac_bpt}.json` — three runs.
- **Outcome:** ✅ all three full **Dynamic-mode** sessions to `SessionStop`, EVCC exited 0 each time:
  - DC: `✓ Session complete in 15008 ms`
  - DC_BPT: `✓ Session complete in 13513 ms`
  - AC_BPT: `✓ Session complete in 12718 ms`

## The bug this run validates the fix for

Before this change the SECC answered a **Dynamic**-mode EV with **Scheduled** response types — a wire-type
mismatch ([V2G20-1600]: the res control mode must be the same variant as the req's):

- `ScheduleExchangeRes` always carried `Scheduled_SEResControlMode` (`Secc20Base.ScheduleExchange`),
- `DC_ChargeLoopRes`/`AC_ChargeLoopRes` folded `(BPT_)Dynamic_*_CLReqControlMode` into the
  *Scheduled* res arm (`Secc20Dc.HandleChargeLoop` / `Secc20Ac.HandleChargeLoop`).

It never fired live because all previous runs were Scheduled. The fix answers **strictly in kind** for all
four control-mode variants (Scheduled / Dynamic / BPT_Scheduled / BPT_Dynamic; the BPT records derive from
the non-BPT ones, so the BPT arms match first), with the Dynamic res types carrying their **mandatory** EVSE
limits (in Dynamic mode the SECC dictates the operating point).

## How the EV ends up in Dynamic mode

A Josev EVCC adopts the ControlMode of the **first** parameter set the SECC offers in `ServiceDetailRes`
(`select_energy_service_v20` picks `parameter_sets[0]`; `is_control_mode_set` reads its ControlMode) — the
control mode is entirely SECC-driven. Our SECC now always offers **both** sets (Scheduled=1, Dynamic=2;
[V2G20-2656]), and the new `--dynamic` CLI flag (`Secc20Base.PreferDynamicControlMode`) flips the order so
the EV picks Dynamic. `MobilityNeedsMode=1` is offered for both modes ([V2G20-2663] only restricts `=2` to
Dynamic).

## Wire evidence (from the EVCC logs)

- `Selected Control Mode: 2` (DYNAMIC) in all three runs.
- `ScheduleExchangeRes … "Dynamic_SEResControlMode":{"DepartureTime":7200}` — our res echoes the EV's
  requested departure time in the Dynamic arm.
- 10 Dynamic charge-loop responses per run, e.g. DC_BPT:
  `"BPT_Dynamic_DC_CLResControlMode":{"EVSEMaximumChargePower":{"Exponent":1,"Value":5000},…}`.
- Full state walk incl. `DCCableCheck → DCPreCharge → DCChargeLoop → DCWeldingDetection` (DC runs) and
  `ACChargeLoop` (AC_BPT run); zero errors/exceptions in any log.

CI coverage: `Secc20DynamicModeTests` (parameter-set order both ways, Dynamic SE res in kind, all four
charge-loop control-mode variants for DC and AC).
Script: [`tools/interop-josev/reverse-dynamic-sdp.sh`](../../../tools/interop-josev/reverse-dynamic-sdp.sh)
(`dc|ac|dc-bpt|ac-bpt`).
Logs: `secc-*-dynamic.log` / `evcc-*-dynamic.log` per variant.
