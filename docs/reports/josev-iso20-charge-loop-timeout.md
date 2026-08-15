# Draft report to SwitchEV (Josev) — the charge-loop sequence timeouts are defined and never used

Status: **draft, not sent.** Read on **upstream `master` `d645255c`** (2026-05-19, "Pydantic upgrade to
v2"), not only on the copy EVerest fetches — and **measured on the wire against that SECC on
2026-08-15**, AC and DC, each with a control. Post it under your own name; see *Before sending* at the
bottom.

Evidence in this repository:
[`2026-08-11-josev-charge-loop-timeout-audit`](../interop-runs/2026-08-11-josev-charge-loop-timeout-audit/notes.md)
(the source audit) and
[`2026-08-15-josev-charge-loop-timeout`](../interop-runs/2026-08-15-josev-charge-loop-timeout/notes.md)
(four arms, and your own log naming the value).

---

**Title:** `V2G_SECC_SEQUENCE_TIMEOUT_AC_CL` / `_DC_CL` (0,5 s) are defined and referenced nowhere, so
the `-20` SECC waits 60 s after a charge-loop response

**Version:** `SwitchEV/iso15118` upstream `master` **`d645255c`**, and the fork EVerest fetches,
`26f79889` — the code below is identical in both.

## The defect

`iso15118/shared/messages/iso15118_20/timeouts.py` transcribes the per-message overrides of Tables 216
and 217 correctly:

```python
V2G_SECC_SEQUENCE_TIMEOUT = 60
...
AC_CHARGE_LOOP_REQ = 0.5
V2G_SECC_SEQUENCE_TIMEOUT_AC_CL = 0.5  # CL = Charge Loop
...
DC_CHARGE_LOOP_REQ = 0.5
V2G_SECC_SEQUENCE_TIMEOUT_DC_CL = 0.5  # CL = Charge Loop
...
V2G_SECC_SEQUENCE_TIMEOUT_WPT_CL = 0.5  # CL = Charge Loop
```

**All three `_CL` constants have zero references outside that file** — checked across the whole tree on
both revisions. What the charge-loop states actually hand on is the 60 s baseline:

```python
# iso15118/secc/states/iso15118_20_states.py — ACChargeLoop, and DCChargeLoop identically
self.create_next_message(
    None,
    ...,
    Timeouts.V2G_SECC_SEQUENCE_TIMEOUT,     # <- 60, where the table says 0,5
    Namespace.ISO_V20_AC,
)
```

`create_next_message`'s third positional argument is `next_msg_timeout`, *"The amount of seconds to
wait for the subsequent message from the counterpart"* (`shared/states.py`), and `rcv_loop` re-arms
the socket read from it each iteration:

```python
# iso15118/shared/comm_session.py
message = await asyncio.wait_for(self.reader.read(7000), timeout)
...
timeout = self.current_state.next_msg_timeout
```

So the plumbing is right and the value is wrong — in exactly one place per energy transfer mode. All
16 `create_next_message` calls in the `-20` SECC pass `V2G_SECC_SEQUENCE_TIMEOUT`, which is correct for
14 of them and wrong for the two charge loops.

## On the wire, both energy modes

Your SECC in host mode, plain TCP, our EV as the peer. A control arm charges normally to `SessionStop`;
the silent arm stops sending after a charge-loop response and holds the connection open.

```
DC   13:20:16,012  Sent DC_ChargeLoopRes
     13:21:16,073  Reason: TimeoutError occurred. Waited for 60.0 s after sending last message
                   Session ended in DCChargeLoop

AC   13:23:59,758  Sent AC_ChargeLoopRes
     13:24:59,818  Reason: TimeoutError occurred. Waited for 60.0 s after sending last message
                   Session ended in ACChargeLoop
```

**60,061 s** and **60,060 s**, against **0,5 s** in Tables 217 and 216. We would lead an issue with your
own line rather than with ours: *"Waited for 60.0 s"*, printed from `DCChargeLoop` and `ACChargeLoop` —
the two states whose constants are transcribed in `timeouts.py` and referenced nowhere.

Both modes were run because the fix is two lines, one per state, and we did not want one of them to rest
on the other.

## Why it matters, and what the standard says

`[V2G20-441]` sets `V2G_SECC_Sequence_Timeout` from Table 215 — 60 s for *all other messages*.
**`[V2G20-1500]`** and **`[V2G20-1502]`** oblige the SECC to implement the AC and DC tables, and those
override it after `AC_ChargeLoopRes` and `DC_ChargeLoopRes` with **0,5 s**. That is the phase in which
the contactor is closed and current is flowing, which is why the standard shortens the timer there:
a station whose EV has gone silent should notice in half a second, not in a minute.

## Suggested fix

The constants are already there, so it is a reference rather than a change of behaviour to decide on:

```python
    Timeouts.V2G_SECC_SEQUENCE_TIMEOUT_AC_CL,   # in ACChargeLoop
    Timeouts.V2G_SECC_SEQUENCE_TIMEOUT_DC_CL,   # in DCChargeLoop
```

`_WPT_CL` has no state to attach to yet and can wait for one.

## Smaller, and in the same file: the `-20` states log a timeout they do not use

Every one of the 17 `-20` SECC states is constructed as

```python
super().__init__(comm_session, Timeouts.V2G_EVCC_COMMUNICATION_SETUP_TIMEOUT)
```

while the `-2` states use `Timeouts.V2G_SECC_SEQUENCE_TIMEOUT` for all but the opening one (34 uses
against 1), and DIN does the same. The constructor argument does not govern the wait — `State.__init__`
only stores it and logs `"Waiting for up to {timeout} s"` — so the effect is a log line that says 20 s
in every `-20` state while the socket is actually waiting 60. Cosmetic next to the above, and confusing
in exactly the situation where somebody is reading logs to find out why a session hung.

## Context: two independent implementations, the same miss

The same defect was measured on the wire hours earlier in EVerest's `libiso15118`, which has one flat
`TIMEOUT_SEQUENCE = 60 s` and no charge-loop value at all: their station held a silent charge loop for
**60,0025 s** (DC) and **60,0160 s** (AC), timed from their own log
([DC](../interop-runs/2026-08-11-everest-d20-sequence-timeout/notes.md),
[AC](../interop-runs/2026-08-15-everest-d20-sequence-timeout-ac/notes.md)). Two independent `-20`
implementations flatten the same per-message override, which is worth knowing when deciding how
prominent the fix should be — and Josev is the one that already has the right numbers written down.
All four intervals land within 60 ms of each other, from two codebases that share nothing.

**Our own station has the same shape** and is recorded as ours to fix: `Secc20Base` takes a single
sequence timeout for every phase.

---

## Before sending

- [x] **Run it — done 2026-08-15, four arms.** Their SECC in host mode, our EVCC with
      `V2G_INTEROP_SILENT=90`, a control per energy mode: **60,061 s** after `DC_ChargeLoopRes` and
      **60,060 s** after `AC_ChargeLoopRes`, with **their own log naming the value it waited on**.
      [`…-josev-charge-loop-timeout`](../interop-runs/2026-08-15-josev-charge-loop-timeout/notes.md).
      <br>It cost two fixes of ours first, both in the harness and both worth knowing: the first silent
      run produced a **complete, successful session**, because this fixture passed four of the eleven
      parameters its EVerest twin does and `silentInChargeLoop` was not among them — so the car never
      went quiet and the run would have read as *"their station does not time out"*. And the AC arm was
      answered `Failed_NoNegotiation`, because the same fixture dropped the power mode before the SAP
      handshake and always offered the DC namespace.
- [x] **Check the citation against upstream, not the fork.** Fetched `SwitchEV/iso15118` `master`
      `d645255c` explicitly: the three `_CL` constants are present and unreferenced there too, and both
      charge-loop states pass the 60 s baseline. EVerest's fork point `26f79889` is two weeks older and
      identical here.
- [x] **Find the value that actually governs the wait.** It is `next_msg_timeout`, set through
      `create_next_message` and re-read by `rcv_loop` — *not* the `State.__init__` argument. Two
      earlier readings of this went wrong in opposite directions before that was pinned down, and the
      report is written from the third.
- [x] **Say where they are right.** The plumbing is correct, the `-2` and DIN SECCs use the right
      constant, and the charge-loop values are already transcribed from the tables. This is a
      reference that was never wired, not a design that misunderstands the timer.
- [x] **Admit our own gap.** `Secc20Base` is flat too.
- [ ] **Decide issue or PR.** Two lines plus the log-line tidy-up; a PR may be quicker than a
      discussion.
- [ ] **Post under your own name, in your own words.**
