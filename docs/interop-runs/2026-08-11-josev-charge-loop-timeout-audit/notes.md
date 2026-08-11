# 2026-08-11 — the same charge-loop timeout, in a second implementation. **Source only.**

Hours after measuring EVerest's `-20` station holding a silent charge loop for
[60,00 s where Tables 216/217 give 0,5 s](../2026-08-11-everest-d20-sequence-timeout/notes.md), the
obvious question was whether that is one project's oversight or the shape of the mistake. Josev is the
other `-20` implementation this suite meets, and it is Python, so the answer cost a read rather than a
rig.

| | |
|---|---|
| Counterparty | [SwitchEV/iso15118](https://github.com/SwitchEV/iso15118) upstream `master` **`d645255c`** (2026-05-19), cross-read against the fork EVerest fetches, `26f79889` |
| Method | source only — **their SECC was not brought up**, and the filing's first checklist item says so |
| Outcome | `V2G_SECC_SEQUENCE_TIMEOUT_{AC,DC,WPT}_CL = 0.5` are defined and **referenced nowhere**; both charge-loop states hand on the 60 s baseline. Filed: [`josev-iso20-charge-loop-timeout.md`](../../reports/josev-iso20-charge-loop-timeout.md) |

## What is actually wrong

`iso15118/shared/messages/iso15118_20/timeouts.py` transcribes Tables 216 and 217 correctly, including
the 0,5 s charge-loop overrides. Those three constants have **zero references** outside that file, on
both revisions. `ACChargeLoop` and `DCChargeLoop` pass `Timeouts.V2G_SECC_SEQUENCE_TIMEOUT` — 60 s —
as the `next_msg_timeout` for the following request, which `rcv_loop` then uses to arm the socket read.

So: right value written down, wrong value wired, in one line per energy transfer mode. Two lines to fix,
and the constants already exist to reference.

## Two wrong readings before the right one, which is the part worth keeping

This nearly became a filing about something else. Twice.

**First wrong reading — "nothing reads the timeout at all."** `grep -rn '\.timeout\b'` found only two
assignments in `shared/states.py` and no consumer, which looked like a SECC with no sequence timeout
whatsoever. The pattern was too narrow: the attribute that governs the wait is **`next_msg_timeout`**,
which contains `timeout` but not `.timeout`, so the grep could not match it. A regex that cannot match
the thing you are looking for returns the same empty result as an absence.

**Second wrong reading — "every `-20` state arms the EV's 20 s setup timer."** All 17 `-20` SECC states
do pass `Timeouts.V2G_EVCC_COMMUNICATION_SETUP_TIMEOUT` to `State.__init__`, where `-2` and DIN pass the
sequence timeout — a real inconsistency, and a tempting headline. But `State.__init__` only stores that
value and logs `"Waiting for up to {timeout} s"`; the socket read is armed from `next_msg_timeout`
instead. The inconsistency is a misleading **log line**, not a wrong wait, and it is in the filing as
exactly that.

The third reading followed the value from the constant to `create_next_message` to `rcv_loop`'s
`asyncio.wait_for`, and only then was there something to write down. **Follow the value to the syscall,
not to the name that sounds like it.**

## Why it was filed without a run

The rule here is to observe rather than only read, and this breaks it knowingly:

- The **same** behaviour was measured on the wire the same day against EVerest's implementation, so the
  claim being made about the standard and about the consequence is not resting on a reading.
- What is unmeasured is *this* code path, and the filing's first checklist item is the run, with the
  command that produces it.
- Bringing Josev's SECC up needs docker, redis and a prepared clone — a rig session of its own rather
  than a step in this one.

Recorded this way rather than quietly, because a filing that says "not observed" up front is worth more
than one that leaves the reader to work out that nobody ran it.

## What this does not decide

- **Whether their EVCC has the same shape.** It arms `Timeouts.SUPPORTED_APP_PROTOCOL_REQ` at session
  start and re-arms per message from the same `next_msg_timeout` mechanism; whether every EVCC state
  passes the right per-message value was not audited.
- **DIN and `-2`.** Both use `V2G_SECC_SEQUENCE_TIMEOUT` throughout, which is right for them — `-2` has
  no charge-loop override in Table 205's shape that this audit checked for.
- **Anything about behaviour under load**, which is where a 60 s hold actually costs something.

## Reproduce the reading

```bash
git clone --depth=1 https://github.com/SwitchEV/iso15118.git
cd iso15118
grep -rn "V2G_SECC_SEQUENCE_TIMEOUT_.*_CL" iso15118/          # defined in timeouts.py, nowhere else
grep -n -A4 "create_next_message" iso15118/secc/states/iso15118_20_states.py | grep Timeouts
```
