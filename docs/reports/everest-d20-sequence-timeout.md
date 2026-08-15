# Draft report to EVerest (`libiso15118`) — one sequence timeout for every message, where the charge loop gets 0,5 s

Status: **draft, not sent.** Measured on the wire against **everest-core 2026.02.1** (`b61bb12b8`),
`Evse15118D20`, ISO 15118-20 over plain TCP — **DC on 2026-08-11 and AC on 2026-08-15**, each with a
control. Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-11-everest-d20-sequence-timeout`](../interop-runs/2026-08-11-everest-d20-sequence-timeout/notes.md)
(DC) and
[`2026-08-15-everest-d20-sequence-timeout-ac`](../interop-runs/2026-08-15-everest-d20-sequence-timeout-ac/notes.md)
(AC) — two arms each, and your own log carrying both timestamps in both.

---

**Title:** `V2G_SECC_Sequence_Timeout` is a single 60 s constant, so a silent EV holds the charge loop
for 60 s where Tables 216/217 give the SECC 0,5 s

**Version:** everest-core **2026.02.1** (`b61bb12b8`), `lib/everest/iso15118`, ISO 15118-20 AC and DC.

## The defect

```cpp
// include/iso15118/d20/timeout.hpp:30
constexpr auto TIMEOUT_SEQUENCE = 1000 * 60;

// src/iso15118/session/iso.cpp — Session::send_response(), the only place SEQUENCE is armed
timeouts.start_timeout(d20::TimeoutType::SEQUENCE, d20::TIMEOUT_SEQUENCE);
```

The arming and disarming are right: `send_response()` starts the timer, and the next request stops it
(`iso.cpp`, `stop_timeout(SEQUENCE)`, skipped only for `SupportedAppProtocolReq`, which correctly has no
sequence timer before it). **What is wrong is that the duration never varies.**

`V2G_SECC_Sequence_Timeout` is not one number. Table 215 gives 60 s for *all other messages* — and the
AC and DC tables override it for the charge loop:

| | table | `V2G_SECC_Sequence_Timeout` after | value |
|---|---|---|---:|
| AC | 216, obliged by **`[V2G20-1500]`** | `AC_ChargeLoopRes` | **0,5 s** |
| DC | 217, obliged by **`[V2G20-1502]`** | `DC_ChargeLoopRes` | **0,5 s** |
| everything else | 215, via **`[V2G20-441]`** | — | 60 s |

`[V2G20-441]` says to set the timeout *to the value defined in Table 215*; `[V2G20-1500]`/`[V2G20-1502]`
say the SECC **shall implement** the AC/DC tables. `[V2G20-443]` is what then has to happen — stop
waiting, stop the session — and your code does that correctly, 120× too late.

The charge loop is the phase where this matters: the contactor is closed and current is flowing. The
standard shortens the timer there for that reason.

## Measured, with a control — on both tables

Two arms against a freshly started `Evse15118D20`, plain TCP. Same negotiation within each pair
(`Authorization: eim`; energy transfer service 2 for DC, 1 for AC). The only difference is what our EV
does after the first charge-loop request.

| arm | our EV | outcome |
|---|---|---|
| **control** | charges normally | full session, `SessionStopReq`/`Res` |
| **silent** | stops sending, **holds the connection open** | your station ended the session ≈60 s later |

From your own log, the two lines that decide it — **DC, 2026-08-11**:

```
03:06:05.849686 [INFO] evse_manager:Ev :: EVSE ISO V2G DcChargeLoopRes
03:07:05.852171 [ERRO] iso15118_charge :: Sequence Timeout 40secs is reached. Stopping the session
```

and **AC, 2026-08-15**:

```
15:01:18.793896 [INFO] evse_manager:Ev :: EVSE ISO V2G AcChargeLoopRes
15:02:18.809863 [ERRO] iso15118_charge :: Sequence Timeout 40secs is reached. Stopping the session
```

| | your last charge-loop response → your own timeout verdict | allowed |
|---|---:|---:|
| DC, Table 217, `[V2G20-1502]` | **60,0025 s** | 0,5 s |
| AC, Table 216, `[V2G20-1500]` | **60,0160 s** | 0,5 s |

The two agree to within 14 ms, which is what one shared constant should look like — and the AC arm was
run precisely so that the AC row is a measurement rather than an inference from the DC one. In the DC
run our EV, measuring the socket rather than the log, saw the connection close 65,04 s after it stopped
sending; the extra ≈5 s is your teardown between the timer firing and the TCP close, and it is worth
knowing that the two numbers measure different things.

The control arm matters in both: it shows the car reaches and completes the loop normally, so the silent
arm is not a car that failed to get there.

## Also, and much smaller: the log line names the wrong number

```
logf_error("Sequence Timeout 40secs is reached. Stopping the session");   // iso.cpp
```

40 s is `V2G_EVCC_Sequence_Performance_Time` from Table 215 — the **EV's** number. The constant is 60 s
and the charge-loop allowance is 0,5 s, so the message names neither. Whoever wrote it was reading the
right table and took the wrong row. Harmless next to the above, and a one-word fix while you are there.
It is the same line on both paths: AC and DC print it identically, which is expected — one call site.

## Suggested direction

`start_timeout` already takes a duration, so nothing structural has to change — the call in
`send_response()` needs the value for the message type it just sent:

```cpp
timeouts.start_timeout(d20::TimeoutType::SEQUENCE, sequence_timeout_for(stored_response_type));
```

with the charge-loop responses mapping to 500 ms and everything else to `TIMEOUT_SEQUENCE`. Whether the
lookup belongs beside the message types or in `timeout.hpp` is yours to pick; the response type is
already in hand at that point.

## Context, and our own side

Found by reading the timing tables after an unrelated probe, and confirmed on the wire the same day. The
sequence machinery around it is correct — the arm/disarm pairing, the `SupportedAppProtocolReq`
exemption, and the session-stop behaviour all do what `[V2G20-441]`…`[V2G20-445]` ask.

**Our own station has the same shape and we are not pretending otherwise:** `Secc20Base` takes one
`sequenceTimeout` for the whole session and applies it in every phase, so it is as wrong as this is,
and by the same construction. We found ours while measuring yours. The difference this report rests on
is the value on the wire, not the design — and ours has never been measured by anybody either.

---

## Before sending

- [x] **Observe it, do not only read it.** Two arms on the wire per protocol, and the decisive interval
      comes from *your* log rather than ours — 60,0025 s after `DcChargeLoopRes`, 60,0160 s after
      `AcChargeLoopRes`.
- [x] **Have a control.** The normal-charge arm proves the car reaches the charge loop and completes it,
      so the silent arm is not a car that never got there. Run for both protocols.
- [x] **Check the tables, not just the constant.** Table 215's 60 s is right for everything outside the
      charge loop; the defect is only visible once Tables 216 and 217 are read, and those carry their own
      requirement ids (`[V2G20-1499]`–`[V2G20-1502]`).
- [x] **Say where they are right.** The arming, the disarming, the `SupportedAppProtocolReq` exemption
      and the session stop are all correct. This is one value, not a broken design.
- [x] **Admit our own gap.** `Secc20Base` has one flat timeout too.
- [x] **Decide whether the AC path deserves its own measurement — decided yes, and run 2026-08-15.**
      The constant is shared, so DC settled AC by construction; by construction is not by measurement,
      and Table 216 carries its own requirement id. **60,0160 s after `AcChargeLoopRes`**, same rig, one
      variable, with its own control:
      [`…-sequence-timeout-ac`](../interop-runs/2026-08-15-everest-d20-sequence-timeout-ac/notes.md).
      Both rows of the table above are now measured rather than one measured and one inferred.
- [x] **Check whether `main` has moved — checked 2026-08-11 against upstream, not against the clone.**
      `EVerest/everest-core` `main` is `ebcd36d` (2026-08-11), well ahead of the `2026.02.1` tag this was
      read from — **and the constant is unchanged there**: `constexpr auto TIMEOUT_SEQUENCE = 1000 * 60;`
      still, one value, one call site. The finding is live on `main`, not only in the release.
- [ ] **Post under your own name, in your own words.**
