# Draft report to EVerest (`everest-core`) — a rejected EIM authorization is never reported to the `-20` stack

Status: **draft, not sent.** Measured on the wire 2026-08-13 against **everest-core 2026.02.1**
(`b61bb12`), `Evse15118D20`, ISO 15118-20 DC over plain TCP, with an ISO 15118-2 control on the same
station. Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-13-everest-d20-eim-rejection`](../interop-runs/2026-08-13-everest-d20-eim-rejection/notes.md)
— four arms, including the `-2` control that says which half of your code is right.

Citations are labelled `[tag]` for **2026.02.1**, the tree this was measured against, and `[main]` for
`8dcd75f`, where all of it is unchanged. The two differ only in line numbers.

**Send this one first, then its sibling.**
[`everest-d20-ac-contactor-edge.md`](everest-d20-ac-contactor-edge.md) is the same shape found a day
later on the same module — `EvseManager` not telling the `-20` stack something it knows, fixed on
`EvseManager`'s side — and the two together are a stronger argument than either alone: a verdict never
forwarded, and a state never re-reported. Sent in the other order the second reads as a rehash.
[`sending-order.md`](sending-order.md) carries this as dependency 5.

---

**Title:** `EvseManager` forwards authorization verdicts to the HLC for PnC only, so `Evse15118D20` never
learns an EIM token was rejected — 180 s of `Ongoing` where `[V2G20-2230]` allows 1,5 s and
`WARNING_EIMAuthorizationFailure`

**Version:** everest-core **2026.02.1** (`b61bb12`), confirmed unchanged on `main` (`8dcd75f`).
`modules/EVSE/EvseManager` and `lib/everest/iso15118`, ISO 15118-20.

## The defect is one `else if`

```cpp
// modules/EVSE/EvseManager/evse/evse_managerImpl.cpp:381-387  [tag]   (448-454 [main])
} else if (pnc) {
    // we only send authorization responses to the HLC for PnC rejections. In case of EIM we could
    // still receive a successfull authorization later and therefore we don't inform the HLC
    this->mod->r_hlc[0]->call_authorization_response(
        validation_result.authorization_status,
        validation_result.certificate_status.value_or(types::authorization::CertificateStatus::Accepted));
}
```

`call_authorization_response` is the **only** way a verdict reaches either HLC stack. Your own
`Evse15118D20` asks for one — `publish_require_auth_eim(nullptr)`,
`Evse15118D20/charger/ISO15118_chargerImpl.cpp:500` `[tag]` — and has no other source for the answer.

And `Evse15118D20` **offers no PnC at all**: `auth_services.push_back(…Authorization::PnC)` is commented
out at `Evse15118D20/charger/ISO15118_chargerImpl.cpp:713` `[tag]` (862 `[main]`), under *"Currently PnC is not supported"* at
`:814` `[tag]` (963 `[main]`).

**The two facts meet: every `-20` session is EIM, and EIM verdicts are not forwarded. So no
authorization verdict of any kind ever reaches your `-20` state machine.** A positive one is not needed
— the session proceeds on the `Accepted` path through `EvseManager` itself — but a rejection has nowhere
to go.

## Your `-20` library already handles it, correctly, and cites the requirement

```cpp
// lib/everest/iso15118/src/iso15118/d20/state/authorization.cpp:55-57  [tag]  (57-59 [main])
case AuthStatus::Rejected: // Failure [V2G20-2230]
    res.evse_processing = dt::Processing::Finished;
    response_code = dt::ResponseCode::WARNING_EIMAuthorizationFailure;
```

`authorization_status` becomes `Rejected` only from a control event (`d20/state/authorization.cpp:85-96` `[tag]`),
and that event is only ever sent by the `else if (pnc)` above. **The branch is correct and unreachable.**
This is the same shape as the `IsoMux` routing finding: the layer that is right sits underneath the layer
that decides.

## What ISO 15118-20 asks for

- **`[V2G20-2219]`** — `AuthorizationRes` shall carry `WARNING_EIMAuthorizationFailure` if EIM
  authorization fails **for any reason**, expressly including the customer cancelling it.
- **`[V2G20-2230]`** — after an `AuthorizationReq` with `SelectedAuthorizationService = EIM`, when
  authorization has failed for any reason, the SECC shall answer `EVSEProcessing = Finished` with that
  ResponseCode **within `V2G_SECC_Msg_Performance_Time`** — Table 215 gives **1,5 s** for
  `AuthorizationReq`.

`[V2G20-2230]` then lists the next allowed requests, and **another `AuthorizationReq` is among them**.
That matters here more than anywhere else in the clause: the case your comment is protecting — the
customer may still authorize, so do not end the session — is the case the standard has already provided
for. Report the failure as `Finished`, and let the EV ask again.

## And ISO 15118-2 asks for the opposite — please do not fix both the same way

This is why the report exists in this shape rather than as "you forgot to forward EIM". We ran the
identical arm against `EvseV2G` on the same station, and **`-2` behaves the same way and is right to**:

- **`[V2G2-854]`** — EIM selected and no ***positive*** EIM information available → the SECC shall set
  `EVSEProcessing = Ongoing_WaitingForCustomerInteraction`. A rejected token is exactly that case.
- **`[V2G2-856]`** — only *positive* information makes it `Finished`.
- **`[V2G2-845]`** — and the EV shall keep resending `AuthorizationReq` while that lasts.

ISO 15118-2 has no `WARNING_EIMAuthorizationFailure` and places no duty on the SECC to report a negative
EIM result at all. `EvseV2G` implements this by name, with the clause in the comment:

```cpp
// modules/EVSE/EvseV2G/iso_server.cpp:947-948  [tag]   (1117-1118 [main])
conn->ctx->evse_v2g_data.evse_processing[PHASE_AUTH] =
    (uint8_t)iso2_EVSEProcessingType_Ongoing_WaitingForCustomerInteraction; // [V2G2-854]
```

**So the comment in `evse_managerImpl.cpp` is not an oversight. It is ISO 15118-2's rule, correctly
stated — applied by a shared module to a protocol that changed it.** A fix that simply stops
distinguishing EIM from PnC will regress `-2`.

## Measured, with three controls

Same station, same rejected token, one JSON file deciding the verdict. Our EV's own patience had to be
raised out of the way first — it stops polling an `Ongoing` phase after 60 s, which is shorter than every
timer in play here and produced a first result that was our number, not yours.

| arm | verdict returned | `AuthorizationRes` |
|---|---|---|
| **`-20` control** | `Accepted` | `OK`, `Finished` on the 3rd poll → `ScheduleExchange`, `DC_CableCheck`, 610 frames |
| **`-20`, short wait** | `Invalid` | `OK`, `Ongoing` ×602 — our EV gave up |
| **`-20`, full wait** | `Invalid` | `OK`, `Ongoing` **×1 800**, then response 1802 = **`FAILED`** |
| **`-2` twin** | `Invalid` | `OK`, `Ongoing` ×2 003, then `FAILED` at **299,8 s** — your `auth_timeout_eim`, and correct |

Your own `Auth` module decided correctly and said so, in every rejecting arm:

```
02:04:09.284  [INFO] auth:Auth :: Result for token: [redacted] hash: CE55F71752B68164: REJECTED
02:07:14.387  [INFO] iso15118_charge :: Closing TCP connection
```

**185,1 s** from your verdict to the connection closing, around the 180 s `TIMEOUT_EIM_ONGOING`
(`lib/everest/iso15118/include/iso15118/d20/timeout.hpp:31`, same line `[tag]` and `[main]`). Allowed:
**1,5 s**. And the code that finally arrives is `FAILED`, not `WARNING_EIMAuthorizationFailure`, because
the timeout is tested **before** the EIM switch:

```cpp
// lib/everest/iso15118/src/iso15118/d20/state/authorization.cpp:36-38  [tag]   (38-40 [main])
if (timeout_reached) {
    return response_with_code(res, dt::ResponseCode::FAILED);
}
```

The `Accepted` control matters: it shows the whole path works and the car reaches authorization normally,
so the rejecting arms are not a session that never got there.

## Suggested direction

Forward the verdict for EIM as well, and let each protocol's state machine decide what it means — which
is where the two rules already live, correctly, on both sides:

```cpp
// evse_managerImpl.cpp — instead of `} else if (pnc) {`
} else {
    this->mod->r_hlc[0]->call_authorization_response(
        validation_result.authorization_status,
        validation_result.certificate_status.value_or(types::authorization::CertificateStatus::Accepted));
}
```

`Evse15118D20` then reaches the branch it already has. `EvseV2G` needs to keep doing what it does today:
its `handle_authorization_response` (`EvseV2G/charger/ISO15118_chargerImpl.cpp:279-292` `[tag]`) sets
`evse_processing[PHASE_AUTH] = Finished` unconditionally, so under `-2` a rejection would newly end the
authorization phase — which `[V2G2-854]` does not want. **That is the part of this that needs your
judgement rather than a patch from us**, and it may be why the gate was written this way. Two shapes that
would work, and you will know which fits:

- keep the distinction, but make it *per protocol* rather than per authorization type — the `-2` module
  ignores a rejection, the `-20` module acts on it; or
- forward always, and give `EvseV2G`'s handler the `[V2G2-854]` branch it currently lacks.

## Secondary, and not part of this ask

**When the ongoing timeout genuinely expires** — nobody ever authorizes — `d20/state/authorization.cpp:36-38`
answers plain `FAILED`. Whether that should also be `WARNING_EIMAuthorizationFailure` under
`[V2G20-2219]`'s *"fails due to any reason"* is a separate reading, and it is only reachable at all
*because* of the defect above. We have not measured it as its own case and are not asking for it here.

Two more that are **latent, not defects today**, recorded so they are known the day the commented-out
`auth_services.push_back` at `Evse15118D20/charger/ISO15118_chargerImpl.cpp:713` `[tag]` is uncommented:

- `handle_authorization_response` takes `certificate_status` as `[[maybe_unused]]`
  (`Evse15118D20/charger/ISO15118_chargerImpl.cpp:811` `[tag]`), so a `CertificateRevoked` verdict is discarded. Today nothing
  can set it meaningfully, because there is no PnC.
- `case dt::Authorization::PnC:` in `d20/state/authorization.cpp:67-69` `[tag]` (69-71 `[main]`) is empty and falls
  through to `ResponseCode::OK` with `evse_processing` left at its default.

## Context, and our own side

Found while building a stand-in for the contract-validating backend your SIL does not ship — an external
process that registers over MQTT as the module your configs already declare, using `--standalone` and
your own `everestpy`, so nothing in EVerest is patched and your `Auth` module cannot tell the difference.
Your `-2` Plug & Charge path passed that arm cleanly: chain accepted, our signature verified, verdict
carried to the wire, including `FAILED_CertificateRevoked` when we answered `CertificateRevoked`. The
`-20` arm is what found this.

**One thing about your SIL that is not a defect but cost us a day**, in case it saves someone else one:
`EvseManager` republishes the PnC token through its own `token_provider` implementation, and only
`config-sil-ocpp-pnc.yaml` and `config-sil-ocpp201-pnc.yaml` connect that implementation to `auth`. In
every other config the token is published to a variable nobody subscribed to, and a perfect Plug & Charge
session ends at `auth_timeout_pnc` with no token in any log and no error anywhere. EIM is unaffected,
which is what hides it. Your plain SIL configs are simply not PnC configs — but the failure mode is
silent, and a line in the config comments would be worth more than it costs.

**Our own station has not been measured on this at all.** `Secc20Base` carries its own authorization
handling and no test in our suite drives a rejected EIM token through it, so we are not in a position to
claim we get `[V2G20-2230]` right either. We found yours by building the instrument; ours is next.

---

## Before sending

- [x] **Observe it, do not only read it.** Four arms on the wire, and the decisive interval comes from
      *your* log — 185,1 s between your `Auth` logging `REJECTED` and your own connection close.
- [x] **Outlast their timer, not ours.** The first pass measured our EV's 60 s patience and stopped 118 s
      short of the answer; a run that does not raise it reads exactly like a station that never replied.
- [x] **Have a positive control.** The `Accepted` arm runs to `DC_CableCheck`, so the rejecting arms are
      not a session that failed earlier.
- [x] **Check the twin protocol before proposing a fix.** `-2` does the same thing and is **right** to —
      `[V2G2-854]`/`[V2G2-856]`/`[V2G2-845]` invert the `-20` rule. This changes the fix, and a report
      without it would have caused a regression.
- [x] **Say where they are right.** The `-20` `Rejected` branch, its requirement citation, `EvseV2G`'s
      `[V2G2-854]` handling and the whole `Accepted` path are all correct.
- [x] **Admit our own gap.** We have never driven a rejected EIM token through our own station.
- [x] **Check whether `main` has moved — checked 2026-08-13 against upstream, not against the clone.**
      `main` is `8dcd75f` and **every site is unchanged**. All five numbers below are `[main]` — they do
      not resolve against a 2026.02.1 checkout and are not meant to: the `else if (pnc)` gate
      (`evse_managerImpl.cpp:449` `[main]`), the unreachable `Rejected` branch
      (`d20/state/authorization.cpp:57-59` `[main]`), the empty PnC case (`:70` `[main]`), the
      commented-out `auth_services.push_back`
      (`Evse15118D20/charger/ISO15118_chargerImpl.cpp:862` `[main]`) and `TIMEOUT_EIM_ONGOING`
      (`timeout.hpp:31` `[main]`, the one line that is the same in both). Live on `main`, not only in
      the release.
- [ ] **Decide one issue or two.** Written as one: the 180 s tail is a consequence of the gate, not an
      independent ask, and splitting it would put two issues in front of the same maintainer for one
      symptom. If they prefer, the *"Secondary"* section lifts out cleanly.
- [ ] **Re-read the citations against the tree before posting** — every one is labelled `[tag]` or
      `[main]` and the two have different line numbers; `tools/reports-audit/check_citations.py`
      resolves against whichever checkout is configured, so the label decides which number is right.
- [ ] **Post under your own name, in your own words.**
