# 2026-08-13 — EVerest `-20` AC: the wall was 29 milliseconds wide

**Both AC cells are green.** Five complete `-20` AC sessions against their stock SIL — three `AC`, two
`AC_BPT` — with their own contactor really closing, nothing injected and nothing patched. The wall that
has stood since 2026-08-03 was never their EV module. It was **when** the car raises the CP line.

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), built from source in WSL |
| Config | [`config-ac20-ours.yaml`](config-ac20-ours.yaml), their `config-sil-ac-d20.yaml` shape — plain TCP, EIM |
| Ours | `EverestInteropTests.OurEvcc_AgainstTheirEvseV2G_RunsToCompletion`, `V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac` |
| New instrument | [`tools/interop-everest/carsim-on-trigger.sh`](../../../tools/interop-everest/carsim-on-trigger.sh) |
| Outcome | **AC 3/3, AC_BPT 2/2, all 16/16 `OK`.** Control, run between them, fails as it always did. |

## What the wall actually was

`PowerDelivery` asks the board-support layer to close the AC contactor, arms a 3 s timeout, and waits
for a `ClosedContactor` **event** (`lib/everest/iso15118/src/iso15118/d20/state/power_delivery.cpp:118-126`,
abridged — the two lines saving `previous_req` are left out):

```cpp
if (m_ctx.session.is_ac_charger() and ac_connector_closed == false and
    req->charge_progress == dt::Progress::Start) {
    m_ctx.feedback.signal(session::feedback::Signal::AC_CLOSE_CONTACTOR);
    m_ctx.start_timeout(d20::TimeoutType::CONTACTOR, 3000);
    logf_info("Waiting for contactor is closed");
    return {};
}
```

Two things follow from that, and together they are the whole story.

**`is_ac_charger()` is why `-20` DC never meets this.** DC falls straight through to `handle_request`
and answers. There is no contactor wait in the DC path at all — which is the answer to *"why is DC
testable and AC not"*, and it has nothing to do with AC being harder.

**`-2` does not meet it either, for a different reason.** `EvseV2G` stores the value —
`v2g_ctx->contactor_is_closed = status` (`modules/EVSE/EvseV2G/charger/ISO15118_chargerImpl.cpp:294-300`) —
and waits in a condition-variable loop that **re-tests** it, so a `true` that arrived earlier is
remembered. `libiso15118` remembers nothing: `ac_connector_closed` starts `false` and only a
`ClosedContactor` arriving **inside** the 3 s window can change it. **Latch against edge.** That, and
not anything about alternating current, is the difference between the two protocols here.

In the topology this harness has used since 2026-08-03, `sil-car.sh CP_AT_PLUGIN=1` raises the CP line
at plug-in. The IEC layer then goes `PrepareCharging → Charging → PowerOn` long before the HLC session
reaches `PowerDelivery`, `EvseManager` calls `ac_contactor_closed(true)` at that moment, and the event
is delivered to whichever `-20` state is active — which does not read it, because `PowerDelivery` is the
only state that does. It is discarded. When the window finally opens, the CP line is already at C, so
there is no second edge and nothing can arrive.

**Their SIL was producing the confirmation all along. It was thrown away.**

## The measurement

Seven sessions in eleven minutes, one station, one binary, one config. The variable is when the car
raises CP.

| # | mode | CP raised | window opens | `PowerOn` | `PowerDeliveryRes` | Δ window→response |
|---|---|---|---|---|---|---|
| 1 | AC | into the window | 10:12:24.239 | — | 10:12:27.288 | **3,049 s** `FAILED_ContactorError` |
| 2 | AC | into the window | 10:14:13.076 | +939 ms | 10:14:14.060 | **984 ms** `OK` |
| 3 | AC | into the window | 10:15:17.298 | +784 ms | 10:15:18.128 | **830 ms** `OK` |
| 4 | AC_BPT | into the window | 10:16:11.745 | +880 ms | 10:16:12.669 | **924 ms** `OK` |
| 5 | AC_BPT | into the window | 10:17:01.629 | +1005 ms | 10:17:02.684 | **1,055 s** `OK` |
| **C** | AC | **at plug-in** | 10:17:40.508 | **−4,948 s** | 10:17:43.555 | **3,047 s** `FAILED_ContactorError` |
| 7 | AC | into the window | 10:18:34.320 | +783 ms | 10:18:35.112 | **792 ms** `OK`, recorded |

The control is the row that makes this a measurement. `PowerOn` fires **4,948 s before** the window
opens — the confirmation exists, it is simply five seconds early — and the session then dies on the
timeout exactly as it has on every attempt since 2026-08-03. Six runs and three car-simulator sequences
went into that wall; the fix was to move one command by about five seconds.

The margin is comfortable: 783–1005 ms from CP rise to `PowerOn`, against 3 000 ms. The 2,5 s estimated
beforehand off the 2026-08-09 `cphold` log was pessimistic — that figure included the car simulator's own
`iso_wait_pwm_is_running` poll, which here has already completed.

Session 1 is kept in the table because it failed for a third reason worth knowing: with CP held at B the
station gives up after ~45 s (`PrepareCharging → T_step_EF → Car Paused`), and from `Car Paused` the
wake-up takes longer than the 3 s window. The arm fired correctly, 42 ms after the trigger — the station
simply could not act on it. **The session has to start inside the ~45 s that follow `Set PWM On`.**

## The instrument

[`carsim-on-trigger.sh`](../../../tools/interop-everest/carsim-on-trigger.sh) watches their log for a
chosen line and then publishes a JsCarSimulator command list on their own external MQTT interface. It is
the same shape as [`contactor-report.sh`](../../../tools/interop-everest/contactor-report.sh) and
inherits its two hard-won details — the polled watcher (a `tail -F | grep -m1` pipeline under
`pipefail` reports failure *on success*) and `grep -F` for a literal trigger.

The difference from `contactor-report.sh` matters for what may be claimed. That one **asserts a hardware
fact** over the HLC command interface; this one moves the **simulated car**, and the station then reaches
its own conclusion through its own IEC layer, its own `CPEvent::PowerOn`, and its own
`EvseManager::ac_contactor_closed(true)`. Nothing here tells `Evse15118D20` anything. That is why these
five sessions buy the matrix cells and the 2026-08-09 injection runs did not.

## Reproduce

```bash
/usr/sbin/mosquitto &
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-ac20-ours.yaml > charger.log 2>&1 &
CP_AT_PLUGIN=0 bash ~/everest/sil-car.sh &          # plug in, hold at state B — do NOT raise CP
bash ~/everest/sdp-probe.sh eth0                    # fresh multicast SDP per session; note the port
bash tools/interop-everest/carsim-on-trigger.sh --watch charger.log &

V2G_INTEROP_SECC='127.0.0.1:15141' V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac \
  dotnet test -c Release --filter FullyQualifiedName~OurEvcc_AgainstTheirEvseV2G_RunsToCompletion
```

`V2G_INTEROP_BPT_FIRST=1` for the AC_BPT half; their log then reads `EV selected service: AC_BPT`.

Two timing constraints, both of which cost a run here: start the session within ~45 s of `Set PWM On`,
and arm the watcher **before** the session, since the window is 3 s wide. For the control, use
`CP_AT_PLUGIN=1` and no watcher — that is the configuration of every AC run before today.

## What this does not establish

It is still their SIL, and the contactor is still simulated. What is now shown is that their `-20` AC
state machine, their IEC layer and their `EvseManager` will carry a foreign EV through a complete AC
session — including `AC_BPT` — when the contactor confirmation lands where the state machine is looking.

**One question falls out of it, unmeasured and deliberately not written up as a finding.** On a real AC
charger at nominal PWM the IEC layer charges alongside the HLC session, and an EV that raises CP before
the `PowerDelivery` exchange completes would put the BSP's confirmation outside the window in exactly the
way the control row shows. Whether their BSP re-reports, and whether a real EV can produce that ordering,
are questions about hardware this project does not have. `EvseV2G` is immune to it by construction — it
latches the value — which is the same asymmetry the
[contactor-latch report](../../reports/everest-iso20-ac-contactor-latch.md) already found in the
neighbouring lines, from the opposite direction.

## Next

- **Nothing is blocking the two AC cells any more**; they are green in the matrix and their entries in
  [`open-work.md`](../../open-work.md) are gone.
- The `-20` AC session has never run **over TLS**, and now can. Same for a **recorded reverse** run.
- The question above is worth one deliberate attempt at a decision — read `EvseManager`'s CP handling for
  whether a `PowerOn` already latched is re-reported when `AC_CLOSE_CONTACTOR` is signalled. If it is
  not, that is a report, and it is a different one from the pointer bug.
