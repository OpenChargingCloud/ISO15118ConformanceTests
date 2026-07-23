# Interop run — **Pause/Resume** (-2 and -20), live forward vs a Josev SECC

- **Date:** 2026-07-22
- **Our side:** `evcc --connect … --pause` (session 1, ends with `ChargingSession.Pause`), then a fresh SDP
  discovery and `evcc --connect … --resume <hex-session-id>` (session 2, rejoins). Josev tears down its TCP
  server on pause and resumes its SDP responder — the resumed session lands on a **new dynamic port**, so
  re-discovery is part of the flow.
- **Josev:** SECC, docker host-mode, plain TCP.

## ISO 15118-**2** — full pause/resume ✅

```
session 1: … session setup: OK_NewSessionEstablished.  Paused session id: 4B22A135BCDF7406
Josev:     Preserved session state: EVSessionContext15118(session_id='4B22A135BCDF7406',
           auth_options=[EIM], charge_service=…)
session 2: … session setup: OK_OldSessionJoined.  ✓ Session complete in 1350 ms.
```

The spec mechanic works end to end ([V2G2-740]): pause → context preserved across connections → the
resumed `SessionSetupReq` carries the old id → **`OK_OldSessionJoined`** → full replay to `SessionStop`
(Terminate).

## ISO 15118-**20** — pause works, rejoin is a Josev gap ⚠️

```
session 1: … Paused session id: 4BED567663CF0B84   ✓ complete
Josev:     Preserved session state: EVSessionContext15118(session_id=None, …)   ← empty!
Josev:     EVCC's session ID 4BED567663CF0B84 does not match . New session ID 481E6E7B… assigned
session 2: … session setup: OK_NewSessionEstablished.  ✓ complete
```

Josev's **-20 states never populate `ev_session_context`** (its `-2` states do), and its -20 `SessionSetup`
compares the incoming id against the *fresh comm session's empty* `session_id` instead of the preserved
context — so a -20 resume always degrades to the graceful "false session ID → new session assigned" path.
Both sessions still complete cleanly; the old-session **rejoin** for -20 is Josev's gap, not ours — our own
-20 resume answers `OK_OldSessionJoined` (loopback E2E below).

## What was added on our side

- `Secc2`/`Secc20Base`: `SessionStopReq(Pause)` marks the session `Paused` (id retained); a follow-up
  instance constructed with `ResumeSessionId` answers a matching `SessionSetupReq` with
  **`OK_OldSessionJoined`** (else a fresh session). CLI SECC keeps accepting connections while paused and
  carries the id over.
- `Evcc2`/`Evcc20Base`: `StopMode` (Terminate/Pause) + `ResumeSessionId` (the opening SessionSetupReq
  carries the old id) + `SessionSetupCode`/`SessionId` surfaced.
- CLI: `evcc --pause-resume` (both halves in one process, incl. re-discovery) and
  `evcc --pause` / `evcc --resume <hex>` (the halves as separate invocations, for scripts that must
  re-discover SDP between them — at the time of this run via an in-script python probe, since the
  CLI's own EVCC-side SDP client timed out live. *That gap is fixed since 2026-07-23* (multicast
  loopback, see the roadmap's resolved list) — the script now runs `evcc --sdp` per session natively.

CI: `Iso2LoopbackTests.AcSession_PauseThenResume_RejoinsOldSession` and
`Iso20LoopbackTests.DcSession_PauseThenResume_RejoinsOldSession` — two real TCP connections each, asserting
`Paused`, id retention, and `OK_OldSessionJoined` on the resumed setup.
Script: [`live-evcc-pause-resume.sh`](../../../tools/interop-josev/live-evcc-pause-resume.sh) (`2|20`).
Logs: `{josev-secc,our-evcc}-iso2-pause.log`, `{josev-secc,our-evcc}-iso20-pause.log`.
