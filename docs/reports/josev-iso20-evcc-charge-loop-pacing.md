# Draft report to EVerest (`PyEvJosev`) — the `-20` EV turns the charge loop around in ≈0,53 s

> **Re-aimed 2026-08-15, and this used to be addressed to SwitchEV first.** §4 said the ≈0,5 s had not
> been localized and named the py4j/JVM codec round-trip as the first place to look. It was looked at:
> **the codec costs ~30 ms per direction**, and **Josev's own EVCC turns the same charge loop around in
> a median of 43 ms** against the same station, in the same scenario — a factor of twelve. Whatever the
> half second is, the Josev EVCC does not have it when it runs by itself, so the measurement belongs to
> the module that wraps it. [`…-josev-evcc-pacing-localized`](../interop-runs/2026-08-15-josev-evcc-pacing-localized/notes.md).
> <br>What remains for SwitchEV is one sentence, in *Not part of this*.

Status: **draft, not sent.** Measured on the wire 2026-08-13 against **everest-core 2026.02.1**
(`b61bb12`) running `PyEvJosev`, ISO 15118-20 AC over plain TCP, with our own SECC on the other end.
Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-13-everest-d20-ac-reverse`](../interop-runs/2026-08-13-everest-d20-ac-reverse/notes.md) — two
arms, our station's own timer as the instrument, and their EVSE-side log carrying the corroborating
average — and
[`2026-08-15-josev-evcc-pacing-localized`](../interop-runs/2026-08-15-josev-evcc-pacing-localized/notes.md),
which is where the ≈0,5 s stopped being unattributed.

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

## 4. Where the ≈0,5 s goes — three answers, and the third is why this is addressed to you

This section used to say *"we did not localize it"* and name the codec as the first suspect. It has been
measured since.

**It is not a deliberate pacing.** The only `asyncio.sleep(0.5)` on the EVCC path is commented out
(`iso15118/evcc/controller/simulator.py`, inside `continue_charging`), and the one live sleep in the
fork's EVCC is the 0,1 s SDP start-up synchronisation in `comm_session_handler.py`. *(True of the fork.
Upstream `d645255` is different and has a real charge-loop delay hook — see* Not part of this *below.)*

**It is not confined to the charge loop**, per the table in §1: the setup phase costs the same per
exchange.

**It is not the EXI codec.** Josev logs three timestamped lines per message and they bracket exactly the
three costs; split across four sessions and both roles, the medians are:

| | their EVCC, `-20` AC | their EVCC, `-20` DC | their EVCC, `-2` AC | their SECC, `-20` DC |
|---|---:|---:|---:|---:|
| `SENT` → `Decoded` — peer + read + **decode** | 31 ms | 25 ms | 36 ms | — |
| `Decoded` → to-encode — state handling | 1 ms | 1 ms | 1 ms | 0 ms |
| to-encode → `SENT` — **encode** + write | 30 ms | 24 ms | 32 ms | 28 ms |

Both codec halves cross the py4j gateway into the JVM, in both roles, and both cost **tens of
milliseconds** — an order of magnitude short of what has to be explained. The state machine's own
handling is 1 ms.

**And it is not in the Josev EVCC.** Their own EVCC container, upstream `d645255`, in the identical
scenario — `-20` AC, EIM, plain TCP, SDP discovery, our station on the other end:

| what drives the Josev EVCC | AC charge-loop turnaround |
|---|---:|
| your `PyEvJosev` module, fork `26f7988` | **≈532 ms** |
| Josev's own EVCC container, upstream `d645255` | **43 ms** (min 32, max 58) |

Twelve times, same codec, same protocol, same transport, same peer. That is why this report is now
addressed to you rather than to SwitchEV.

**What we could not do, and why we chose not to.** Splitting the 532 ms the same way needs Josev's INFO
lines from inside your module, and they do not exist: `modules/EV/PyEvJosev/utilities.py:30-42` clears
the root logger's handlers, installs `EverestPyLoggingHandler` and **never sets a level**, so the root
logger stays at Python's default `WARNING`. Confirmed by re-running the scenario: 0 of the expected
lines, while the session negotiated and charged normally. One line would fix it — and enabling INFO adds
per-message formatting and a log call to precisely the quantity being measured, so a number taken that
way would be a number about a modified tree. **The instrument that would finish this is disabled by the
module under measurement**, and we would rather hand you that sentence than a contaminated figure.

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

**And a fourth implementation is the reason this section survived the re-aiming.** Josev's own EVCC, run
standalone against our station, turns the loop around in 43 ms — inside Table 216's 0,25 s with room to
spare. So the stack this module wraps is not the one with the problem, and the sentence *"a station that
waits 60 s never discovers that its own EV takes 532 ms"* now has a sharper form: the only configuration
in which the 532 ms exists is the one in which nothing measures it.

**Our own gap, and it is the symmetric one.** Our stack has **no**
`V2G_EVCC_Sequence_Performance_Time` either — no such constant anywhere in it. Our EVCC happens to turn
the loop around in ~50 ms and therefore sits inside the budget by accident rather than by design;
nothing in it measures or enforces the 0,25 s. Our *station* enforces its half, which is the only reason
this measurement exists, and it enforced it against us first.

## Not part of this

- **The one thing that is still SwitchEV's.** `V2G_EVCC_SEQUENCE_PERFORMANCE_TIME` is absent from
  `timeouts.py` in **both** trees, where the SECC-side `_CL` constants are present — §3. That is a
  one-line note to upstream, worth sending with
  [`josev-iso20-charge-loop-timeout`](josev-iso20-charge-loop-timeout.md) rather than as a report of its
  own, and it is unaffected by where the 532 ms turned out to live.
- **Upstream has a charge-loop delay hook and this fork does not.** `charge_loop_delay()` awaited as
  `asyncio.sleep(delay)` in upstream's `-20` AC and DC loops, its `-2` loop and DIN, logging *"Next
  ChargeLoop Req in N seconds"*; `grep charge_loop_delay` matches nothing in `26f7988`. It resolved to
  **0** in our measurement and caused nothing — named here only so that nobody reading §4's *"it is not
  a deliberate pacing"* has to re-derive that the sentence is about this tree.
- **A small crash in their SDP refusal path**, met while setting the comparison up and deliberately not
  filed here: an EVCC that refuses an SDP response naming port 15118 then raises `AttributeError:
  'SDPResponse' object has no attribute 'ip_address'` inside the `__repr__` of the message it is
  refusing. Different project, different report, and it has nothing to do with pacing.
- **Where the 480 ms actually is.** Not in the codec, not in the Josev EVCC — and no further, for the
  reason §4 ends with.

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
- [x] **Localize the 0,5 s — done 2026-08-15, and it moved the report to a different project.** Not the
      codec: their own three log lines split each exchange into decode ≈30 ms, handling ≈1 ms, encode
      ≈30 ms, across four sessions and both roles. Not the EVCC: Josev's own container turns the same
      charge loop around in **43 ms** in the identical scenario, twelve times faster than through this
      module. The remainder is not localized further, and §4 ends with the reason — their module clears
      the root logger and never sets a level, so the split cannot be taken inside it without a patch
      that changes what is measured.
      [`…-josev-evcc-pacing-localized`](../interop-runs/2026-08-15-josev-evcc-pacing-localized/notes.md).
- [x] **Re-run over TLS — done 2026-08-14, and it is worse.** Mutual TLS 1.3, same EV, same config, same
      20 s window: **23,400 s for 43 charge loops, ≈544 ms each**, against ≈532 ms over plain TCP. So the
      number a real `-20` deployment sees is the larger one — TLS costs about 12 ms per exchange here —
      and the deviation is 2,2× the 0,25 s rather than 2,1×. Quote whichever you like; they are the same
      finding ([`…-d20-ac-reverse-tls`](../interop-runs/2026-08-14-everest-d20-ac-reverse-tls/notes.md)).
- [x] **Decide fork or upstream first — answered by the measurement, not by a preference.** It is
      neither: it is the **module**. `PyEvJosev` is where the 532 ms appears and the only place it
      appears, so this is one EVerest issue. The single sentence that is genuinely upstream's — the
      absent `V2G_EVCC_SEQUENCE_PERFORMANCE_TIME` — is in *Not part of this* and travels with the SECC
      report instead.
- [ ] **Consider sending it with the SECC half.** §7 is the argument; two issues, close together, not one
      issue with two headings — they have different fixes in different files.
- [ ] **Post under your own name, in your own words.**
