# Draft report to EVerest (`EvseV2G`) — a SessionID of zero passes the `[V2G2-460]` check

Status: **draft, not sent.** Measured on the wire 2026-08-11 against **everest-core 2026.02.1**
(`b61bb12b8`), `EvseV2G`, ISO 15118-2 DC over plain TCP; the line is unchanged on upstream `main`
(`a22c7e1c`, checked the same day). Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-11-everest-iso2-session-id-zero`](../interop-runs/2026-08-11-everest-iso2-session-id-zero/notes.md)
— three arms, one variable, and the probe that produced them.

---

**Title:** `iso_server.cpp`: the `[V2G2-460]` session check is skipped when the received SessionID is
zero, so a mid-session request carrying eight zero bytes is served as if it came from the session owner

**Version:** everest-core **2026.02.1** (`b61bb12b8`) and upstream `main` (`a22c7e1c`), `EvseV2G`,
ISO 15118-2.

## The defect

```cpp
// modules/EVSE/EvseV2G/iso_server.cpp:79-82   (main: 86-91)
/* [V2G2-460]: check whether the session id matches the expected one of the active session */
*v2g_response_code =
    ((conn->ctx->current_v2g_msg != V2G_SESSION_SETUP_MSG) && (conn->ctx->ev_v2g_data.received_session_id != 0) &&
     (conn->ctx->evse_v2g_data.session_id != conn->ctx->ev_v2g_data.received_session_id))
        ? iso2_responseCodeType_FAILED_UnknownSession
        : *v2g_response_code;
```

The middle conjunct exempts one value. `[V2G2-460]` has no exemption: the response shall carry
`FAILED_UnknownSession` if the SessionID in **any** request message except `SessionSetupReq` is not
equal to the stored one — and zero is not equal to it. The first conjunct already excludes
`SessionSetupReq`, which is the message where zero legitimately means *"no session yet"*, so the
`!= 0` test is not protecting that case; it only removes a value from the comparison.

**Zero is the worst value to exempt**, for two reasons that meet here:

- it is the value ISO reserves to mean *"I do not have a session"*, so it is the one an EV sends when
  it has no idea what the session is;
- `v2g_session_id_from_exi()` zero-initialises and copies only `bytesLen` bytes — its own comment says
  *"the provided session id could be smaller (error) in case that the peer did not send our full
  session id back to us"* — so a peer that sends an **empty** SessionID also arrives as zero. A car
  that echoes nothing at all and a car that echoes correctly become indistinguishable.

## Measured, with a control on both sides

Three arms against a freshly started station, each sending `SupportedAppProtocolReq`,
`SessionSetupReq`, and then the message the station is actually waiting for — `ServiceDiscoveryReq`.
The **only** difference between arms is the SessionID in that third message.

| arm | SessionID sent | your log | response |
|---|---|---|---|
| **correct** | the id you had just issued | *(nothing)* | 27 B, `… c0 **01** 2004820324` |
| **wrong** | the same id, one bit flipped | `Failed response code detected for message "Service Discovery", error: Unknown Session` | 27 B, `… c0 **e1** 2004820324` |
| **zero** | `0000000000000000` | *(nothing)* | 27 B, `… c0 **01** 2004820324` |

`correct` shows the request is valid and in sequence. `wrong` shows the check is present and works —
so `zero` is not "this station never looks". `zero` and `correct` differ in **no byte** of the response
but the session id you echo back, and your log records no failure for it.

The framing was cross-checked in every arm against your own
`Created new session with id 0x…` line, so the eight bytes the probe wrote really are the SessionID
field. (That check exists because the first attempt at this probe assumed the field was byte-aligned,
sent `…0001` where it meant zero, was correctly refused, and looked like a clean negative.)

## Three places in your own tree already do it right

This is not a design decision taken across the project — it is one conjunct in one file.

**Your DIN twin, thirty lines away, has no such guard:**

```cpp
// modules/EVSE/EvseV2G/din_server.cpp:101-105
/* [V2G-DC-391]: check whether the session id matches the expected one of the active session */
*din_response_code = ((conn->ctx->current_v2g_msg != V2G_SESSION_SETUP_MSG) &&
                      (conn->ctx->evse_v2g_data.session_id != conn->ctx->ev_v2g_data.received_session_id))
                         ? din_responseCodeType_FAILED_UnknownSession
                         : *din_response_code;
```

**Your ISO 15118-20 implementation has no such guard**, and applies the plain comparison in ten states:

```cpp
// lib/everest/iso15118/src/iso15118/d20/context_helper.cpp
bool validate_and_setup_header(message_20::Header& header, const Session& cur_session,
                               const decltype(message_20::Header::session_id)& req_session_id) {
    setup_header(header, cur_session);
    return (cur_session.get_id() == req_session_id);
}
```

**And your test suite already tests the rule — for DIN, with a non-zero id.**
`din_validate_response_code_V2G_DC_391` sets `evse_v2g_data.session_id = 1234` and
`ev_v2g_data.received_session_id = 5678`. There is no ISO-2 equivalent: `FAILED_UnknownSession`
appears in `tests/` only in `din_server_test.cpp`. So the one test that covers this rule would still
pass if it were transplanted to `iso_server.cpp` as written, because 5678 is not zero.

## Suggested fix

Drop the middle conjunct:

```cpp
*v2g_response_code =
    ((conn->ctx->current_v2g_msg != V2G_SESSION_SETUP_MSG) &&
     (conn->ctx->evse_v2g_data.session_id != conn->ctx->ev_v2g_data.received_session_id))
        ? iso2_responseCodeType_FAILED_UnknownSession
        : *v2g_response_code;
```

which is exactly what `din_server.cpp` already does. If the intent was to tolerate an EV that omits
the SessionID, that tolerance is worth making explicit and configurable rather than implicit in a
comparison, because as written it also accepts a peer that never learned the id.

A second test beside the DIN one, with `received_session_id = 0`, would pin it. We would send a PR
only if you want one — which of the two shapes belongs in your tree is yours to pick.

## Context

Found while extending a probe from an earlier run of ours that checked `[V2G2-459]`/`[V2G2-538]`
against the same station — where **your station was correct**, answering the out-of-order request with
the corresponding response message and the right code before closing
([notes](../interop-runs/2026-08-11-everest-iso2-sequence-error/notes.md)). That run listed
`FAILED_UnknownSession` as *"same probe, one more arm, not run"*. This is that arm.

Worth adding in the same breath: **our own station does not implement `[V2G2-460]` at all** —
`FAILED_UnknownSession` appears nowhere in our live code. Yours is ahead of ours here in every respect
but the one conjunct, and we are fixing our side.

---

## Before sending

- [x] **Observe it, do not only read it.** Three arms on the wire against a running station, with the
      framing cross-checked against the station's own log in each.
- [x] **Have a control for both directions.** `correct` proves the request is valid and in sequence;
      `wrong` proves the check exists. Without the second, "zero was served" would be consistent with
      "this station never checks", which is a different and much weaker claim.
- [x] **Check the citation against current `main`.** Fetched upstream `main` `a22c7e1c` on 2026-08-11:
      the guard is unchanged at `iso_server.cpp:86-91`. Note the local checkout is a shallow,
      single-tag clone, so `origin/main` does not resolve in it and `git log HEAD..origin/main` returns
      zero commits whether or not upstream has moved — the fetch above is what actually decided it.
- [x] **Say where they are already right.** The DIN twin, the `-20` implementation and the sequence
      check are all correct; this is one conjunct, not a pattern.
- [x] **Point at their own test.** The rule is tested for DIN with a non-zero id, which is why the
      defect survived — that is a more useful sentence to a maintainer than the requirement alone.
- [x] **Admit our own gap.** We do not implement `[V2G2-460]` at all. Saying so costs nothing and is
      the difference between a report and a complaint.
- [ ] **Decide issue or PR.** The fix is one deletion plus a test; a PR may be quicker to merge than a
      discussion. Their `-2` module has an active test suite to add it to.
- [ ] **Post under your own name, in your own words.**
