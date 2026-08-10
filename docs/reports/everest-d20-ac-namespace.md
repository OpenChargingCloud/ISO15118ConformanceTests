# Draft report to EVerest — a DC-configured `Evse15118D20` accepts the ISO 15118-20 **AC** message set

Status: **draft, not sent.** Measured on the wire 2026-08-10 against everest-core **2026.02.1**
(`b61bb12b8`) built from source: three handshake arms with a control, and one session carried through
to the point where it breaks. Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-10-everest-d20-ac-namespace`](../interop-runs/2026-08-10-everest-d20-ac-namespace/notes.md)
— the run notes and four station logs, one per arm.

Five other reports go to everest-core:
[`everest-isomux.md`](everest-isomux.md) (four findings in the multiplexer — **§1 there is this
finding's sibling**, the same requirement failed from the other side),
[`everest-d20-client-auth.md`](everest-d20-client-auth.md) — **the same module and the same handshake**,
two issues about what its TLS server asks the EV for —
[`everest-loop-shutdown.md`](everest-loop-shutdown.md),
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md) — **also libiso15118, so
the same reviewer** — and
[`everest-evsev2g-session-log-responses.md`](everest-evsev2g-session-log-responses.md) with
[`everest-evse-security-ocsp-dropped.md`](everest-evse-security-ocsp-dropped.md). Plus
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) and one to your fork of Josev's
certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)). The framing in
`everest-loop-shutdown.md` — what everest-core has been worth to this project, and why a report from us
is not a bug filed by a stranger — applies here unchanged and is not repeated.

---

**Title:** `libiso15118` `d20::state::handle_request()` accepts `urn:iso:std:iso:15118:-20:AC` and
`:-20:DC` interchangeably without regard to what the station is configured for, so a DC-only station
answers `OK_SuccessfulNegotiation` to an AC-only offer and commits the session to a message set it
cannot serve — `[V2G20-169]` makes the station's own capability a filter *before* the EV's ranking

**Version:** everest-core **2026.02.1** (`b61bb12b8`), native build, Debian 13. Module
`Evse15118D20` alone (no `IsoMux` involved), `config-sil-dc-d20`-shaped config: DC power supply,
`charge_mode: DC`, no AC hardware in the module graph. Plain TCP.

## What we saw

Three arms, a fresh station process each:

| Arm | The EV offered | The station answered |
|---|---|---|
| **A** | `[1] -20:AC`, `[2] -20:DC` | **`-20:AC`** |
| **B** (control) | `[1] -20:DC`, `[2] -20:AC` | `-20:DC` |
| **C** | `[1] -20:AC` — nothing else | **`-20:AC`**, `OK_SuccessfulNegotiation` |

**B is the control, and it is why this is not a ranking bug**: `Priority` *is* honoured. What is
missing is the other half — the station's own capability, applied as a filter before the ranking.

**C is the finding in its simplest form**: one entry, the AC message set, a station with no AC
anything, and a positive answer.

## What it costs

Arm C again, with your `sil-car.sh` plug-in so authorization completes and the session can continue:

```
SupportedAppProtocolReq(-20:AC)  → OK_SuccessfulNegotiation   ← the station commits to AC here
SessionSetupReq                  → OK
AuthorizationSetupReq            → OK
AuthorizationReq                 → OK        (the plug-in token)
ServiceDiscoveryReq              → OK, services 2 and 6       ← DC and DC_BPT. No AC service exists.
```

Our EVCC stops there, correctly:

```
ServiceDiscovery: the station offers no AC energy-transfer service (wanted 1/5, offered 2, 6).
```

So the station spent four message pairs, an authorization and a token on a session whose message set it
could never serve — and the **EV** is what had to notice. `Failed_NoNegotiation` at the handshake is
what the standard provides for this, and your handler already returns it, just not in this case.

The AC and DC `-20` namespaces are separate `ProtocolNamespace` values precisely because they select
different **message sets**: answering with the AC SchemaID tells the car to encode the rest of the
session against the AC schema. It is a commitment, not a preference.

## Where it comes from

`lib/everest/iso15118/src/iso15118/d20/state/supported_app_protocol.cpp:27-44`:

```cpp
std::map<uint8_t, uint8_t> ev_supported_protocols{}; // key: priority, value: schema_id

for (const auto& protocol : req.app_protocol) {
    if (protocol.protocol_namespace.compare(ISO20_DC_NAMESPACE) == 0) {
        ev_supported_protocols[protocol.priority] = protocol.schema_id;
    } else if (protocol.protocol_namespace.compare(ISO20_AC_NAMESPACE) == 0) {
        ev_supported_protocols[protocol.priority] = protocol.schema_id;
    } else if (protocol.protocol_namespace.compare(custom_protocol_namespace.value_or("")) == 0) {
        ev_supported_protocols[protocol.priority] = protocol.schema_id;
    }
}

if (ev_supported_protocols.empty()) {
    return response_with_code(res, ResponseCode::Failed_NoNegotiation);
}

res.schema_id = ev_supported_protocols.begin()->second; // [V2G20-167] Highest Prio: 1, Lowest Prio: 20
```

Both namespaces land in the same map with no reference to what this station can do — and
`handle_request` could not consult it if it wanted to: it takes the request and the *custom* namespace,
and nothing else. The `[V2G20-167]` comment on the last line is right as far as it goes; what is absent
is the filter that should precede the ranking.

The information exists a few frames up: the configured energy services are what
`ServiceDiscoveryRes` answers with, four messages later.

## Why we think it is worth fixing

**`[V2G20-169]`** — the SECC selects, from the protocols **it supports itself**, the one the EVCC
ranked highest. Two halves, and this implements the second without the first. `[V2G20-167]` defines the
priority field and is already cited in the code.

We are citing requirement identifiers and paraphrasing what they oblige, not quoting the text; our rule
is [`docs/normative-basis.md`](../normative-basis.md). `[V2G20-169]` is a `-20` identifier and carries
no document caveat.

**And because the failure mode is the expensive kind.** A refusal at the handshake costs one message
pair and tells the car exactly what is wrong. What happens instead costs the handshake, session setup,
authorization setup, an authorization — a *token*, in a PnC deployment a contract-certificate
validation — and then fails at `ServiceDiscovery` with a mismatch the car has to diagnose. A car with a
less careful service check than ours may get further still.

This is the sibling of §1 in [`everest-isomux.md`](everest-isomux.md), from the other side: there the
router reads the namespace and not the ranking; here the backend reads the ranking and not its own
capability. Between them the two halves of `[V2G20-169]` are each implemented once and never together.

## Suggested direction

1. **Filter before ranking.** `handle_request` needs to know which energy transfer modes this station
   serves — the same information `ServiceDiscoveryRes` is built from — and should admit a namespace to
   the map only if it can serve it. An offer that survives to an empty map already returns
   `Failed_NoNegotiation` at `:39-41`, so the shape is there; only the admission test is missing.
2. **If a DC station serving the AC message set is deliberate**, say so in a comment at `:29-33`, and
   consider whether `ServiceDiscoveryRes` should then carry a diagnostic rather than an empty AC list.
   We would rather hear that answer than assume it is an oversight.
3. **Worth a glance while you are there:** `custom_protocol_namespace` goes into the same map on the
   same terms. We have not exercised it and make no claim.

## Not part of this

`IsoMux` in front of this module has its own SAP defect, in the opposite direction, and one of us
routing correctly would not fix the other — [`everest-isomux.md`](everest-isomux.md) §1.

We did not test what a *mode-mismatched* session does to your station's own state beyond
`ServiceDiscovery`, because our EV stops there. Whether the AC-committed session leaves anything behind
in `EvseManager` is not something we looked at.

---

## Before sending

- [x] **Reproduce it, with a control.** Three arms, fresh station each; arm B shows the ranking *is*
      honoured, which is what makes arm C a capability defect rather than a priority one.
- [x] **Carry it past the handshake.** With the car plugged in the session reaches `ServiceDiscovery`
      and dies there on services 2 and 6 — the cost is measured, not asserted.
- [x] **Check every line reference against the tree.**
      `lib/everest/iso15118/src/iso15118/d20/state/supported_app_protocol.cpp:17-18`, `:27-44`,
      `:39-41` — read from the built 2026.02.1 source on 2026-08-10.
- [ ] **Lead with arm C**, not with the code: *a station with no AC hardware said yes to an AC-only
      offer.* One sentence, and it is the whole issue.
- [ ] **Ask whether it is deliberate** before calling it a defect — a station that serves the AC message
      set for some reason we cannot see is a possibility, and the answer would be worth having.
- [ ] **Say where it costs.** The handshake refusal is one message pair; what happens instead spends an
      authorization and a token first.
- [ ] **Mention the `IsoMux` sibling** if both are open at once, so a maintainer sees the two halves of
      `[V2G20-169]` together.
- [ ] **File one issue, this one.**
- [ ] **Post under your own name, in your own words.**
