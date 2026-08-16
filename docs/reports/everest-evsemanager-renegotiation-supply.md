# Draft report to EVerest (`EvseManager` + `EvseV2G`) — a DC renegotiation asks for a CableCheck the station cannot pass, and leaves it `Inoperative`

Status: **draft, not sent**, and **measured on the wire twice**, with the mechanism read from the tree
that ran. Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-15-everest-iso2-renegotiation-rerun`](../interop-runs/2026-08-15-everest-iso2-renegotiation-rerun/notes.md)
and [`2026-08-16-everest-cablecheck-renegotiation`](../interop-runs/2026-08-16-everest-cablecheck-renegotiation/notes.md).

> **Read the second paragraph of *Context* before deciding how to send this.** A neighbouring report of
> ours about the same message sequence was **withdrawn** on 2026-08-15 because it was wrong. This one is
> a different claim, aimed at a different module, and it says so early because a maintainer who
> remembers the first will reasonably assume the worst about the second.

---

**Title:** `ChargeProgress = Renegotiate` publishes nothing to `EvseManager`, so the DC supply stays on
and the `CableCheck` your own state machine then requires fails with `MREC11CableCheckFault`

**Version:** everest-core **2026.02.1** (`b61bb12`), `modules/EVSE/EvseV2G` + `modules/EVSE/EvseManager`,
`config-sil-dc.yaml` shape, unmodified.

## Observed

An ISO 15118-2 DC session, one EV-initiated renegotiation mid-charge. Everything up to and including the
renegotiation is accepted:

```
PowerDeliveryReq(Renegotiate)  →  PowerDeliveryRes (OK)
ChargeParameterDiscoveryReq    →  ChargeParameterDiscoveryRes (OK)
CableCheckReq                  →  CableCheckRes (OK)      ← ×3, EVSEProcessing = Ongoing
CableCheckReq                  →  CableCheckRes (FAILED)
```

and your own log says why, and what it costs:

```
evse_manager :: EVSE ISO Start cable check...
evse_manager :: Cancel cable check wait below voltage
evse_manager :: Voltage did not drop below 60V within timeout, sending CableCheck Finished(false) anyway
evse_manager :: Error raised, type: evse_manager/MREC11CableCheckFault, sub_type: Self test failed
evse_manager :: Error raised, type: evse_manager/Inoperative
evse_manager :: Initiating error shutdown
```

**The station does not merely fail the session — it goes `Inoperative` and stops serving the next one**
until the manager is restarted. That is the part we would lead with.

**The same station, in the same session, ran the opening `CableCheck` → `PreCharge` → `PowerDelivery` →
`CurrentDemand` perfectly.** This is not a claim that your cable check is broken; it is a claim about one
path into it.

## Where it comes from

`cable_check()` opens by **verifying** the safe voltage rather than establishing it:

```cpp
// EvseManager.cpp:2028-2038
session_log.evse(true, "Start cable check...");
// Verify output is below 60V initially
if (not wait_powersupply_DC_below_voltage(CABLECHECK_SAFE_VOLTAGE)) { … fail_cable_check(oss.str()); return; }
```

and `wait_powersupply_DC_below_voltage` (`:2444`) waits and measures — it calls `powersupply_DC_off()`
only in its cancel and no-measurement branches. On the way into a session that is exactly right: nothing
has been switched on. On the **return path of a renegotiation** the supply is still serving the charge
loop.

Nothing switches it off, because of one asymmetry in `handle_iso_power_delivery`:

```cpp
// EvseV2G/iso_server.cpp:1588-1598
case iso2_chargeProgressType_Stop:
    …
    } else {                                              // DC
        conn->ctx->p_charger->publish_current_demand_finished(nullptr);
        conn->ctx->p_charger->publish_dc_open_contactor(nullptr);
    }
    break;

case iso2_chargeProgressType_Renegotiate:
    conn->ctx->session.renegotiation_required = true;     // and nothing else
    break;
```

`Stop` publishes `current_demand_finished`, which `EvseManager.cpp:865` binds to `powersupply_DC_off()`.
`Renegotiate` sets a flag `EvseV2G` reads itself and publishes nothing — and `grep -rn "enegotiat"` over
`modules/EVSE/EvseManager/` matches **nothing at all**: the module that owns the power supply has no
notion of renegotiation.

Both halves are self-consistent. `EvseV2G` routes a renegotiated DC session back through `CableCheck`,
which is what ISO 15118-2's DC state table requires (`[V2G2-565]`, `[V2G2-582]`, and your comment at that
state cites `[V2G-582]`). `EvseManager` implements a cable check that assumes it runs before any energy
was delivered. The seam between them is empty.

## What we ruled out on our side first

- **The EV's sequence.** Our car sends `CableCheck` and `PreCharge` after the renegotiated
  `ChargeParameterDiscovery` — that is the fix we made on 2026-08-15 *because of your station*, and with
  the pre-fix car your `FAILED_SequenceError` is reproducible on demand as a control.
- **The EV's status.** We re-ran the whole arm with `EVReady = false` in the isolation sequence's
  `DC_EVStatus`, in case the supply was waiting on the car to stop claiming readiness. **Byte-identical
  outcome**; nothing in the cable-check path reads that field.

## Suggested direction — and we are asking, not asserting

Three shapes, all defensible, and which is right depends on what those events mean elsewhere in your
tree:

1. **Publish the existing event.** `Renegotiate` could publish `current_demand_finished` the way `Stop`
   does, since your manager already binds it to `powersupply_DC_off()`. Smallest change; the question is
   whether that event means *"the charge loop is over"* to anything else.
2. **A pause event of its own**, if `current_demand_finished` carries session-terminal meaning.
3. **Make `cable_check()` establish the safe voltage** — call `powersupply_DC_off()` before waiting
   rather than failing when it is not already off. This one fixes every future path into the phase, not
   just this one.

We would not guess between these, and we have not written a patch.

**One thing we deliberately do not claim**: whether the DC contactor should also open. ISO 15118-2's NOTE
at the Control-Pilot requirements has the contactor staying closed through a renegotiation, and your
`Stop` path opens it — but the cable check's precondition is about the *supply*, not the contactor, so
the supply half stands on its own.

## Context

| stack | DC renegotiation |
|---|---|
| **EVerest** | sequence accepted; the required `CableCheck` then fails and the station goes `Inoperative` |
| Josev (SwitchEV) | never measured in DC — both 2026-07-22 runs were **AC**, and we say so because our own earlier report quietly used them as if they were not |
| *(ours)* | both sides implement the sequence; our station's cable check is simulated and does not model a supply |

**A neighbouring report of ours was withdrawn over this same message sequence.**
[`everest-evsev2g-renegotiation-cablecheck`](everest-evsev2g-renegotiation-cablecheck.md) claimed
`EvseV2G` was wrong to expect `CableCheckReq` after a renegotiation. It was not: our car was skipping two
required message pairs, we fixed it, and the withdrawn report is kept with its four citation errors
written into it. **This report exists because that fix let the session get one phase further.** If you
had seen the first one, this is the correction and not a re-run of it.

---

## Before sending

- [x] **Reproduce it yourself** — one DC session against your stock DC SIL config with an EV that sends
      `PowerDeliveryReq(Renegotiate)` mid-charge. No PKI, no TLS, no configuration change. Twice, once
      with `EVReady = true` and once with `false`.
- [x] **Rule out the reporter's own stack.** The sequence and the status flag were both eliminated by
      measurement, and the pre-fix car reproduces the *earlier* failure on demand as a control.
- [x] **Read the mechanism in the tree that ran**, not in a clone: `cable_check()`,
      `wait_powersupply_DC_below_voltage()`, `handle_iso_power_delivery()`, and the
      `current_demand_finished` binding are all quoted from `b61bb12` as built.
- [ ] **Re-read the line numbers against `main` on the day you post**, and say which build the
      measurement is from. `main` moves; this is 2026.02.1.
- [ ] **Lead with `Inoperative`, not with the message.** *"A renegotiation leaves the charger unable to
      serve the next car until it is restarted"* is the sentence that gets this looked at; the
      `CableCheckRes (FAILED)` is how you get there.
- [ ] **Say plainly that a neighbouring report of ours was wrong**, and link it. It costs one sentence
      and buys the benefit of the doubt this one needs.
- [ ] **Ask which of the three shapes they want** rather than proposing a patch. Offer one if they ask.
- [ ] **File one issue, this one.** The withdrawn report is not sent at all.
- [ ] **Post under your own name, in your own words.**
