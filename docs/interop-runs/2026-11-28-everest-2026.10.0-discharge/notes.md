# 2026-11-28 — Energy back against EVerest 2026.10.0, both directions

**The first sessions of this project in which energy flowed from a foreign car, or to a foreign station.**
Until today every bidirectional session against a counterparty selected a BPT service and then charged.

| | |
|---|---|
| Counterparty | everest-core **2026.10.0** (`74845c57`), `dist-2610`, in WSL |
| Ours | outer `1230059`, EVSimulatorApp `e16b3d6`, WWCP_ISO15118 `f9dd169`. That is the stack with negative discharge limits since this morning ([`…-discharge-sign`](../2026-11-28-everest-2026.10.0-discharge-sign/notes.md)), plus two fixture changes of this run (*What changed in the fixture*, below) |
| Reverse | their `Ev15118` against our SECC, `-20` **Dynamic**, DC_BPT and AC_BPT. Our station discharging through `V2G_INTEROP_DISCHARGE` (100 A DC, 3 000 W AC), plus one charge-only DC control |
| Forward | our EVCC against their `Evse15118D20` + `EvseManager`, `-20` **Dynamic**, DC_BPT and AC_BPT. Their station is told to export by one MQTT command to their API module, `everest_api/evse_manager/cmd/set_limit_watts` = `-5000`. A charge-only control per protocol |
| Result | **DC, both directions: energy goes back, every response `OK`, every session to `SessionStop`.** AC forward: their station asks for −5 000 W and our car follows from the next loop. AC reverse: their SIL car ignores our negative target and keeps charging. That is its simulator (`EvManager`), not their protocol stack |
| Evidence | per arm the recorder's octet streams, `frames.log`, `trace.json` and flow, both logs, the derived config, and `decoded-loop.txt`. That file holds every charge-parameter-discovery, power-delivery and charge-loop frame decoded by our own codec ([`scripts/decode-loop/`](scripts/decode-loop/Program.cs)), with identical consecutive exchanges folded and the first frame of each in hex |

## What is on the wire

Values as decoded, `value × 10^exponent`; "×n" is that many consecutive charge loops with the same values.

### Reverse: their car, our station

| arm | what our station sends | what their car does | loops |
|---|---|---|---|
| [`dc20-bpt-dyn-discharge`](reverse/dc20-bpt-dyn-discharge/decoded-loop.txt) | `EVSEPresentCurrent` **−100 A** at 400 V, every loop | takes it: `OK`, no stop, `EVPresentVoltage` 0 V ×25 then 400 V ×667 | 692 |
| [`dc20-bpt-dyn-control`](reverse/dc20-bpt-dyn-control/decoded-loop.txt) | `EVSEPresentCurrent` **+120 A** at 400 V (no discharge asked) | the same: 0 V ×25 then 400 V ×664 | 689 |
| [`ac20-bpt-dyn-discharge`](reverse/ac20-bpt-dyn-discharge/decoded-loop.txt) | `EVSETargetActivePower` **−3 000 W**, every loop | **ignores it**: `EVPresentActivePower` 0 W ×18, then **+11 040 W** ×895 | 913 |

Their car states the limits it is held to negative: −150 kW / −300 A DC, −3 680 W / −1 380 W AC. It reads
ours as negative too: `Ev15118: DC BPT EVSE limits: max discharge power -50000 W` and `AC BPT … -22000 W`
in [their log](reverse/dc20-bpt-dyn-discharge/their-side.log). So our negative limits went out and came
back in, read correctly by a foreign decoder. Our station's −100 A is held inside their car's own
−300 A by magnitude, so the clamp this morning's run could only read about has now run against a car
that sends that limit negative.

### Forward: our car, their station

| arm | what their station sends | what our car does | loops |
|---|---|---|---|
| [`d20-bpt-dyn-export-long`](forward/d20-bpt-dyn-export-long/decoded-loop.txt), limit at boot | `EVSEPresentCurrent` 0 A ×2, **+125 A** at 500 V ×5, +100 A ×5, **−10 A at 200 V** ×10, **−25 A at 200 V** ×278 | `OK` throughout, states −125 A / −50 kW as its discharge limits | 300 |
| [`d20-bpt-dyn-export-late`](forward/d20-bpt-dyn-export-late/decoded-loop.txt), limit 6 s in: after `DC_BPT` was selected, 1.4 s before their supply switched on | the same ramp: +125 A ×5, +100 A ×5, −10 A ×15, −17.9 A ×5, **−25 A** ×268 | the same | 300 |
| [`ac20-bpt-dyn-export-late`](forward/ac20-bpt-dyn-export-late/decoded-loop.txt), limit 6 s in: during the charge loop | `EVSETargetActivePower` +22 080 W ×40, then **−5 000 W** ×260 | **follows**: +22 000 W ×41, then `EVPresentActivePower` **−5 000 W** ×259, from the loop after the first negative target (`[V2G20-1825]`) | 300 |
| [`d20-bpt-dyn-control`](forward/d20-bpt-dyn-control/decoded-loop.txt) | 0 A ×2, +125 A at 500 V ×1 | — | 3 |
| [`ac20-bpt-dyn-control`](forward/ac20-bpt-dyn-control/decoded-loop.txt) | +22 080 W ×3 | +22 000 W | 3 |

**−25 A at 200 V is exactly the −5 000 W asked for.** Their station settles on the export limit within
about 20 loops, and in both DC arms its `EVSEMaximumDischargePower` reads −5 000 W from the first loop on. Their log says
`DC power supply: switch ON in export mode`, and our car's −125 A arrives as `dc_ev_maximum_current_limit:
200.0` and `Max discharge current -200.000000A`: their station reads both signs by magnitude, as this
morning's source reading said.

## What did not work first, and why

- **AC, limit at boot: no session at all.** A negative limit sets their import limit to 0 and the export
  to 5 kW. Before a BPT service is selected their `EvseManager` forces the discharge limits to 0. That
  leaves an AC station with nothing to offer, so it never switches PWM on, and [`ac20-bpt-dyn-export`](forward/ac20-bpt-dyn-export/their-side.log)
  ends in `no 'Set PWM On' after plug-in`. For AC the limit has to be published once the session runs
  (`LIMIT_AFTER=6`). DC tolerated it at boot. That is their behaviour, read from the run, not judged here.
- **Three charge loops are too short.** The first pass used our car's fixed three iterations, and their DC
  station had only reached +1 A when the session ended
  ([`d20-bpt-dyn-export`](forward/d20-bpt-dyn-export/decoded-loop.txt)). `V2G_INTEROP_BATTERY` is what
  makes a session long, and for `-20` it was never wired (next section).

## What changed in the fixture

Both changes are in `ISO15118ConformanceTests.Simulation/Interop/`, tests only, no wire change:

- **`V2G_INTEROP_DISCHARGE=<magnitude>`** sets `Secc20Dc.DischargeCurrent_A` (amperes) or
  `Secc20Ac.DischargePowerW` (watts) on our station in a reverse run. The station half has existed
  since this morning (WWCP_ISO15118#60), but only the stack's own tests could reach it. It acts only in a BPT
  Dynamic session, and MCS leaves the variable unread, so the run reports it as ignored there.
- **`V2G_INTEROP_BATTERY` now reaches the `-20` car.** `RunEvccAsync` gave the battery to the `-2` car
  alone. A `-20` run that asked for one kept three iterations while the variable counted as consulted,
  so nothing said the knob had done nothing. `-20` has no interval knob, so its loop keeps the car's own pacing.

## Seen on our side, not changed by this run

- **Our DC car discharges past empty, and meters at its own voltage.** In both long DC arms the pack
  started at 50 % of 60 kWh, so 30 kWh could come out. It ended at `0.0 %`, `-38.86 kWh delivered` (and
  `-39.60` in the other arm).
  - **The SoC is clamped at empty, but the count is not.** `EvBattery.Add` stops the pack at empty yet
    adds the whole negative amount to `DeliveredWh`, while the charging side counts only what the pack
    took.
  - **The car never stops offering.** It keeps stating −125 A as its maximum discharge current at 0 %.
  - **The amount is twice the station's.** The car samples its meter at its own `EVPresentVoltage`, a
    fixed 400 V, times the station's current, while their station reports 200 V. So the car's figure is
    twice what the station says it moved.

  None of this changes a byte the station sees, and it only shows when a station discharges for long
  enough. It is ours to fix ([open-work](../../open-work.md#ours-to-fix)).
- **Our AC car states less than it draws.** Without a battery it reports `EVPresentActivePower` 22 kW in
  the same Dynamic request in which it states `EVMaximumChargePower` 11 kW. Both figures are deliberate
  and recorded in the corpus ("what the Dynamic arms always stated"). They disagree all the same.
  Pre-existing; noted, not changed.

## Their side, not findings

- **Their SIL car cannot discharge on AC, and cannot follow a target at all.** `EvManager`'s car simulator
  computes `EVPresentActivePower` as `charge_current_a × ac_nominal_voltage × 3` from the
  `iso_draw_power_regulated 16,3` command (`modules/EV/EvManager/main/car_simulation.cpp`). The station's
  target reaches it only as the simulated board's maximum current
  (`car_simulatorImpl.cpp`, `subscribe_ac_evse_target_power`), which the power figure never reads. So
  it reports 11 040 W, above the 3 680 W it states as its own maximum, whatever the station asks.
  `[V2G20-1825]` makes following the target the EV's duty. This is their simulator's, not their
  protocol stack's: `Ev15118` passes the target through to it. Not a report.
- **Their AC station asks for more than it announced.** In `AC_ChargeParameterDiscoveryRes` it states
  `EVSEMaximumDischargePower` **0**, from its AC config, and in the loop it then asks for −5 000 W. No
  requirement was found that binds `EVSETargetActivePower` to the station's own discovery limits. The
  local `-20` text has `[V2G20-1823]` to `[V2G20-1826]` on the target, and none says so. Recorded, not
  judged.
- **Their AC station's own measurement stays positive.** While our car reports −5 000 W, their
  `EVSEPresentActivePower` reads +1 611 … +1 672 W. It comes from their simulated power meter.
- **A soft overcurrent at the switch**, `L1:31.9, limit 8.52`, as their PWM drops from 53.3 % to 12.1 %.
  That is the IEC side following the new import limit while the SIL car's simulated draw stays at 32 A.
  It raised no fault, and the session ran to `SessionStop`.

## How it was run

Inside WSL, driven from PowerShell. Our side was built from its own mirror `~/i15118-dis` (artifacts
`~/wsl-artifacts-dis`), so the main tree's `~/i15118` was not touched. The system `dotnet`
(`/usr/bin/dotnet`) throughout.

```bash
# reverse: their Ev15118 against our SECC (2026-11-06 reverse configs, session started as dc_bpt/ac_bpt,
# control_mode Dynamic: their module's default, which those configs had set to Scheduled)
bash rev-discharge.sh dc20-bpt-dyn-discharge dc 100
SKIP_BUILD=1 bash rev-discharge.sh ac20-bpt-dyn-discharge ac 3000
SKIP_BUILD=1 bash rev-discharge.sh dc20-bpt-dyn-control dc 0

# forward: our EVCC against their station (2026-11-26 forward configs, unchanged)
bash fwd-discharge-batch.sh     # first pass: three loops, limit at boot
bash sync-build-dis.sh          # after wiring the -20 battery
bash fwd-discharge-batch2.sh    # second pass: V2G_INTEROP_BATTERY=…,minutes=300, LIMIT_AFTER=6 for AC

bash dec-all.sh                 # decoded-loop.txt per arm
```

The scripts are in [`scripts/`](scripts/). `fwd-discharge.sh` is `fwd-2610.sh` from the
[2026-11-26 forward run](../2026-11-26-everest-2026.10.0-forward/notes.md) with its own tree and run dir,
plus `LIMIT_W` / `LIMIT_AFTER`. How a negative limit reaches their station was read in their source first.
The API module turns a negative `set_limit_watts` into an import limit of 0 and an export schedule.
The energy manager's broker takes the export when import is 0. `EvseManager` then switches its power supply
to export (DC) or signs the AC target negative, in a `-20` BPT session only.
