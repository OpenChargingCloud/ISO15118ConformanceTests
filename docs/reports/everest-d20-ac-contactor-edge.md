# Draft report to EVerest — `-20` AC waits for a contactor *edge* it may already have missed

Status: **draft, not sent.** Read against `everest-core` **`main` (`ebcd36d`)** and **2026.02.1
(`b61bb12`)** on 2026-08-13, and **measured against a running station the same day**, with a positive
control that succeeds — [`2026-08-13-everest-d20-ac-contactor-window`](../interop-runs/2026-08-13-everest-d20-ac-contactor-window/notes.md).
Post it under your own name; see *Before sending* at the bottom.

> **Live on `main`.** This is **not** the `ClosedContactor` pointer bug — that one
> ([`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md)) was fixed by the
> missing `*` and withdrawn unsent. This is the neighbouring lines, it survives that fix, and on `main`
> its scope has widened: the gate now covers `is_ac_der_iec_charger()` as well.

Five other reports for this project are listed in [`README.md`](README.md). **File them separately.**
The framing in [`everest-loop-shutdown.md`](everest-loop-shutdown.md) — what everest-core has been worth
to this project, and why a report from us is not a bug filed by a stranger — applies here unchanged and
is not repeated.

---

**Title:** `libiso15118` `d20::state::PowerDelivery`: the AC contactor wait can only be satisfied by an
event arriving *during* the wait, so a contactor that closed a moment earlier produces
`FAILED_ContactorError` — the charger refuses to charge because it already can

**Version:** everest-core **`main` (`ebcd36d`)**, and unchanged in **2026.02.1 (`b61bb12`)** except for
line numbers and the DER-IEC widening.

Every citation is given for **both** trees, because they differ throughout and the file names do not
disambiguate: `power_delivery.cpp` exists five times in everest-core, and a bare basename resolves
confidently to the wrong one. Paths are therefore full and every line number is labelled.

| what | file | `[main]` `ebcd36d` | `[tag]` 2026.02.1 |
|---|---|---|---|
| the flag, per state entry | `lib/everest/iso15118/include/iso15118/d20/state/power_delivery.hpp` | 21 | 21 |
| its only writer | `lib/everest/iso15118/src/iso15118/d20/state/power_delivery.cpp` | 61-70 | 52-58 |
| the gate and the 3 s timeout | `lib/everest/iso15118/src/iso15118/d20/state/power_delivery.cpp` | 123-131 | 118-126 |
| the request handler — permission only | `modules/EVSE/EvseManager/EvseManager.cpp` | 396-399 | 418-421 |
| close needs both permissions | `modules/EVSE/EvseManager/Charger.cpp` | 700 | 663 |
| the three CP-edge calls | `modules/EVSE/EvseManager/EvseManager.cpp` | 1123-1143 | 1136-1157 |
| the level nobody reports | `modules/EVSE/EvseManager/EvseManager.hpp` | 330 | 322 |
| CP event → that level | `modules/EVSE/EvseManager/Charger.cpp` | 1205-1210 | 1168-1172 |
| the `-2` wait, for contrast | `modules/EVSE/EvseV2G/iso_server.cpp` | 1735-1762 | 1546-1573 |
| the `-2` latch write | `modules/EVSE/EvseV2G/charger/ISO15118_chargerImpl.cpp` | 305 | 294 |

## Summary

`PowerDelivery` asks for the AC contactor, arms a 3 s timeout, and then waits for a `ClosedContactor`
**event**. Your `EvseManager` only ever sends that event on a **Control-Pilot edge**. It holds the
contactor's actual state in `contactor_open` and never re-reports it, and the `-20` state machine has no
way to ask.

So whenever the contactor is *already closed* when `PowerDeliveryReq(Start)` is processed, nothing can
end the wait: the edge has been and gone, the level is right, and the session dies at 3 s with
`FAILED_ContactorError`.

Measured on your stock `-20` AC SIL, one variable — **when the simulated car raises CP**:

| CP raised | `PowerOn` relative to the wait | `PowerDeliveryRes` |
|---|---|---|
| at plug-in | **−4,948 s** (before) | `FAILED_ContactorError` at **3,047 s** |
| at plug-in, 2026-08-09 | **−1,163 s** (before) | `FAILED_ContactorError` at **3,032 s** |
| into the wait, ×5 | **+783…1005 ms** | **`OK`** → `AC_ChargeLoop` ×3 → `SessionStop` |

The five successes are the control that matters: same station, same binary, same config, same hour.
Nothing was injected and nothing patched in any of the seven — the only difference is whether your own
`CPEvent::PowerOn` happened to fall inside the three-second window.

## The mechanism — a level read as an edge

**The flag is per-entry and starts false**
(`lib/everest/iso15118/include/iso15118/d20/state/power_delivery.hpp:21`, both trees):

```cpp
bool ac_connector_closed{false};
```

**Its only writer is an event delivered while this state is active**
(`lib/everest/iso15118/src/iso15118/d20/state/power_delivery.cpp:61-70` `[main]`; `:52-58` `[tag]`,
where the pointer bug used to be):

```cpp
} else if (const auto* control_data = m_ctx.get_control_event<ClosedContactor>()) {
    ac_connector_closed = *control_data;
    …
    m_ctx.stop_timeout(d20::TimeoutType::CONTACTOR);
```

`ClosedContactor` is consumed here and nowhere else, so one that arrives while another state is active
is discarded.

**The gate then reads that flag**
(`lib/everest/iso15118/src/iso15118/d20/state/power_delivery.cpp:123-131` `[main]`; `:118-126` `[tag]`):

```cpp
if ((m_ctx.session.is_ac_charger() or m_ctx.session.is_ac_der_iec_charger()) and not ac_connector_closed and
    req->charge_progress == dt::Progress::Start) {
    previous_req = *req;
    m_ctx.feedback.signal(session::feedback::Signal::AC_CLOSE_CONTACTOR);
    m_ctx.start_timeout(d20::TimeoutType::CONTACTOR, 3000);
    logf_info("Waiting for contactor is closed");
    return {};
}
```

**And the request that goes out sets a permission, not a query**
(`modules/EVSE/EvseManager/EvseManager.cpp:396-399` `[main]`; `:418-421` `[tag]`):

```cpp
r_hlc[0]->subscribe_ac_close_contactor([this] {
    session_log.car(true, "AC HLC Close contactor");
    charger->set_hlc_allow_close_contactor(true);
});
```

That flag is one half of the close condition — `modules/EVSE/EvseManager/Charger.cpp:700` `[main]`
(`:663` `[tag]`) requires `hlc_allow_close_contactor and iec_allow_close_contactor`. **If the contactor
is already closed, setting it changes nothing**, so no new CP transition occurs and no new event is
produced.

**The three calls that could produce one are all edges**
(`modules/EVSE/EvseManager/EvseManager.cpp:1123-1143` `[main]`; `:1136-1157` `[tag]`):

```cpp
if (event == CPEvent::CarPluggedIn) { … call_ac_contactor_closed(false); … }
if (event == CPEvent::PowerOn)      { contactor_open = false; call_ac_contactor_closed(true);  }
if (event == CPEvent::PowerOff)     { contactor_open = true;  call_ac_contactor_closed(false); }
```

The state is right there — `std::atomic_bool contactor_open`
(`modules/EVSE/EvseManager/EvseManager.hpp:330` `[main]`, `:322` `[tag]`), kept in step with the CP
events (`modules/EVSE/EvseManager/Charger.cpp:1205-1210` `[main]`, `:1168-1172` `[tag]`). It is simply
never volunteered, and never asked for.

## Your `-2` module handles both orderings, and that is the whole argument

`EvseV2G` does two things this path does not
(`modules/EVSE/EvseV2G/iso_server.cpp:1735-1762` `[main]`; `:1546-1573` `[tag]`):

```cpp
if (conn->ctx->contactor_is_closed == false) {          // 1. reads the CURRENT state first
    conn->ctx->p_charger->publish_ac_close_contactor(nullptr);
    …
    while ((rv == 0) && (conn->ctx->contactor_is_closed == false) && …) {   // 2. re-tests on every wake
        rv = pthread_cond_timedwait(&conn->ctx->mqtt_cond, &conn->ctx->mqtt_lock, &ts_abs_timeout);
```

If the contactor is already closed, the whole block is **skipped** and the response goes out
immediately. If it is not, the wait re-reads a **latched** value that `handle_ac_contactor_closed` wrote
(`modules/EVSE/EvseV2G/charger/ISO15118_chargerImpl.cpp:305` `[main]`, `:294` `[tag]`), so an early
`true` is still there when the wait looks. Same signal, same module graph, same `EvseManager` — opposite outcome.

Two protocols, one of which cannot lose the news. That contrast is why we think this is an oversight
rather than a decision.

## What we are **not** claiming

**We have not shown that a deployed charger meets this ordering**, and we are not asserting it. Our
evidence comes from driving your SIL with a third-party EV, where we control when the car raises CP.

What makes us think it is worth your time anyway: in AC at **nominal PWM** — which is your own `-20` AC
SIL default, `ac_hlc_use_5percent: false`, logged as *"AC mode, HLC enabled(X1) … we can go directly to
nominal PWM"* — the IEC 61851 layer is already charging alongside the HLC session. Whether a vehicle in
that mode can reach state C before your `-20` `PowerDelivery` exchange completes is a question about
real EVs and your BSP, and you know that side. **If it can, this is a charge that fails on a working
contactor. If it cannot, this is a robustness defect in a state machine that discards its only input.**
Either way the code cannot recover, and that part is not in doubt.

We deliberately have **no** severity claim here. Please classify it.

## Suggested direction

The choice is yours; three shapes, cheapest first.

1. **Read the level when the wait begins.** The state `EvseManager` already keeps is
   `contactor_open`; publishing it once in the `subscribe_ac_close_contactor` handler — before or
   instead of only setting the permission — closes the gap without touching `libiso15118`:
   ```cpp
   r_hlc[0]->subscribe_ac_close_contactor([this] {
       session_log.car(true, "AC HLC Close contactor");
       charger->set_hlc_allow_close_contactor(true);
       if (not contactor_open) {
           r_hlc[0]->call_ac_contactor_closed(true);   // it is already closed — say so
       }
   });
   ```
2. **Give the interface a level to read.** `ac_contactor_closed` is a command; a *variable* carrying the
   current state, which the state machine reads on entry, removes the ordering question entirely and
   would serve `AC_OPEN_CONTACTOR` too.
3. **Make the state machine re-check on timeout rather than fail.** Before answering
   `FAILED_ContactorError`, ask once. This is the smallest change inside `libiso15118` and the weakest
   of the three, because it still fails when the answer cannot be had synchronously.

We would send a PR for (1) if it is the shape you want.

## Reproduction

Your stock `config-sil-ac-d20.yaml` shape, plain TCP, EIM. **Nothing is patched, rebuilt or
reconfigured**; the only external input is your own car simulator's MQTT interface.

```bash
# fails: the car raises CP at plug-in, so PowerOn precedes the wait by seconds
CP_AT_PLUGIN=1 bash tools/interop-everest/sil-car.sh &
#   -> PowerDeliveryRes = FAILED_ContactorError, 3,047 s after "Waiting for contactor is closed"

# succeeds: the car holds at state B and raises CP when the window opens
CP_AT_PLUGIN=0 bash tools/interop-everest/sil-car.sh &
bash tools/interop-everest/carsim-on-trigger.sh --watch charger.log &
#   -> PowerOn +783…1005 ms, PowerDeliveryRes = OK, three AC_ChargeLoop exchanges, SessionStop
```

Your own log tells the whole story without our stack in it — the failing run, 2026-08-13:

```
10:17:35.560  EVSE IEC Event PowerOn                                  <- the contactor closes
10:17:40.508  iso15118_charge  :: Waiting for contactor is closed     <- 4,948 s later, the wait begins
10:17:43.555  EVSE ISO V2G PowerDeliveryRes                           <- 3,047 s: FAILED_ContactorError
```

`grep` for those three lines in any AC `-20` session log where the car drew power early.

---

## Before sending

- [x] **Reproduce it on a running station, not only in the type system.** Done 2026-08-13, with a
      five-session positive control that succeeds under the opposite ordering
      ([run notes](../interop-runs/2026-08-13-everest-d20-ac-contactor-window/notes.md)).
- [x] **Check it is live on `main`, not only on the release.** `ebcd36d`, read 2026-08-13. The
      neighbouring pointer bug is fixed there and this is not; the gate has in fact widened to
      `is_ac_der_iec_charger()`.
- [x] **Keep it apart from the withdrawn latch report.** Different lines, different mechanism, survives
      that fix. Say so in the issue, because a maintainer who remembers the `*` will otherwise read this
      as a rehash.
- [ ] **Re-read the `[main]` line numbers on the day you post.** `main` moves daily and most of this
      report is `main`. Note that `check_citations.py` resolves against whichever tree `TREE_EVEREST`
      names — the release here — so it prints the *release* lines for every `[main]` citation above and
      they are offset by a handful. That is expected; the table in *Version* is the authority, and the
      release column is the half the tool actually checks. Do not "correct" the table from its output.
- [ ] **Ask about impact rather than asserting it.** We do not know whether a real EV at nominal PWM
      reaches state C before `PowerDelivery` completes. Lead with the correctness defect — a discarded
      input that cannot be recovered — and let them classify severity. Do **not** lead with "your
      charger fails to charge"; we have not shown that on hardware.
- [ ] **Offer the one-line `EvseManager` patch** rather than a `libiso15118` change, since the state
      that is missing already exists on that side.
- [ ] **Mention the greppable sibling pattern** — any `get_control_event<T>()` whose result is latched
      into a per-state member and then waited on. `AC_OPEN_CONTACTOR` is the obvious neighbour and we
      have **not** checked it.
- [ ] **File one issue, this one.** The SIL's own-EV sequencing is not part of it, deliberately.
- [ ] **Post under your own name, in your own words.**
