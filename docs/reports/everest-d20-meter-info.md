# Draft report to EVerest — `Evse15118D20` never returns `MeterInfo`, even when the EV asks for it

Status: **draft, not sent.** Measured on the wire 2026-08-10 against everest-core **2026.02.1**
(`b61bb12b8`) built from source: a complete 70-exchange ISO 15118-20 DC session against their SIL with
`MeterInfoRequested = true` on every charge-loop request, and a control session without it. Post it under
your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-10-everest-d20-meter-info`](../interop-runs/2026-08-10-everest-d20-meter-info/notes.md) — the
run notes, both recorded sessions, the frame log and their charger log.

Other reports go to everest-core:
[`everest-d20-client-auth.md`](everest-d20-client-auth.md),
[`everest-d20-trust-anchor.md`](everest-d20-trust-anchor.md),
[`everest-d20-ocsp-absent.md`](everest-d20-ocsp-absent.md),
[`everest-d20-ac-namespace.md`](everest-d20-ac-namespace.md),
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md) and
[`everest-loop-shutdown.md`](everest-loop-shutdown.md) — all `Evse15118D20` or libiso15118, so **the same
reviewer** — plus [`everest-isomux.md`](everest-isomux.md),
[`everest-evsev2g-session-log-responses.md`](everest-evsev2g-session-log-responses.md),
[`everest-evse-security-ocsp-dropped.md`](everest-evse-security-ocsp-dropped.md),
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) and one to your fork of Josev's
certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)). The framing in
`everest-loop-shutdown.md` — what everest-core has been worth to this project, and why a report from us
is not a bug filed by a stranger — applies here unchanged and is not repeated.

**This one is different from the five above in a way worth saying up front**: it is the first that is
about the *charge loop* rather than the handshake, and it is a `TODO` you already wrote rather than
something hidden. The value we can add is not telling you it is missing — it is telling you what it
costs, with the requirement identifiers and a measurement.

---

**Title:** `d20::state::DC_ChargeLoop` reads `MeterInfoRequested` off the request and forwards it as a
feedback signal, but never sets `meter_info` on the response — so an EV that asks under `[V2G20-1081]`
gets nothing, which `[V2G20-1082]` makes a *shall*, and the `-20` metering-receipt flow
(`[V2G20-1083]`, `[V2G20-1919]`) has no way to start

**Version:** everest-core **2026.02.1** (`b61bb12b8`), native build, Debian 13. Module `Evse15118D20`,
library `lib/everest/iso15118`, `config-sil-dc-d20`-shaped config with a DC power supply and the
`EvseManager` power meter wired up, plain TCP.

## What we saw

A complete `-20` DC session, 70 exchanges, every response `OK`, three charge loops — and on every one of
the three:

```
MeterInfo: asked in every charge-loop request ([V2G20-1081]); 0 response(s) carried the element ([V2G20-1082]).
```

### The control, which is what makes it sharp

We ran the same session twice, minutes apart, differing in one bit.

**Our request changed.** First `DC_ChargeLoopReq` of each run, session id and timestamp aside:

```
MeterInfoRequested = false   …3062 81 0012006464003c02002400ca
MeterInfoRequested = true    …3062 a1 0012006464003c02002400ca
                                  ↑↑
```

Same 38-byte frame, one bit. So the ask was on the wire.

**Your response did not.** Charge loops 1 and 2, session id aside, are **byte-identical between the two
runs**:

```
loop 1, both runs   00640000020000000080810a01100fe1a01e03f8680780fe1508c0
loop 2, both runs   00640000020000000080810a01100fe1a01e03f0481580fe1508c0
```

Loop 3 differs only in the delivered-energy counter. Your station's answer does not depend on the
question — which is the clearest statement of the defect we can make, and it needed the control run to
be able to say it.

## Where it comes from

`lib/everest/iso15118/src/iso15118/d20/state/dc_charge_loop.cpp`. The field is read, and used — as a
*feedback signal to the module*, at `:261`:

```cpp
m_ctx.feedback.dc_charge_loop_req(req->meter_info_requested);
```

and the response type has the slot —
`include/iso15118/message/dc_charge_loop.hpp:96`:

```cpp
std::optional<datatypes::MeterInfo> meter_info;
```

Between them there is one comment, at `:178`:

```cpp
// TODO(sl): Setting EvseStatus, MeterInfo, Receipt, *_limit_achieved
```

Nothing in `d20/` ever assigns `meter_info`. `ac_charge_loop.cpp:157` carries the same TODO, so AC is in
the same position; we did not measure it, because our AC runs against your SIL wall earlier for an
unrelated reason of ours.

## Why we think it is worth fixing

- **`[V2G20-1081]`** — the EVCC sets `MeterInfoRequested` to TRUE to ask. It is the EV's only mechanism.
- **`[V2G20-1082]`** — *if `[V2G20-1081]` applies*, the SECC **shall** respond with the `ChargeLoopRes`
  including the `MeterInfo` element. Conditional on being asked; unconditional once it is.
- **`[V2G20-1833]`** — independently of any ask: an EVSE equipped with metering technology, supporting
  the capability, shall provide initial `MeterInfo` in the **very first** charge-loop response. Whether
  your `-20` module "supports the capability" is exactly the question this issue puts to you; the
  hardware side of the antecedent is met in your own SIL config.
- **`[V2G20-902]`** — what the element is for: energy charged during the current service session.

**And because of what it blocks, which is more than a reading.** `[V2G20-1083]` says a SECC triggers a
`MeteringConfirmationReq/Res` by including `MeterInfo` and setting `EVSENotification` to
`MeteringConfirmation`; `[V2G20-1919]` says a receipt based on kWh measurements carries the associated
`MeterInfo`. With `meter_info` never set, neither can happen — so the `-20` signed-metering path is not
partially implemented at this station, it is unreachable. For a public charging station that is the
billing evidence chain.

We are citing requirement identifiers and paraphrasing what they oblige, not quoting; the rule is
[`docs/normative-basis.md`](../normative-basis.md). All `-20` identifiers, no document caveat.

## Suggested direction

1. **Set the element.** The reading already reaches the module — `EvseManager` publishes powermeter
   values and your `-2` path uses them. What is missing is a route from there into
   `dc_charge_loop.cpp`'s response, alongside the `EVSEPresentCurrent`/`EVSEPresentVoltage` that already
   come from the same place.
2. **Decide whether to gate it.** `[V2G20-1082]` requires it when asked; `[V2G20-1833]` suggests sending
   an initial one anyway. Sending it whenever a reading exists satisfies both and is the simpler rule —
   it is what our own station does — but the choice is yours and worth a comment either way.
3. **Split the TODO while you are there.** `EvseStatus`, `MeterInfo`, `Receipt` and `*_limit_achieved`
   are four different obligations sharing one comment, and only one of them is this issue.
   `*_limit_achieved` in particular has its own requirement family and its own consequence — a station
   that is at its limit and says otherwise is a different bug from one that reports no meter.
4. **`ac_charge_loop.cpp:157` needs the same**, and we could not measure it.

## Not part of this

- **`Receipt` and `EVSEStatus`** from the same TODO. Named above, not measured, not claimed.
- **`*_limit_achieved`**, likewise — and we have our own history with the `-2` version of that field, so
  we are inclined to think it matters more than it looks. Its own issue if you want one.
- **AC.** Same TODO, unmeasured.
- **Whether your `-2` path is affected.** It is not: our 2026-08-10 `-2` DC charge against `EvseV2G`
  carried metering, and the `-2` receipt flow works. This is the `-20` module only.
- **Our own half.** Until this morning our EVCC hardcoded `MeterInfoRequested` to `false`, so we had
  never asked anyone. That is fixed and tested on our side, and it is why this report exists at all —
  said here because a finding that needed us to build the question first is worth being open about.

---

## Before sending

- [x] **Measure it, with a control.** Two complete sessions differing in one bit of one field: our
      request changed, your response did not. The byte comparison is in the run notes.
- [x] **Prove the ask reached the wire.** `0x81 → 0xa1` in the same 38-byte frame — not an assertion
      about our object model.
- [x] **Check every line reference against the tree.**
      `dc_charge_loop.cpp:178`, `:261`; `ac_charge_loop.cpp:157`;
      `include/iso15118/message/dc_charge_loop.hpp:82`, `:96` — read from the built 2026.02.1 source on
      2026-08-10.
- [ ] **Lead with what it blocks, not with the TODO.** *Your `-20` station cannot start a metering
      confirmation, so the signed-metering path is unreachable* is the sentence; the missing assignment
      is the cause.
- [ ] **Be explicit that you already know.** The TODO is yours. The issue is worth filing anyway because
      it carries the requirement identifiers and a measurement, but opening with *"you have a TODO"*
      would be worth nothing to a maintainer.
- [ ] **Ask about `[V2G20-1833]`'s antecedent.** *Does `Evse15118D20` mean to support the metering
      capability at all?* If the answer is "not yet", that is a roadmap item rather than a defect, and
      `[V2G20-1082]` is still the one that bites once an EV asks.
- [ ] **File one issue, this one**, and mention that AC has the same gap without claiming to have
      measured it.
- [ ] **Post under your own name, in your own words.**
