# Draft report to SwitchEV (Josev) and EVerest (`ext-switchev-iso15118`) — the `-20` EVCC turns the charge loop around in ≈0,53 s

Status: **draft, not sent.** Measured on the wire 2026-08-13 against **everest-core 2026.02.1**
(`b61bb12`) running `PyEvJosev`, ISO 15118-20 AC over plain TCP, with our own SECC on the other end.
Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-13-everest-d20-ac-reverse`](../interop-runs/2026-08-13-everest-d20-ac-reverse/notes.md) — two
arms, our station's own timer as the instrument, and their EVSE-side log carrying the corroborating
average.

**This is a performance deviation, not a violated timeout, and the difference is the whole reason the
measurement sat for a day before becoming a report.** ISO 15118-20 sorts the two thresholds on that
interval into different kinds,
and a report that blurs them is refutable in one sentence. §2 says which is which and cites the figure
that decides it.

---

**Title:** `V2G_EVCC_Sequence_Performance_Time` is absent from `timeouts.py` and unimplemented, so the
`-20` charge loop paces at ≈0,53 s where Tables 216/217 give the EVCC 0,25 s — and above the 0,5 s at
which a conformant SECC ends the session

**Version:** `SwitchEV/iso15118` `master` **`d645255c`** and EVerest's fork **`26f79889`** — both are
**HEAD as of 2026-08-14**, and `iso15118/shared/messages/iso15118_20/timeouts.py` is byte-identical in
the two. Exercised through everest-core `2026.02.1`'s `PyEvJosev`.

## 1. Measured, with a control

Your EVCC discovered our SECC over SDP, negotiated `urn:iso:std:iso:15118:-20:AC` and charged. Two arms,
one variable: what our station's `V2G_SECC_Sequence_Timeout` was set to.

| arm | our sequence timeout after `AC_ChargeLoopRes` | `AC_ChargeLoop` pairs | ended by |
|---|---|---:|---|
| **strict** | **0,5 s** — Table 216, `[V2G20-1500]` | **1** | our station, **2 of 2** |
| **relaxed** | 20 s — deliberately non-conformant, to measure rather than to judge | **44** | your EV, `SessionStopReq` |

Every response code in both arms was `OK`. The strict arm is not a car that failed to reach the loop:
it completes ten exchanges, gets `PowerDeliveryRes(OK)`, sends one `AC_ChargeLoopReq`, receives
`AC_ChargeLoopRes(OK)` — and then our station stops waiting.

**The direct measurement, and it is the one that matters.** Our SECC arms its 0,5 s read budget *after*
its `AC_ChargeLoopRes` has been written to the socket, and disarms it when the next request arrives.
That is exactly the interval ISO 15118-20 puts the EVCC's threshold on — response received to next
request sent — and the clock starts a hair *after* our response left, so the measurement is
conservative in your favour. It expired, twice out of twice:

```
SECC sequence timeout: EV silent for > 500 ms in the charge loop
```

**The corroborating average**, from *your* side's log in the relaxed arm:

```
20:36:20.769182 [INFO] evse_manager:Ev :: EVSE IEC Event PowerOn
20:36:44.176264 [INFO] evse_manager:Ev ::            CAR IEC Event CarRequestedStopPower
```

**23,407 s for 44 charge-loop pairs — ≈532 ms each.** That figure is request-to-request and therefore
includes our station's turnaround, so it brackets the interval from above while the 500 ms brackets it
from below. Either end is **more than 2× the 0,25 s** Table 216 allows the EVCC.

### It is not a charge-loop pacing decision — it is the session's per-message cost

Worth establishing before anyone goes looking for a `sleep` in the loop. The same log times the phase
*before* the loop, and the car simulator's script anchors both ends (`iso_wait_pwr_ready` blocks until
the ISO stack signals power ready, so `CarRequestedPower` follows `PowerDeliveryRes`):

| | SLAC matched → `CarRequestedPower` | contains | per exchange |
|---|---:|---|---:|
| relaxed arm | 5,730 s | SDP + TCP + **10** request/response pairs | ≈573 ms |
| strict arm | 6,015 s | the same | ≈600 ms |

Same order as the charge loop's 532 ms. The charge loop is simply the one phase where the standard's
budget is tight enough for it to matter — everywhere else Table 215 allows 2 s and nobody notices.

Frame counts are exact, from the recorded octet stream: 10 exchanges, 44 `AC_ChargeLoop` pairs,
`PowerDelivery(Stop)`, `SessionStop`.

## 2. What the standard asks — and which kind of threshold this is

Figure 212 draws **one** interval between message pair *n−1* and pair *n*, from the response arriving at
the EVCC to the next request leaving it, and puts **two** thresholds on it. Its legend sorts them into
different categories, and that categorisation is the substance of this section:

| threshold | kind | whose | AC charge loop | obliged by |
|---|---|---|---:|---|
| `V2G_EVCC_Sequence_Performance_Time` | **performance** criterion | EVCC | **0,25 s** | `[V2G20-1499]` |
| `V2G_SECC_Sequence_Timeout` | **error** criterion | SECC | **0,5 s** | `[V2G20-1500]` |

Table 216 gives `V2G_EVCC_Sequence_Performance_Time` exactly one row — `AC_ChargeLoopReq`, 0,25 s — and
`[V2G20-1499]` makes implementing the EVCC-specific times of that table a *shall*. Table 217 carries the
same value for `DC_ChargeLoopReq` (`[V2G20-1501]`) and Table 218 for WPT (`[V2G20-5069]`). Three tables,
one number.

**So two things are true at once, and this report keeps them apart on purpose:**

1. **Missing it is a conformance deviation, not grounds for anyone to abort.** There is no clause of the
   `[V2G20-443]` shape — stop the communication session — pointed at the *car's* sequence timer. If your
   answer is *"that is a performance target, not a hard timeout"*, you are reading the legend the same
   way we are, and this half of the report is a `shall` you are missing by 2,1×, nothing more.
2. **The consequence is not yours to define, and there it *is* an error criterion.** `[V2G20-443]` has
   the SECC end the session when its sequence timer reaches the timeout with no request received, and
   `[V2G20-1500]` puts that timeout at 0,5 s after a charge-loop response. **An EV pacing above 0,5 s
   cannot charge on a conformant station** — which needs no reading of the EVCC clause at all. That is
   the strict arm above, 2 of 2, at the *first* loop rather than somewhere in the middle.

One absence recorded rather than glossed: the document gives **no general clause that starts the EVCC's
sequence timer** the way `[V2G20-441]` does for the SECC. The neighbouring EVCC block
(`[V2G20-436]`–`[V2G20-440]`) governs `V2G_EVCC_Msg_Timeout`, the wait for a response. Figure 212 and
`[V2G20-1499]` carry the general case; the individual state-machine requirements that invoke the car's
sequence timer for the charge loop are the standby transitions (`[V2G20-2113]`, `[V2G20-1391]`,
`[V2G20-1393]`). Enough to cite, not enough to pretend the symmetry is written out — and if you think
that absence changes the reading, that is a fair conversation to have on the issue.

## 3. In your source: the parameter is not there to be missed

`iso15118/shared/messages/iso15118_20/timeouts.py` transcribes the tables carefully. It has
`V2G_EVCC_ONGOING_TIMEOUT`, `V2G_EVCC_CABLE_CHECK_TIMEOUT`, `V2G_EVCC_PRE_CHARGE_TIMEOUT`,
`V2G_EVCC_COMMUNICATION_SETUP_TIMEOUT` and `V2G_SECC_SEQUENCE_TIMEOUT`; it has the per-message
`V2G_EVCC_Msg_Timeout` values, including `AC_CHARGE_LOOP_REQ = 0.5` and `DC_CHARGE_LOOP_REQ = 0.5`,
which are **correct** for Tables 216/217.

**There is no `V2G_EVCC_SEQUENCE_PERFORMANCE_TIME` in the file, and nothing anywhere in the tree
references such a value** — `grep -rn PERFORMANCE iso15118/` finds only an unrelated local constant in
the SECC simulator. So this is a different shape from the SECC finding in the *same file*
([`josev-iso20-charge-loop-timeout`](josev-iso20-charge-loop-timeout.md), where the value is written down
and never referenced): here the parameter was never transcribed, so nothing in the EVCC measures its own
turnaround or could report it.

## 4. Where the ≈0,5 s actually goes — we did **not** localize it

Said plainly, because the rest of this report is measured and this part is not. Two things we can rule
out and one place we would look first:

- **It is not a deliberate pacing.** The only `asyncio.sleep(0.5)` on the EVCC path is commented out
  (`iso15118/evcc/controller/simulator.py`, inside `continue_charging`), and the one live sleep in the
  EVCC is the 0,1 s SDP start-up synchronisation in `comm_session_handler.py`.
- **It is not confined to the charge loop**, per the table in §1: the setup phase costs the same per
  exchange.
- **Where we would look first, unmeasured:** every message is encoded and decoded through
  `ExificientEXICodec`, a py4j gateway to a JVM — your own `iso15118/evcc/main.py:28` selects it, and
  the module that exercised it here does the same (everest-core `2026.02.1`,
  `modules/EV/PyEvJosev/module.py:88`; the same call at line 89 on `main` `8e52afd5`). Two
  inter-process round-trips per exchange is a plausible fixed cost of this order, and it is the kind of
  thing a timestamped debug log of one charge loop would settle in a minute. Plausible is not measured,
  and we are not claiming it.

## 5. Where you are right

- The `V2G_EVCC_Msg_Timeout` side of the same tables is implemented and correct — 0,5 s for the charge
  loop, against Table 215's 2 s baseline.
- The session itself was clean: 56 exchanges, **every response code `OK`**, from SDP through a signed
  `AuthorizationReq` to a proper `SessionStopReq`. Your EV is the only foreign `-20` **AC** car that has
  ever driven our station end to end, and it did it on the first attempt after a defect of *ours* was
  out of the way.
- The 44 loops in the relaxed arm are a real charge, not a sequence walk.

## 6. Suggested direction

Two separable pieces, and the first is worth more than the second:

1. **Find the per-message cost.** 0,25 s is a generous budget for building a small message; something is
   spending half a second per exchange, in every phase, and the charge loop is where the standard
   notices. This is a measurement inside your own process, not a protocol change.
2. **Give the parameter a name.** `V2G_EVCC_SEQUENCE_PERFORMANCE_TIME_{AC,DC,WPT}_CL = 0.25` in
   `timeouts.py` alongside the `_CL` constants already there, and somewhere for the EVCC to notice it has
   overrun — a warning is enough, since this is a performance criterion and aborting your own session
   over it would be worse than the deviation. A stack that logs *"charge-loop turnaround 532 ms, budget
   250 ms"* has told its user what no station will.

## 7. Context: the two halves hide each other

This is measurable at all only because a *conformant* station was on the other end.

- Their `-20` **station** — EVerest's `libiso15118`, a different codebase — flattens
  `V2G_SECC_Sequence_Timeout` to a single 60 s constant
  ([`everest-d20-sequence-timeout`](everest-d20-sequence-timeout.md), measured at 60,0025 s from their
  own log). A station that waits 60 s never discovers that its own EV takes 532 ms.
- Your **SECC** has the right numbers in `timeouts.py` and references them nowhere, so it waits 60 s too
  ([`josev-iso20-charge-loop-timeout`](josev-iso20-charge-loop-timeout.md)).

Three implementations, and the pair that ships together is exactly the pair that cannot see this. Send
this one alongside the SECC report if you send both — they are the same file and the same table, from
the two sides.

**Our own gap, and it is the symmetric one.** Our stack has **no**
`V2G_EVCC_Sequence_Performance_Time` either — no such constant anywhere in it. Our EVCC happens to turn
the loop around in ~50 ms and therefore sits inside the budget by accident rather than by design;
nothing in it measures or enforces the 0,25 s. Our *station* enforces its half, which is the only reason
this measurement exists, and it enforced it against us first.

---

## Before sending

- [x] **Observe it, do not only read it.** Two arms on the wire, 2 of 2 in the strict arm, and the
      decisive interval is measured by the timer the standard defines rather than inferred from a log.
- [x] **Have a control.** The relaxed arm shows the car reaches the loop and completes 44 of them, so the
      strict arm is not a car that never got there — and the only variable between the two is a value in
      *our* station.
- [x] **Measure the interval the standard measures.** Our read budget arms after our response is written
      and stops at the next request — Figure 212's span, and starting fractionally late, which favours
      them.
- [x] **Decide the requirement side before citing it.** Done 2026-08-13 in
      [`normative-basis.md`](../normative-basis.md), from Tables 216–218 extracted with `pdftotext -table`
      (`-layout` mis-renders these tables) and Figure 212's legend.
- [x] **Say which kind of criterion it is.** Performance for the EVCC, error for the SECC. If this report
      said "your EV violates a timeout" it would be wrong, and one sentence would close it.
- [x] **Check the pinned revisions against HEAD — checked 2026-08-14.** `SwitchEV/iso15118` `master` is
      still `d645255c` and `EVerest/ext-switchev-iso15118` still `26f79889`; `timeouts.py` fetched from
      upstream and compared against the fork, identical, and the absence holds in both. Pinned and HEAD
      are the same commit in both trees.
- [x] **Re-resolve the citations against the trees, not against the draft** —
      [`tools/reports-audit/`](../../tools/reports-audit/README.md), 2 of 2, and **it caught one before
      this was ever read by anybody**: §4 first cited `modules/PyEvJosev/module.py`, which is the
      *installed* layout under `dist/libexec/everest/`. The source path is `modules/EV/PyEvJosev/` at the
      tag as well as on `main`, and the line differs by one between them.
- [x] **Say where they are right**, and that their EV completed a session nothing else has.
- [x] **Admit our own gap.** No EVCC performance-time constant on our side either; we are inside the
      budget by accident.
- [ ] **Localize the 0,5 s, or say in the issue that you did not.** §4 is a hypothesis with a named place
      to look, and it is labelled as one. A timestamped debug log of a single charge loop would settle it
      and would make the issue far easier to act on — consider running that before posting.
- [x] **Re-run over TLS — done 2026-08-14, and it is worse.** Mutual TLS 1.3, same EV, same config, same
      20 s window: **23,400 s for 43 charge loops, ≈544 ms each**, against ≈532 ms over plain TCP. So the
      number a real `-20` deployment sees is the larger one — TLS costs about 12 ms per exchange here —
      and the deviation is 2,2× the 0,25 s rather than 2,1×. Quote whichever you like; they are the same
      finding ([`…-d20-ac-reverse-tls`](../interop-runs/2026-08-14-everest-d20-ac-reverse-tls/notes.md)).
- [ ] **Decide fork or upstream first.** Same file in two live trees, both at HEAD — the shape
      [`josev-iso20-pki-curve`](josev-iso20-pki-curve.md) handles as dependency 4: the fork is the one
      that moves, and the upstream issue can then cite it. Unlike that one, nothing here is
      fork-specific, so a single upstream issue that the fork inherits may be the honest shape. Pick one.
- [ ] **Consider sending it with the SECC half.** §7 is the argument; two issues, close together, not one
      issue with two headings — they have different fixes in different files.
- [ ] **Post under your own name, in your own words.**
