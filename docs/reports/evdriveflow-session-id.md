# Draft report to EDF Lab (eVDriveFlow) — the SECC never compares the SessionID it was sent

Status: **draft, not sent**, and **measured on your station on 2026-08-15**. It was a source finding
until then; it is now three arms against a running `start_evse.py`, and your station served a complete
DC sequence — `PowerDelivery` included — under a SessionID it never issued. Post it under your own
name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-15-edf-session-id-460`](../interop-runs/2026-08-15-edf-session-id-460/notes.md) (your
station), and [`2026-08-11-iso20-session-id-probe`](../interop-runs/2026-08-11-iso20-session-id-probe/notes.md)
(the same probe against EVerest, which refuses it).

Four other reports for the same project are in
[`evdriveflow-headless-session.md`](evdriveflow-headless-session.md),
[`evdriveflow-authorization-setup.md`](evdriveflow-authorization-setup.md) and
[`evdriveflow-service-discovery-filter.md`](evdriveflow-service-discovery-filter.md). **File this one
separately** — it is a different rule and a different file.

---

**Title:** `secc/states/*`: the SessionID of an incoming request is never read, so `[V2G20-460]` is
unimplemented and any request is served as the session owner's

**Version:** `eVDriveFlow` `60249c3` (2023-04-17), still `origin/main` on 2026-08-11, and the code the
2026-08-15 measurement below was taken against.

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

Re-run inside the container that answered the measurement below, so this is the code that was running
rather than a clone of it: `payload.header` matches nothing under `secc/`; `MessageHeaderType(` matches
fourteen times, thirteen of them the line above, and only `process_session_setup_request.py` uses an id
it has just minted.

So a request carrying a foreign SessionID, or eight zero bytes, or anything at all, is processed as if
it belonged to the session, and the response echoes your own id back as though it had matched.

## What your station does with a foreign SessionID

Three arms against `start_evse.py` on 2026-08-15, differing in one variable — the SessionID our car
puts in every request after `SessionSetup`:

| arm | our EV sends | your station |
|---|---|---|
| **control** | the id you issued | 12 responses, all `OK`, to the charge loop |
| **zero** | eight zero bytes | **12 responses, all `OK`**, to the charge loop |
| **foreign** | `DEADBEEFDEADBEEF` | **12 responses, all `OK`**, to the charge loop |

The three sessions are the same message for message — same names, same response codes, same lengths —
and differ only in your own session id and its timestamps.

**Ten of your thirteen applicable handlers answered a request whose SessionID you never issued**:
`AuthorizationSetup`, `Authorization`, `ServiceDiscovery`, `ServiceDetail`, `ServiceSelection`,
`DC_ChargeParameterDiscovery`, `ScheduleExchange`, `DC_CableCheck`, `DC_PreCharge` and
**`PowerDelivery`**. (Fifteen `process_*_request.py` less the two the rule excludes; the eleventh,
`DC_ChargeLoopReq`, was received and hit an unrelated `None` dereference.)

`PowerDelivery` is the one worth pausing on. `ChargeProgress = Start` is the request that closes the
contactor, and it was answered `OK` to a peer whose session id was invented — so this is not an
opening-handshake curiosity that a later check would catch. There is no later check.

**Your own log has both halves of it**, the id received and the id answered with, three lines apart:

```
XML message received: …<s2:Header><s2:SessionID>DEADBEEFDEADBEEF</s2:SessionID>…</s1:PowerDeliveryReq>
Received PowerDeliveryReq.
XML message to be sent:  …<ns1:SessionID>3432363539393930</ns1:SessionID>…
                            <ns1:ResponseCode>OK</ns1:ResponseCode>
```

### And what a conformant station does with the same probe

The same knob was pointed at EVerest's `Evse15118D20` on 2026-08-11, before this was written:

| arm | our EV sends | their station |
|---|---|---|
| **control** | the id it was issued | full DC session, charge loop, welding detection, `SessionStopRes` |
| **zero** | eight zero bytes, from `AuthorizationSetupReq` on | **`FAILED_UnknownSession`**, session over |

That is the answer `[V2G20-460]` asks for, from an independent implementation, on the wire — and it is
also what rules out our probe being at fault, since the identical bytes are refused there.

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

**And ours was the same as yours until the morning this was written.** Our ISO 15118-2 station had no
`[V2G2-460]` check at all — `FAILED_UnknownSession` appeared nowhere in our live code — and our `-20`
station had no table of corresponding responses to answer *with*: its sequence guard threw and closed
the socket. Both halves were fixed on 2026-08-11, and fixing them turned 32 of our own passing tests
red, because every `-20` fixture had been sending the all-zero SessionID for its whole existence
against a station with no check to fail. We are not reporting from higher ground.

---

## Before sending

- [x] **Run it against your station.** Done 2026-08-15, three arms, and it went further than this item
      expected: not `AuthorizationSetupRes(OK)` and then a stall, but ten handlers answered `OK`
      including `PowerDelivery`.
      [`2026-08-15-edf-session-id-460`](../interop-runs/2026-08-15-edf-session-id-460/notes.md).
      <br>Two caveats that belong with it. **Getting past your fifth message needed the
      `SupportedServiceIDs` filter** — without it the session ends at `ServiceDiscoveryReq`, which is
      [a separate finding of yours](evdriveflow-service-discovery-filter.md) — so an unfiltered probe
      measures three handlers rather than ten. And **the control was re-run last as well as first**:
      your station's first session after a start answers `DC_CableCheckRes(FAILED)` while its virtual
      isolation test warms up, which on its own reads as *"the wrong id got further than the right
      one"*.
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
