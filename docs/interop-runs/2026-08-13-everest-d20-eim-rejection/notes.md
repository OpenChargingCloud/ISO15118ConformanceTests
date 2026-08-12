# 2026-08-13 — a rejected authorization never reaches their `-20` station

**Their `-20` charger has the correct branch for a failed EIM authorization, cites the requirement in a
comment, and cannot reach it: the module above it forwards authorization verdicts to the HLC for Plug &
Charge only. With a validator answering `Invalid`, their station answered `AuthorizationRes = OK,
EVSEProcessing = Ongoing` **602 times** where `[V2G20-2230]` gives it 1,5 s to answer `Finished` with
`WARNING_EIMAuthorizationFailure`.**

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), native WSL2 build |
| Ours | harness `f464baa`, stack `da76eee` |
| Config | `config-d20-ours.yaml` — stock wiring, unmodified |
| Session | -20 DC over plain TCP, EIM, forward (our EVCC → their `Evse15118D20`) |
| Arm | [`contract-validator-arm.sh`](../../../tools/interop-everest/contract-validator-arm.sh) with `EIM_ONLY=1`, plus [`mqtt-authorize.sh`](../../../tools/interop-everest/mqtt-authorize.sh) to supply the token |
| Artifacts | [`flow.accepted.md`](flow.accepted.md), [`flow.rejected.md`](flow.rejected.md), [`token-rejected.jsonl`](token-rejected.jsonl) |

The [`-2` arm of the same day](../2026-08-13-everest-contract-validator/notes.md) left this as its last
open item: `Evse15118D20`'s `handle_authorization_response` takes `certificate_status` as
`[[maybe_unused]]`, so the question was whether a verdict survives to the `-20` wire at all. It does
not, and the reason is one level higher than the `[[maybe_unused]]`.

## Both arms

Same station, same token, differing only in the JSON file the validator re-reads per call. EIM, because
`Evse15118D20` offers nothing else — `auth_services.push_back(dt::Authorization::EIM)` at
`ISO15118_chargerImpl.cpp:711` and the `PnC` line beneath it commented out at `:713`.

| verdict returned | `AuthorizationRes` | then |
|---|---|---|
| `Accepted` | `OK`, `Finished` on the 3rd poll | `ScheduleExchange`, `DC_CableCheck` — 610 frames, stops for want of hardware |
| `Invalid` + `certificate_status: CertificateRevoked` | `OK`, `Ongoing` — **602 polls, never anything else** | our EVCC gave up |

Their `Auth` module decided correctly and said so:

```
[INFO] auth:Auth :: Result for token: [redacted] hash: CE55F71752B68164: REJECTED
```

and the station kept answering `Ongoing` anyway.

## Where it stops

`EvseManager` is handed the `ValidationResult` and forwards it to the HLC **only for Plug & Charge**,
under its own comment (`evse_managerImpl.cpp:381-387`):

```cpp
} else if (pnc) {
    // we only send authorization responses to the HLC for PnC rejections. In case of EIM we could
    // still receive a successfull authorization later and therefore we don't inform the HLC
    this->mod->r_hlc[0]->call_authorization_response(…);
}
```

So `Evse15118D20` never learns that the token was rejected. It has no other source for that: its own
`publish_require_auth_eim(nullptr)` (`ISO15118_chargerImpl.cpp:500`) is a request, and the answer only
ever comes back through the call above.

**And the branch that would handle it is right there, with the requirement in the comment**
(`lib/everest/iso15118/src/iso15118/d20/state/authorization.cpp:55-57`):

```cpp
case AuthStatus::Rejected: // Failure [V2G20-2230]
    res.evse_processing = dt::Processing::Finished;
    response_code = dt::ResponseCode::WARNING_EIMAuthorizationFailure;
```

`authorization_status` reaches `Rejected` only from a control event
(`authorization.cpp:85-96`) that is only sent for PnC. The `-20` library is correct; the module wiring
above it makes the correct path dead code.

## What the standard asks for

Paraphrased, per [`normative-basis.md`](../../normative-basis.md) — clause IDs and paraphrase, never ISO
prose:

- **`[V2G20-2219]`** — `AuthorizationRes` shall carry `WARNING_EIMAuthorizationFailure` if EIM
  authorization fails for any reason, expressly including the customer cancelling it.
- **`[V2G20-2230]`** — after an `AuthorizationReq` with `SelectedAuthorizationService = EIM`, when
  authorization has failed **for any reason**, the SECC shall answer with `EVSEProcessing = Finished`
  and that ResponseCode, within `V2G_SECC_Msg_Performance_Time` — Table 215 gives
  **1,5 s** for `AuthorizationReq`. The next allowed request may be another `AuthorizationReq`, so the
  standard already provides for the retry their comment is protecting.

That last point is what decides it. Their reasoning — *"we could still receive a successful
authorization later"* — is a real concern, and ISO 15118-20 answers it: report the failure as
`Finished`, and let the EV ask again. Staying `Ongoing` instead is not a more careful reading of the
requirement, it is the case the requirement rules out.

## What it eventually does — read, not measured

`TIMEOUT_EIM_ONGOING = 1000 * 60 * 3` (`lib/everest/iso15118/include/iso15118/d20/timeout.hpp:31`), and
the timeout branch is tested **before** the EIM switch (`authorization.cpp:36-38`), returning plain
`FAILED`. So after three minutes their station would answer `FAILED` rather than
`WARNING_EIMAuthorizationFailure` — the wrong code, 120× the permitted time. **Our EVCC gave up
first**, at 602 polls, so that half is source and not measurement. Worth one long run before it is
written into a report as observed.

## Filing candidate — not yet written

This is a measured `shall` deviation with the fix already present in their own tree, which puts it
above most of the 44 issues in [`docs/reports/`](../../reports/README.md). Two things a report needs
that this run does not have:

1. **The 180 s tail measured**, so the report can state what they *do* answer rather than what they
   would.
2. **The `-2` twin checked.** The same gate at `evse_managerImpl.cpp:381-387` withholds EIM rejections
   from `EvseV2G` too, but `-2` has its own `auth_timeout_eim` and a different requirement, so it is a
   separate question and may well be fine.

## Not the finding, and worth saying

`certificate_status` being `[[maybe_unused]]` in `Evse15118D20::handle_authorization_response`
(`ISO15118_chargerImpl.cpp:811`) is **not** a defect today: the module offers no PnC, so nothing can
ever set it meaningfully. It becomes one the day the commented-out `auth_services.push_back` at `:713`
is uncommented — as does the empty `case dt::Authorization::PnC:` in `authorization.cpp:67-69`, which
would fall through to `ResponseCode::OK` with `evse_processing` left at its default. Two latent
defects behind one comment, recorded here so the day it is uncommented they are already known.
