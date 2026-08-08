# Draft report to SwitchEV (iso15118) — a paused ISO 15118-20 session can never be rejoined

Status: **draft, not sent.** Observed 2026-07-22 against **SwitchEV/iso15118 @ `d645255`** ("Pydantic
upgrade to v2", #455), SECC in Docker host-mode, plain TCP; source re-read against that commit on
2026-08-08. Post under your own name; see *Before sending* at the bottom.

One issue. It is small, it has a six-line fix, and the machinery it needs already exists and already
runs — it is simply never used by the `-20` states.

## First, what works

Josev is the counterparty this project has the most history with, and the only one whose EXI comes from
a codec sharing no lineage with our corpus generator — which is what makes a byte agreement here worth
something. Across `-2` and `-20`, both directions, TCP and TLS, EIM and Plug & Charge, our runs have
gone end to end repeatedly. **Including pause/resume, in ISO 15118-2, which works exactly as the
standard describes it.** That is the reason this report can be short: the feature is there, the
plumbing is there, and one protocol was left out of it.

## What happens

Same station, same client, same rig; the only variable is the protocol version.

**ISO 15118-2** — pause, disconnect, rediscover, reconnect with the old session ID:

```
session 1   session setup: OK_NewSessionEstablished.  Paused session id: 4B22A135BCDF7406
Josev       Preserved session state: EVSessionContext15118(session_id='4B22A135BCDF7406',
            auth_options=[EIM], charge_service=…)
session 2   session setup: OK_OldSessionJoined.       ✓ complete
```

**ISO 15118-20** — the identical sequence:

```
session 1   Paused session id: 4BED567663CF0B84      ✓ complete
Josev       Preserved session state: EVSessionContext15118(session_id=None, …)      <- empty
Josev       EVCC's session ID 4BED567663CF0B84 does not match . New session ID 481E6E7B… assigned
session 2   session setup: OK_NewSessionEstablished  ✓ complete
```

Both sessions complete cleanly — nothing crashes, and an EV that does not care simply charges again.
But `OK_OldSessionJoined` is unreachable in `-20`, so a paused session can never be resumed, and any
state the EV expected to survive the pause is gone.

Note the second line of the `-20` block, because it is the whole diagnosis: **your own handler
preserved a context across the two connections.** It was empty.

## Where it comes from

`ev_session_context` appears **17 times** in `secc/states/iso15118_2_states.py` and **0 times** in
`secc/states/iso15118_20_states.py`.

The `-2` state fills it and then reads it back (`iso15118_2_states.py:188-194`):

```python
self.comm_session.ev_session_context.session_id = session_id
…
elif (
    self.comm_session.ev_session_context.session_id
    and msg.header.session_id == self.comm_session.ev_session_context.session_id
):
    # The EV wants to resume the previously paused charging session
    session_id = self.comm_session.ev_session_context.session_id
```

The `-20` state compares against something else (`iso15118_20_states.py:155-158`):

```python
elif session_setup_req.header.session_id == self.comm_session.session_id:
    # The EV wants to resume the previously paused charging session
    session_id = self.comm_session.session_id
```

`self.comm_session` is the **live** `SECCCommunicationSession`, created for the connection that just
opened. Its `session_id` is not the paused one, so the comparison cannot succeed on a resume, and the
`else` branch — *"False session ID from EV, gracefully assigning new session ID"* — takes every case.
The `-20` state also never writes the context, so even a corrected comparison would have nothing to
match against.

Everything else is already in place and protocol-agnostic: `comm_session_handler.py:291-298` carries
`ev_session_context` out of a finishing session, `:82` and `:98-99` accept it into the next one, and
`:369` is the log line quoted above. That machinery ran for the `-20` session in the capture. Only the
state never filled it.

## Suggested fix

Mirror the `-2` branch — write the context on a new session, compare against it on a resume:

```python
if session_setup_req.header.session_id == bytes(1).hex():
    self.response_code = ResponseCode.OK_NEW_SESSION_ESTABLISHED
    self.comm_session.ev_session_context = EVSessionContext15118()
    self.comm_session.ev_session_context.session_id = session_id
elif (
    self.comm_session.ev_session_context.session_id
    and session_setup_req.header.session_id
        == self.comm_session.ev_session_context.session_id
):
    session_id = self.comm_session.ev_session_context.session_id
    self.response_code = ResponseCode.OK_OLD_SESSION_JOINED
else:
    …                       # unchanged
    self.comm_session.ev_session_context = EVSessionContext15118()
    self.comm_session.ev_session_context.session_id = session_id
```

Offered rather than asserted: `EVSessionContext15118` carries `-2` fields (`auth_options`,
`charge_service`, `sa_schedule_tuple_id`) that have `-20` counterparts with different shapes, so how
much of the *rest* of the context `-20` should preserve is a design question we cannot answer from
outside. Rejoining the session ID is the part the standard requires and the part that is missing.

## Also affects EVerest's fork

The vendored copy in `everest-core` (`_deps/josev-src` @ `26f7988`, 2026-05-04) has the same shape —
`ev_session_context` 19 times in the `-2` states, 0 in the `-20` ones, and the same
`comm_session.session_id` comparison. Anyone running `Evse15118D20` or `PyEvJosev` inherits it, so a
fix here is worth carrying downstream.

---

## Before sending

- [x] **Lead with what works.** Pause/resume is implemented and correct in `-2`; this is one protocol
      left out of a working feature, not a missing feature.
- [x] **Show it rather than describe it.** Both sides' logs from the same rig, with the protocol as the
      only variable — the `-2` run is the control.
- [x] **Quote their own log line as the diagnosis.** "Preserved session state: …(session_id=None…)"
      proves the preservation path ran and the state never filled it, which is a stronger claim than
      pointing at the comparison alone.
- [x] **Re-read the source against the pinned commit.** `d645255`, on 2026-08-08: 17 vs 0 occurrences,
      and the two branches at `iso15118_2_states.py:188-194` and `iso15118_20_states.py:155-158`.
- [x] **Check the downstream fork**, so the issue can say who else is affected.
- [x] **Keep the fix offered, not asserted** — how much of the context `-20` should carry beyond the
      session ID is theirs to decide.
- [ ] **Re-check against current `master` before posting.** The observation is from 2026-07-22 and the
      source read from `d645255`; confirm the branches still look like this, and say which commit you
      checked.
- [ ] **Decide whether to mention the renegotiation observation** from the same counterparty — their
      EVCC sends a real `SessionStopReq(ServiceRenegotiation)` [V2G20-1477] and then drops the link
      anyway. It is a separate filing at best and unproven at present; leaving it out keeps this one
      clean.
- [ ] **Post under your own name, in your own words.**
