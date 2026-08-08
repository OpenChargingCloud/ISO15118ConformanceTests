# 2026-08-08 (re-run) — a paused ISO 15118-20 session, resumed end to end

The [first run](../2026-08-08-everest-pause-resume-tls/notes.md) five hours earlier found the defect;
this one is the same rig with the fix in. **Both halves complete.** As far as this project's records go
it is the first live `-20` pause/resume that finishes.

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) `everest-core` @ **`b61bb12`**, native build |
| Their config | `config-d20-tls-ours.yaml` — `ENFORCE_TLS`, `enforce_tls_1_3: true` |
| Ours | conformance suite @ `e203ccf`, app @ `36c8180` (the resume fix, app #12) |
| Direction | `EV→` — our EVCC against their station, -20 DC, EIM, mutual TLS |
| Outcome | s1 **59 exchanges**, paused `62A98E1503949A9F`. s2 **`OK_OldSessionJoined`, 52 exchanges, complete** |

```
s1   New session created with session_id: 0x62, 0xA9, 0x8E, 0x15, 0x03, 0x94, 0x9A, 0x9F
     59 exchanges … Paused session id: 62A98E1503949A9F      ✓ complete in 6071 ms
s2   Old session resumed with session_id: 0x62, 0xA9, 0x8E, 0x15, 0x03, 0x94, 0x9A, 0x9F
     52 exchanges, session setup: OK_OldSessionJoined        ✓ complete in 5364 ms
```

## The evidence is theirs, not ours

Our own logs would only say what our EVCC believes. **Their station's message log is the independent
record**, and it shows the two halves side by side:

```
s1   SessionSetupReq → AuthorizationSetupReq → AuthorizationReq → ServiceDiscoveryReq
                     → ServiceDetailReq → ServiceSelectionReq → DcChargeParameterDiscoveryReq
                     → ScheduleExchangeReq → DcCableCheckReq ×43 → … → SessionStopReq

s2   SessionSetupReq → DcChargeParameterDiscoveryReq
                     → ScheduleExchangeReq → DcCableCheckReq ×41 → … → SessionStopReq
```

Five messages skipped, exactly the ones a resumed `-20` session must not repeat — authorization setup,
authorization, and all three of service discovery, detail and selection. The station accepted the jump
because it is the station's own rule; five hours earlier it had answered the third message of that list
with `FAILED_SequenceError`.

**Accounting for the 59 → 52 difference honestly:** five of the seven are the skipped opening messages.
The other two are cable-check polls, 43 against 41, which is the SIL's timing and means nothing.

## What this does and does not establish

- **Settled:** our EVCC resumes correctly against an independent implementation of the rule, and does so
  over mutual TLS with a credential minted by that implementation's own PKI script. The sequence half of
  the fix is confirmed by the counterparty rather than by our own SECC, which was the whole problem the
  first time — both sides of our loopback shared the bug and agreed with each other.
- **Not settled: the binding.** Our SECC now computes `SHA-512(SessionID ‖ SHA-512(vehicle leaf))`, the
  same construction EVerest uses, and this run does **not** compare them. In this direction only their
  SECC's value is consulted; ours is an EVCC, and the binding it computes is over *their* station
  certificate, which they never compute. A cross-check needs the reverse direction — their EVCC against
  our SECC — and their EVCC is Josev-derived, whose `-20` resume cannot reach `OK_OldSessionJoined` at
  all ([our filing](../../reports/josev-iso20-pause-resume.md)). So that comparison is blocked on somebody
  else's fix, and saying otherwise would be claiming an agreement nobody measured.
- Our EVCC's own same-station check did not run either: the two halves are separate CLI invocations, so
  no binding is carried between them and `ResumedStationVerified` stays null. Deliberate, and documented
  at `Evcc20Base.ResumeBinding` — the car proceeds where it cannot check, the station does not.

## Rig

Unchanged from the first run, including both traps that cost an hour there: SDP is answered only while
no session is running, and both halves must share one station process because `pause_ctx` lives in it.
Their PKI was backed up before installing the minted `iso-20` tree and restored afterwards; the station
was stopped.

One new trap, worth writing down because it produced a *confident wrong answer*: invoked as
`wsl -- bash -lc …` from this harness, **`$HOME` arrives empty** while `~` still expands. A status check
written with `$HOME` reported the minted PKI as absent — it was there all along, and the paths had
silently become absolute. Every script here now sets `HOME` explicitly. Read the value back before
trusting a negative result from a remote shell.

```bash
bash install-pki.sh            # back up theirs, install the minted iso-20 tree, build vehicle.p12
bash run-pause-resume-tls.sh   # restart the station, then both halves against it
```

Artifacts: `our-evcc.s1.log`, `our-evcc.s2.log`, `their-charger.log`.

## Next

- **The binding cross-check** stays open, blocked on Josev's `-20` resume. It is the last unverified part
  of the fix.
- The `-2` `EAmount`-on-resume gap is unrelated and still listed in [`open-work.md`](../../open-work.md).
