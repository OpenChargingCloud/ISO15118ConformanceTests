# Draft report to EDF Lab (eVDriveFlow) — the SECC never compares the SessionID it was sent

Status: **draft, not sent**, and **not run against your station** — the first checklist item says so.
The probe it needs exists and is shown below working against another `-20` station, which refuses
exactly what yours would serve. Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-11-iso20-session-id-probe`](../interop-runs/2026-08-11-iso20-session-id-probe/notes.md).

Four other reports for the same project are in
[`evdriveflow-headless-session.md`](evdriveflow-headless-session.md),
[`evdriveflow-authorization-setup.md`](evdriveflow-authorization-setup.md) and
[`evdriveflow-service-discovery-filter.md`](evdriveflow-service-discovery-filter.md). **File this one
separately** — it is a different rule and a different file.

---

**Title:** `secc/states/*`: the SessionID of an incoming request is never read, so `[V2G20-460]` is
unimplemented and any request is served as the session owner's

**Version:** `eVDriveFlow` `60249c3` (2023-04-17), still `origin/main` on 2026-08-11.

## The defect

`[V2G20-460]` is one sentence and admits no exception: the response shall carry
`FAILED_UnknownSession` if the SessionID in **any** request message except `SessionSetupReq` is not
equal to the SessionID stored for the currently active session.

Your fifteen `secc/states/process_*_request.py` handlers each build their response header the same way:

```python
response.header = MessageHeaderType(self.session_parameters.session_id, int(time.time()))
```

That writes *your* session id into the response. **Not one of them reads `payload.header.session_id`.**
A grep across the tree finds the incoming header nowhere in `secc/`, and `FAILED_UnknownSession` appears
only in the xsdata-generated bindings and in ISO's schema — never in a handler.

So a request carrying a foreign SessionID, or eight zero bytes, or anything at all, is processed as if
it belonged to the session, and the response echoes your own id back as though it had matched.

## The probe, and what a conformant station does with it

Nothing in our suite could send a wrong SessionID until 2026-08-11, which is why nobody had exercised
this rule against anyone. Our `-20` EV can now do it, and the same probe was run against EVerest's
`Evse15118D20` as a reference:

| arm | our EV sends | their station |
|---|---|---|
| **control** | the id it was issued | full DC session, charge loop, welding detection, `SessionStopRes` |
| **zero** | eight zero bytes, from `AuthorizationSetupReq` on | **`FAILED_UnknownSession`**, session over |

That is the answer `[V2G20-460]` asks for, from an independent implementation, on the wire. Your
station would answer `AuthorizationSetupRes(OK)` and continue.

## Suggested fix

The value is already in hand — `self.session_parameters.session_id` — so the check is a guard in the
dispatch rather than a change to fifteen handlers. Somewhere before `process_payload`, for every
message that is not a `SessionSetupReq`:

```python
if payload.header.session_id != self.session.session_parameters.session_id:
    # answer with the matching *Res carrying FAILED_UnknownSession, then stop the session
```

Whether the refusal is built centrally or per state is a question about your `ReactionToIncomingMessage`
shape rather than about the rule, and it is yours to answer.

## Context: three other stacks, three other answers

Worth knowing before deciding how urgent this is, because it says the requirement is neither obscure
nor uniformly handled:

| stack | `[V2G20-460]` / `[V2G2-460]` |
|---|---|
| Josev (SwitchEV), all three protocols | correct — one guard in `secc_state.py`, no exemptions |
| EVerest `-20` (`libiso15118`) | correct — 15 of 17 states check, and the 2 that do not are the 2 the rule excludes |
| EVerest `-2` (`EvseV2G`) | checks, but exempts SessionID = 0 — [filed separately](everest-evsev2g-session-id-zero.md) |
| **eVDriveFlow `-20`** | **not implemented** |

**And ours was the same as yours until this morning.** Our ISO 15118-2 station had no `[V2G2-460]`
check at all; it now refuses, and the `-20` half of our own fix is still open. We are not reporting
from higher ground.

---

## Before sending

- [ ] **Run it against your station.** This is a source finding. The probe is
      `V2G_INTEROP_SESSIONID=zero` against our `-20` EVCC, and your rig needs docker, a prepared clone
      and the `edfnet` IPv6 network — a session of its own rather than a step in this one. Expect
      `AuthorizationSetupRes(OK)` where EVerest answers `FAILED_UnknownSession`.
- [x] **Show that the probe works and that the rule is real.** Both arms were run against EVerest's
      `-20` station: the control charges, the zero arm is refused. The instrument is not the variable.
- [x] **Check the source at the current head.** `60249c3` is still `origin/main`, unchanged since
      2023-04-17.
- [x] **Say where the requirement is not exotic.** Two of the three other stacks implement it correctly;
      the third is a narrower defect already filed.
- [x] **Admit our own gap.** Our `-2` station had none until the same day, and our `-20` half is still
      open.
- [ ] **Decide issue or PR, and expect a slow response.** Re-checked 2026-08-11: `main` is still
      `60249c3` (2023-04-17), **three years and four months** without a commit. A PR is more likely to be
      useful than an issue. Same caveat as the other four.
- [ ] **Post under your own name, in your own words.**
