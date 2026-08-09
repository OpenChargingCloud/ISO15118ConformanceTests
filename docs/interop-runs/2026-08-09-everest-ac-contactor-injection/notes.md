# 2026-08-09 — EVerest -20 AC: a contactor reported **open** is charged through

**The defect read out of their source on 2026-08-09 reproduces on the wire.** Told over their own MQTT
command interface that the AC contactor did *not* close, their station cancels the timeout that would
have refused, answers `PowerDeliveryRes(OK)`, and runs three `AC_ChargeLoop` exchanges to a clean
`SessionStop`. 2 of 2. The control, identical but for the injection, ends at `FAILED_ContactorError`
after the full 3 s.

| | everest-core **2026.02.1** (`b61bb12`), `configs-ours/config-ac20-ours.yaml`, plain TCP, EIM |
|---|---|
| Our side | `EverestInteropTests.OurEvcc_AgainstTheirEvseV2G_RunsToCompletion`, `V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac` |
| Injection | [`tools/interop-everest/contactor-report.sh`](../../../tools/interop-everest/contactor-report.sh) |
| Report | [`everest-iso20-ac-contactor-latch.md`](../../reports/everest-iso20-ac-contactor-latch.md) |

## The four runs, one variable

| run | CP held at C | injected | `PowerDeliveryRes` | after the window opened |
|---|---|---|---|---|
| `control` | no | — | **`FAILED_ContactorError`** | 3.000 s — the `CONTACTOR` timeout |
| `cphold` | yes | — | **`FAILED_ContactorError`** | 3.032 s — the same timeout |
| `inject` #1 | no | `ac_contactor_closed(false)` | **`OK`** → 3× `AC_ChargeLoop` → `SessionStop` | **99 ms** |
| `inject` #2 | no | `ac_contactor_closed(false)` | **`OK`** → 3× `AC_ChargeLoop` → `SessionStop` | **95 ms** |

The latency column is the measurement. 3.000 s is the timeout expiring; ~95 ms is
`stop_timeout(CONTACTOR)` being called by the arriving event. A `false` does not merely fail to hold
the session — it *ends the wait early*, which is the opposite of what the event says.

Second run, to the millisecond (station clock is UTC+2, the injector logs UTC):

```
14:15:36.186  iso15118_charge  :: Waiting for contactor is closed     <- window opens
14:15:36.187  evse_manager:Ev  :: CAR ISO AC HLC Close contactor
12:15:36.220  contactor-report -> ac_contactor_closed(false)          <- = 14:15:36.220
14:15:36.281  evse_manager:Ev  :: EVSE ISO V2G PowerDeliveryRes       <- 61 ms after it
14:15:36.286  evse_manager:Ev  :: CAR ISO V2G AcChargeLoopReq         <- so the code was OK
```

[`flow.inject2.md`](flow.inject2.md), from our own recorder, all fifteen pairs `OK`:

```
| 9  | PowerDeliveryReq  | PowerDeliveryRes  | OK |
| 10 | AC_ChargeLoopReq  | AC_ChargeLoopRes  | OK |
…
| 14 | SessionStopReq    | SessionStopRes    | OK |
```

## Why the injection is legitimate evidence and not a trick

`ac_contactor_closed` is a **command on their own `ISO15118_charger` interface**, and in a running
station their `EvseManager` is what calls it — with `false` at `EvseManager.cpp:1139` and `:1156`, the
second on `CPEvent::PowerOff`, which their own `types/board_support_common.yaml:9` defines as
*"Hardware confirms that contactors switched off correctly and are not welded"*.

We publish the identical command on the identical topic
(`everest/modules/iso15118_charger/impl/charger/cmd/ac_contactor_closed`), in the framework's own
wire format. Nothing in EVerest is patched, rebuilt or reconfigured, and the state machine has no way
to tell our publisher from theirs. Same principle as
[`mqtt-authorize.sh`](../../../tools/interop-everest/mqtt-authorize.sh), which has stood since
2026-08-02.

What this does **not** establish is that a deployed charger with real hardware reaches the same window
with a `false` in flight. That is a question about their BSP and it stays theirs to answer; the report
asks it rather than assuming.

## The wall, and what did not happen

The `-20` AC wall from [2026-08-03](../2026-08-03-everest-ac/notes.md) is still there:
`cphold` — their CP line held at state C from the plug-in, which is the closest this harness gets to
driving their EV-side hardware — still ends at `FAILED_ContactorError`. The two AC matrix cells do not
move.

**One anomaly, recorded because it happened and not because it is understood.** The first session of
the day, with CP held and *no* injection (the watcher had a bug and published nothing), ran to
completion: `PowerDeliveryRes` 180 ms after the request, three charge loops, clean stop. Re-running
that same configuration as `cphold` gave the timeout again. So: 1 of 2, not reproduced, cause unknown,
and **no claim is made from it** — in particular not that the wall has lifted. Its log was overwritten
by the next run before it was understood to be interesting, which is its own lesson about tagging runs
before reading them.

That anomaly is the reason the injection runs deliberately do *not* hold CP: with the contactor never
closing for real, the window is the full 3 s and the only thing that can end it early is what we sent.

## Two bugs of our own, both in the harness

- **`mosquitto` is at `/usr/sbin`, not on a login `PATH`.** `setsid nohup mosquitto` failed silently,
  the manager then exited on `Cannot connect to MQTT broker`, and the first attempt looked like an
  EVerest problem for a minute.
- **`tail -F | grep -q -m1` under `set -o pipefail` reports failure on success.** grep exits on the
  match, `tail` takes `SIGPIPE`, and `pipefail` propagates that. The first watcher printed *"trigger
  never appeared"* 21 ms **after** the trigger appeared, and published nothing — which is what made
  the anomalous run injection-free without anyone intending it. Now polled instead.

## Reproduce

```bash
/usr/sbin/mosquitto &
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-ac20-ours.yaml > charger.log 2>&1 &
CP_AT_PLUGIN=0 bash ~/everest/sil-car.sh &            # plug in, but do NOT hold CP at state C
bash ~/everest/sdp-probe.sh eth0                      # fresh multicast SDP per session; note the port

bash tools/interop-everest/contactor-report.sh --status false --watch charger.log &

V2G_INTEROP_SECC='[fe80::…%eth0]:50000' V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac \
  dotnet test -c Release --filter FullyQualifiedName~OurEvcc_AgainstTheirEvseV2G_RunsToCompletion
```

Drop the `contactor-report.sh` line for the control. Use the fixture rather than
`WWCP_ISO15118_EVCC --connect`: the CLI's per-message timeout is 2 s against their 3 s contactor
timeout, so it hangs up before the response arrives and the response *code* — the whole measurement —
is never seen.

## Artifacts

`their-charger.{control,cphold,inject,inject2}.log` (ANSI stripped), `injection{,2}.log`,
`flow.inject{,2}.md`, `frames.inject.log`, `our-evcc.control.log`.

No session trace: `SessionTrace.Build` refuses a session that ended on a `FAILED_*` response, and the
successful ones are only interesting beside their control.

## Next

- The report's remaining unticked items are now the human ones plus **which repository it belongs
  in** — `power_delivery.cpp` is byte-identical in `everest-core` and standalone `EVerest/libiso15118`.
- The AC wall is still the way into those two matrix cells, and still needs their EV-side hardware
  simulation driven rather than only their CP line.
- The anomaly above deserves one deliberate attempt at reproduction before it is forgotten.
