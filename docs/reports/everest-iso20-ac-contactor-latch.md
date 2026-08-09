# Draft report to EVerest — `-20` AC treats "contactor did **not** close" as "closed"

Status: **draft, not sent.** Read out of `everest-core` **2026.02.1** (`b61bb12`) on 2026-08-09 and
then **reproduced against a running station the same day, 2 of 2, with a control** —
[`2026-08-09-everest-ac-contactor-injection`](../interop-runs/2026-08-09-everest-ac-contactor-injection/notes.md).
Post it under your own name; see *Before sending* at the bottom.

Two ways to check it, neither of which needs our stack:

```bash
# the mechanism, against your header with your warning set — exit 0 means it is still there
EVEREST_CORE=/path/to/everest-core bash tools/everest-contactor-probe/build.sh

# the behaviour, against your running SIL — publishes one command on your own interface
bash tools/interop-everest/contactor-report.sh --status false --watch charger.log
```

Two other reports for the same project are in [`everest-loop-shutdown.md`](everest-loop-shutdown.md)
and [`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md), and a third goes to your fork
of Josev's certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)). **File them
separately.** The framing in the first of those — what everest-core has been worth to this project,
and why a report from us is not a bug filed by a stranger — applies here unchanged and is not repeated.

---

**Title:** `libiso15118` `d20::state::PowerDelivery`: `ac_connector_closed` is assigned a **pointer**,
so a board-support module reporting the AC contactor **open** latches the session to "closed" and the
charger answers `PowerDeliveryRes(OK)`

**Version:** everest-core **2026.02.1** (`b61bb12`), libiso15118 **0.9.1** as vendored in-tree at
`lib/everest/iso15118`. The file is **byte-identical** in standalone
[`EVerest/libiso15118` @ `main`](https://github.com/EVerest/libiso15118) — 5663 bytes, SHA-256
`32f5a223989b4657…` — so this has two homes and one of them is presumably canonical. See the checklist.

## Summary

Told that the AC contactor did **not** close, your `-20` station charges anyway. Four sessions on your
stock SIL, one variable between them:

| CP held at C | `ac_contactor_closed` | `PowerDeliveryRes` | after the wait began |
|---|---|---|---|
| no | — | `FAILED_ContactorError` | 3.000 s — the timeout |
| yes | — | `FAILED_ContactorError` | 3.032 s — the timeout |
| no | **`false`** | **`OK`** → 3× `AC_ChargeLoop` → `SessionStop` | **99 ms** |
| no | **`false`** | **`OK`** → 3× `AC_ChargeLoop` → `SessionStop` | **95 ms** |

The latency is the tell: a `false` does not merely fail to hold the session, it *ends the wait early* —
`stop_timeout(CONTACTOR)` runs on the event that reported the failure.

Here is why.

`PowerDelivery::feed` handles the `ClosedContactor` control event like this:

```cpp
// src/iso15118/d20/state/power_delivery.cpp:52-58
} else if (const auto* control_data = m_ctx.get_control_event<ClosedContactor>()) {
    ac_connector_closed = control_data;                     // <-- the pointer, not the value

    if (not ac_connector_closed) {
        logf_warning(
            "Got ClosedContactor event, but contactor is not closed.  Waiting until the contactor is closed");
        return {};
    }

    m_ctx.stop_timeout(d20::TimeoutType::CONTACTOR);
    …
    return m_ctx.create_state<AC_ChargeLoop>();
```

`ac_connector_closed` is a `bool` (`power_delivery.hpp:21`) and `control_data` is a
`const ClosedContactor*`. The assignment is therefore a **pointer-to-bool conversion**: true because
the pointer is non-null, which inside this branch it always is. The value it points at — the whole
content of the event — is never read.

Three consequences, in order of how much they matter:

1. **The `not ac_connector_closed` guard can never be taken.** The warning below it is unreachable
   code. Its text says exactly what the author meant the false case to do.
2. **`stop_timeout(CONTACTOR)` runs anyway**, so the 3 s contactor timeout that would otherwise have
   produced the correct `FAILED_ContactorError` is cancelled by the very event that reported failure.
3. **The state machine answers `PowerDeliveryRes` with `OK` and enters `AC_ChargeLoop`** — it tells the
   vehicle that power delivery has started, moments after its own hardware layer said the contactor
   is open.

`ClosedContactor` is consumed in this one place; no other state reads it.

## It is reached from your own production code, not only in theory

`EvseManager` forwards Control-Pilot events to the HLC layer, and two of the three calls pass `false`:

```cpp
// modules/EVSE/EvseManager/EvseManager.cpp:1136-1157
if (event == CPEvent::CarPluggedIn) {
    r_hlc[0]->call_reset_error();
    r_hlc[0]->call_ac_contactor_closed(false);
    …
}
if (event == CPEvent::PowerOn) {
    contactor_open = false;
    r_hlc[0]->call_ac_contactor_closed(true);
}
if (event == CPEvent::PowerOff) {
    contactor_open = true;
    …
    r_hlc[0]->call_ac_contactor_closed(false);
}
```

`Evse15118D20` turns that command straight into the event
(`modules/EVSE/Evse15118D20/charger/ISO15118_chargerImpl.cpp:826-831`):

```cpp
void ISO15118_chargerImpl::handle_ac_contactor_closed(bool& status) {
    std::scoped_lock lock(GEL);
    if (controller) {
        controller->send_control_event(iso15118::d20::ClosedContactor{status});
    }
}
```

And `PowerOff` is not an edge case in your own vocabulary
(`types/board_support_common.yaml:9`): *"Hardware confirms that contactors switched off correctly and
are not welded"*. That is the board-support module positively confirming the contactor is **off** —
and in `PowerDelivery` it is read as confirmation that it is **on**.

The window is the one `PowerDelivery` spends waiting: `PowerDeliveryReq(Start)` arrives, the state
requests `AC_CLOSE_CONTACTOR`, starts the 3 s timeout and logs *"Waiting for contactor is closed"*
(`power_delivery.cpp:117-127`). Any `ac_contactor_closed` call landing in that window ends the wait,
whichever value it carries.

There is a second, quieter effect once the latch is set. The guard at `power_delivery.cpp:118` is
`… and ac_connector_closed == false and …`, so a subsequent `PowerDeliveryReq(Start)` skips the
close-contactor request altogether and answers `OK` directly.

## Why we think it is worth fixing

**Because the layer that knows is being overruled by the layer that does not.** The `-20` state machine
has exactly one input describing the physical contactor, and this discards it while keeping the shape
of having read it — the guard is present, the log message is written, the timeout is stopped as though
the news had been good. A reviewer reading the state machine sees a false case handled.

We are deliberately **not** claiming a specific physical hazard. Whether the cable is energised is
`EvseManager`'s business and the BSP's, not this state machine's, and you know that side; what we can
say is that the HLC layer confirms power delivery to the vehicle against its own hardware layer's
report, which is a disagreement that should not be possible to construct. **How bad that is, is your
call, and it is the main question we would want answered before this is filed as anything more than a
correctness bug.**

Two things suggest oversight rather than decision, which is why this is written as a report and not a
question: the unreachable warning, and the contrast below.

## Your `-2` module does the same thing correctly

`EvseV2G` receives the identical command and handles it as intended
(`modules/EVSE/EvseV2G/charger/ISO15118_chargerImpl.cpp:294-300`):

```cpp
void ISO15118_chargerImpl::handle_ac_contactor_closed(bool& status) {
    pthread_mutex_lock(&v2g_ctx->mqtt_lock);
    v2g_ctx->contactor_is_closed = status;          // the value
    pthread_cond_signal(&v2g_ctx->mqtt_cond);
    pthread_mutex_unlock(&v2g_ctx->mqtt_lock);
```

and `iso_server.cpp:1556-1570` then waits on `contactor_is_closed` in a condition-variable loop that
re-tests it, so a `false` keeps waiting and the timeout still fires `FAILED_ContactorError`. Same
signal, same protocol family, opposite outcome.

## Why `-Wall -Wextra -Werror` did not catch it

libiso15118 is built with `"-Wall;-Wextra;-Wno-unused-function;-Werror"`
(`lib/everest/iso15118/CMakeLists.txt:53`), which is stricter than most, and this still compiles clean:
an implicit pointer-to-`bool` conversion in an assignment is well-formed C++ and neither GCC nor Clang
diagnoses it under those flags. The probe below compiles with exactly that set to show it.

Worth mentioning because it says something about where to look for siblings: `-Werror` gives no cover
here, and the same shape — `x = ptr` where `x` is `bool` and `ptr` came out of a
`get_control_event<T>()` — is mechanically greppable across the other states.

## Reproduction — on a running station

Your stock `config-sil-ac-d20.yaml` shape, plain TCP, EIM. Plug the simulated car in but **do not**
hold CP at state C, so the contactor never really closes and the window is the full 3 s. Then publish
one command on your own interface, in the framework's own wire format, while `PowerDelivery` waits:

```bash
bash tools/interop-everest/contactor-report.sh --status false --watch charger.log
# everest/modules/iso15118_charger/impl/charger/cmd/ac_contactor_closed
# {"msg_type":"Cmd","data":{"id":"…","args":{"status":false},"origin":"…"}}
```

Nothing is patched, rebuilt or reconfigured, and the state machine has no way to tell our publisher
from your `EvseManager`. Second run, station clock UTC+2, injector UTC:

```
14:15:36.186  iso15118_charge  :: Waiting for contactor is closed     <- window opens
14:15:36.187  evse_manager:Ev  :: CAR ISO AC HLC Close contactor
12:15:36.220  contactor-report -> ac_contactor_closed(false)          <- = 14:15:36.220
14:15:36.281  evse_manager:Ev  :: EVSE ISO V2G PowerDeliveryRes       <- 61 ms after it
14:15:36.286  evse_manager:Ev  :: CAR ISO V2G AcChargeLoopReq         <- so the code was OK
```

Drop the injection line and the same session ends `FAILED_ContactorError` at 3.000 s.

## Reproduction — the mechanism alone, no station

The probe compiles against **your** `control_event.hpp`, so the class under test is yours rather than
a retyped copy, and uses your warning set:

```bash
EVEREST_CORE=/path/to/everest-core bash tools/everest-contactor-probe/build.sh
```

```
compiled clean under -Wall -Wextra -Werror

  as written    ac_connector_closed = control_data    -> true  (contactor treated as CLOSED)
  as intended   ac_connector_closed = *control_data   -> false (contactor open, as reported)
```

## Suggested direction

The one-character version is `*control_data`, which invokes the `operator bool()` that
`ClosedContactor` already provides for this purpose (`control_event.hpp:74-85`). We would send a PR if
you want one, but you may prefer one of these instead, and the choice is yours:

1. **`ac_connector_closed = *control_data;`** — minimal, and makes the existing warning branch live.
2. **Make the conversion impossible rather than correct.** `get_control_event<T>()` returning
   `std::optional<T>` (or the states taking `const T&`) removes the whole class of mistake from every
   state at once, at the cost of a wider change.
3. **Keep the timeout running until the contactor is confirmed closed.** Independently of the above:
   `stop_timeout(CONTACTOR)` is currently reached on any `ClosedContactor` event, so even with the
   value read correctly it is worth checking that the false path leaves the timeout armed — otherwise
   a single `false` disarms the refusal and the session waits forever instead of failing.

## Also seen, not reported

Our `-20` AC and AC_BPT runs against your SIL cannot reach the window on their own: they end at
`FAILED_ContactorError` from the **timeout**, because in that topology no `ClosedContactor` event
arrives at all — the contactor confirmation your `EvseManager` waits on comes from your own EV module,
which in our setup has no session. Six runs now, three different car-simulator sequences, 2026-08-03
through 2026-08-09. That is a property of driving your SIL with a third-party EV and we do **not**
report it as a defect: it is written up in
[`2026-08-03-everest-ac`](../interop-runs/2026-08-03-everest-ac/notes.md) and
[`2026-08-06-everest-bpt`](../interop-runs/2026-08-06-everest-bpt/notes.md). It is, however, how we came
to read this file — and it is what makes the injection above a clean measurement, since with the
contactor never really closing, the only thing that can end the 3 s wait early is what we sent.

One session on 2026-08-09 did complete with CP held and nothing injected, and re-running the same
configuration gave the timeout again. 1 of 2, unexplained, and no claim is made from it — noted here
only so that it is not discovered later and mistaken for something we hid.

---

## Before sending

- [x] **Reproduce it on a running station, not only in the type system.** Done 2026-08-09: 2 of 2, with
      a control that fails the way it should, on their stock AC `-20` configuration
      ([run notes](../interop-runs/2026-08-09-everest-ac-contactor-injection/notes.md)). This was the
      report's weak point and it is closed; what it says now is *observed*, not *read*.
- [ ] **Decide which repository it belongs in.** The file is byte-identical in `EVerest/everest-core`
      at `lib/everest/iso15118/` and in standalone `EVerest/libiso15118` @ `main`. One is presumably
      generated from the other; we could not tell which from the outside, and filing into the mirror
      wastes everyone's time. Ask, or file in `libiso15118` and cross-reference. (This is the same trap
      as [`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md), which needed sending twice.)
- [x] **Re-check every line reference against the tree.** All ten were read from the built 2026.02.1
      source on 2026-08-09, which is the same day this was written.
- [ ] **Ask about impact rather than asserting it.** We do not know their BSP and cannot say whether a
      `PowerOff` during the `PowerDelivery` wait is realistic in a deployed charger or an artefact of
      how the SIL sequences events. Lead with the correctness defect, which is not in doubt, and let
      them classify the severity.
- [ ] **Mention the greppable sibling pattern** — `bool x = ptr` out of `get_control_event<T>()` — so
      they can sweep the other states while they are in the file. We checked only this one.
- [ ] **File one issue, this one.** The SIL's own-EV coupling is not part of it, deliberately.
- [ ] **Post under your own name, in your own words.**
