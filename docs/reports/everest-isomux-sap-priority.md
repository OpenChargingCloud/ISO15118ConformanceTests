# Draft report to EVerest — `IsoMux` overrides the EV's protocol ranking

Status: **draft, not sent.** Reproduced three times — everest-core **2025.10.0** and **2026.02.1**,
plain TCP and TLS — with the **same request bytes in and the same response bytes out** every time. The
source was re-read at `b61bb12b8` on 2026-08-09. Post it under your own name; see *Before sending* at
the bottom.

Evidence in this repository:
[`2026-08-03-everest-isomux-both`](../interop-runs/2026-08-03-everest-isomux-both/notes.md) (the run
that found it, with `their-selection-code.txt`),
[`2026-08-05-everest-2026021-matrix`](../interop-runs/2026-08-05-everest-2026021-matrix/notes.md)
finding 2 (the same on the current release), and
[`2026-08-06-everest-isomux-tls`](../interop-runs/2026-08-06-everest-isomux-tls/notes.md) finding 3
(again, over TLS). Each carries the frame log, the flow report and your own charger log.

Three other reports for the same project are in
[`everest-isomux-iso20-over-tls12.md`](everest-isomux-iso20-over-tls12.md) — **same module, same
function, different fix** —
[`everest-loop-shutdown.md`](everest-loop-shutdown.md),
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md) and
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md), plus one to your fork of Josev's
certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)). **File them separately.**
The framing in `everest-loop-shutdown.md` — what everest-core has been worth to this project, and why a
report from us is not a bug filed by a stranger — applies here unchanged and is not repeated.

---

**Title:** `IsoMux`: `v2g_sniff_apphandshake()` routes to the ISO 15118-20 backend on the *first*
`-20` entry in the offer and never reads `Priority`, so an EV that ranks ISO 15118-2 first is put on
`-20` anyway — `[V2G2-169]` and `[V2G20-169]` both make that ranking binding

**Version:** everest-core **2026.02.1** (`b61bb12b8`), and unchanged at `main`. Present identically in
**2025.10.0**. Module `IsoMux`, in front of `EvseV2G` and `Evse15118D20`.

## Summary

A `SupportedAppProtocolReq` that ranks ISO 15118-2 **above** `-20` is a legal offer meaning *"I speak
both and I would rather speak `-2`"*. Your multiplexer supports both — that is what it is for — and
answers `-20`.

Your own log, from 2026.02.1 (the 2025.10.0 and TLS ones are the same three lines):

```
iso_mux:IsoMux :: handshake_req: Namespace: urn:iso:15118:2:2013:MsgDef,  Version: 2.0, SchemaID: 1, Priority: 1
iso_mux:IsoMux :: handshake_req: Namespace: urn:iso:std:iso:15118:-20:DC, Version: 1.0, SchemaID: 2, Priority: 2
iso_mux:IsoMux :: Connected to proxy module for ISO-20
```

Both entries are logged, so both were read; `Priority` is printed two lines above the decision and not
used in it. The session then ran to `SessionStop` in `-20`.

| Run | Release | Transport | EV's ranking | Answered |
|---|---|---|---|---|
| [2026-08-03](../interop-runs/2026-08-03-everest-isomux-both/notes.md) | 2025.10.0 | TCP | `-2` p1, `-20` p2 | SchemaID **2** (`-20`) |
| [2026-08-05](../interop-runs/2026-08-05-everest-2026021-matrix/notes.md) | 2026.02.1 | TCP | `-2` p1, `-20` p2 | SchemaID **2** (`-20`) |
| [2026-08-06](../interop-runs/2026-08-06-everest-isomux-tls/notes.md) | 2026.02.1 | TLS 1.2 | `-2` p1, `-20` p2 | SchemaID **2** (`-20`) |

The same 79-byte `SupportedAppProtocolReq` went out in all three, and the same 12-byte
`SupportedAppProtocolRes` came back — `01fe80010000000480400080`, `OK_SuccessfulNegotiation`,
SchemaID 2. Byte-identical, three times, across two releases and two transports.

The control is the same offer with the ranking reversed (`-20` p1, `-2` p2): that also answers `-20`,
correctly. It is the pair that separates *"follows the EV's ranking"* from *"takes `-20` if it is
mentioned at all"*, and only the second is consistent with both.

## What the standard says

Unusually, twice — with the **same requirement number in each series**:

- **`[V2G2-169]`** (ISO 15118-2) — a *shall*: the SECC picks, from the protocols it supports itself, the
  one the EVCC ranked highest. The `SupportedAppProtocolRes` then names that entry's SchemaID.
- **`[V2G20-169]`** (ISO 15118-20) — the same obligation, restated for `-20`, in the same words as far
  as the meaning goes.
- **`[V2G2-167]` / `[V2G20-167]`** define what the field means: `1` is the highest rank, `20` the
  lowest, at most 20 entries. Both series again.

So the SECC's own capability is a **filter** and the EV's `Priority` is the **ranking** applied within
it. `IsoMux` supports both protocols, so the filter removes nothing and the ranking is the only thing
left to decide the answer.

`-20`'s worked example in `8.2.4` is worth a look while you are there: it offers three entries whose
array order and priority order deliberately differ, and the response names the SchemaID of the
priority-1 entry rather than the first one in the array.

We hold the ISO documents under licence and quote none of them; the identifiers above are how the
industry refers to these obligations, and each sentence states what the requirement obliges rather than
how it is worded. Our rule for that is [`docs/normative-basis.md`](../normative-basis.md).

**One caveat we would rather state than have you find.** Our `-2` document is the **2022 DIS revision**,
not the 2014 edition most `-2` stacks target, so a `[V2G2-xxx]` citation from us is strictly evidence
about the revision. For this one that risk is as low as it gets: `[V2G20-169]` is in the `-20` FDIS,
which needs no such caveat, and the 2019 *ISO 15118 Manual* — written against ISO 15118-2:**2014** —
describes the same rule in its walk-through of the handshake. Three independent places, one of them
contemporaneous with the edition in question.

## Where it comes from in your source

```cpp
// modules/EVSE/IsoMux/v2g_server.cpp:118-142, in v2g_sniff_apphandshake()
for (i = 0; i < conn->handshake_req.supportedAppProtocolReq.AppProtocol.arrayLen; i++) {
    …
    // Check if it supports ISO-20
    const char* iso20_urn = "urn:iso:std:iso:15118:-20";
    if (strncmp(iso20_urn, proto_ns, strlen(iso20_urn)) == 0) {
        iso20 = true;
        free(proto_ns);
        return true;              // <-- first -20 entry anywhere wins
    }
    free(proto_ns);
}
```

and the caller acts on the flag alone (`connection/connection.cpp:436-443`), so the routing rule is
*"does this car mention `-20` at all"*.

That early `return` also explains a logging asymmetry that is otherwise puzzling: a `-20`-first offer
logs **one** `handshake_req` line and a `-2`-first offer logs **two**, because the loop stops at the
match.

## Both of your backends already do it correctly

This is the part that makes us think oversight rather than policy. The multiplexer sits in front of two
modules, and **both** implement the rule — each with its own requirement-id comment.

`EvseV2G` tracks the best rank seen and never returns early
(`modules/EVSE/EvseV2G/v2g_server.cpp:228-283`, in `v2g_handle_apphandshake()`):

```cpp
uint8_t ev_app_priority = 20; // lowest priority
…
} else if ((conn->ctx->supported_protocols & (1 << V2G_PROTO_ISO15118_2013)) &&
           (strcmp(proto_ns, ISO_15118_2013_MSG_DEF) == 0) &&
           (app_proto->VersionNumberMajor == ISO_15118_2013_MAJOR) &&
           (ev_app_priority >= app_proto->Priority)) {           // <-- the ranking
    …
    ev_app_priority = app_proto->Priority;
    conn->handshake_resp.supportedAppProtocolRes.SchemaID = app_proto->SchemaID;
```

`Evse15118D20` does the same in C++, keyed by priority, and cites the requirement in the line that
takes the winner (`lib/everest/iso15118/src/iso15118/d20/state/supported_app_protocol.cpp:26-42`):

```cpp
std::map<uint8_t, uint8_t> ev_supported_protocols{}; // key: priority, value: schema_id
…
res.schema_id = ev_supported_protocols.begin()->second; // [V2G20-167] Highest Prio: 1, Lowest Prio: 20
```

`v2g_sniff_apphandshake()` is visibly a stripped copy of `v2g_handle_apphandshake()` — same name shape,
same `init_appHand_exiDocument` opening, the same
`appHand_responseCodeType_Failed_NoNegotiation; // [V2G2-172]` line, and the same doc comment above it
citing `DIN [V2G-DC-436] ISO [V2G2-540]`. What was dropped in the copy is the two `ev_app_priority`
comparisons.

**And the correct answer was one hop away.** Routed to `EvseV2G`, as the ranking called for, that
module would have found `-2` at Priority 1 and answered SchemaID 1. Nothing else in the path needed to
change; the router alone decided it.

## Why we think it is worth fixing

The `Priority` field is the only place a car can express a preference between protocols, and the answer
is final: the EVCC must run whatever SchemaID comes back (ours does — that is the whole content of a
multi-protocol offer), so its only recourse against an answer it did not want is to hang up.

Two entries in one offer is not an exotic case either. `[V2G20-2129]`'s own notes describe exactly this
car — one that supported `-2` and now adds `-20` — sending both namespaces in one request. It is what a
dual-stack car sends, and a fleet operator staging a `-20` rollout, or an OEM whose `-20` firmware is
newer than its `-2` firmware, has a real reason to rank `-2` first.

If the motivation is *"`-20` is the better protocol, prefer it"* — which would be an understandable
thing to have written — the standard addresses that concern from the other side: `[V2G20-2129]` requires
an EVCC that offers `-20` for an energy transfer mode to support, in `-20`, everything it supported for
that mode in older generations, precisely so that no feature can force a session onto ISO 15118-2. The
lever the standard chose is a constraint on what the car may offer, not permission for the station to
override the ranking. That is our reading and we would rather hear yours than assume it.

Interop is unaffected, and we say so plainly: all four offer shapes complete against your station, and
this cost us nothing but a surprise. It is a conformance point, not an outage.

## Suggested direction

Small, and there is a version of it that does not change any routing decision at all:

1. **Rank instead of returning early.** Walk the whole array, keep the entry with the numerically
   lowest `Priority` among the namespaces the mux can serve, and route on that — the shape
   `EvseV2G` already has. Roughly: drop the `return true`, track `best_priority`/`best_is_iso20`, and
   let the loop finish.

2. **If you would rather not change behaviour yet, make it visible.** One log line at the decision —
   *"selected ISO-20 (priority N of M offered)"* — would have made this a five-minute read instead of a
   source dive, and it is what let us find the TLS finding next door. A comment saying the ranking is
   deliberately ignored would do the same job for the next reader.

3. **A note on the other report.** The fix for
   [`everest-isomux-iso20-over-tls12.md`](everest-isomux-iso20-over-tls12.md) lands in the same routing
   decision — it gates `-20` on the transport, this one picks the entry — so if you are touching
   `connection_handle()` for one, the other is a few lines away. They are separate issues because they
   are separate defects with separate fixes; we mention the adjacency only so that you are not
   surprised to meet both in the same `if`.

## Also seen, not part of this

`Evse15118D20`'s SAP handler treats `-20:AC` and `-20:DC` as interchangeable when ranking — both land in
the same `ev_supported_protocols` map, so a station configured for DC can answer the SchemaID of an
`-20:AC` entry if the car ranked AC higher. We have **not** run that case and are not reporting it; the
mode mismatch would surface at `ServiceDiscovery` and it may well be deliberate. Mentioned because it
sits in the file you would open for point 1.

---

## Before sending

- [x] **Reproduce it yourself.** Three times: 2025.10.0 (2026-08-03), 2026.02.1 (2026-08-05), and over
      TLS (2026-08-06), each with the discriminating offer and each with the reversed-ranking control.
      Byte-identical request and response every time.
- [x] **Re-check every line reference against the tree.** Read from the built 2026.02.1 source on
      2026-08-09: `IsoMux/v2g_server.cpp:118-142`, `IsoMux/connection/connection.cpp:436-443`,
      `EvseV2G/v2g_server.cpp:228-283`,
      `lib/everest/iso15118/src/iso15118/d20/state/supported_app_protocol.cpp:26-42`.
- [x] **Check whether it is still there before sending.** Unchanged at `main` as of 2026-08-09; check
      again on the day, since this is a small function that could be touched at any time.
- [ ] **Lead with the two backends, not with the clause.** *"Both of your modules implement this and the
      router in front of them does not"* is the sentence that makes it a five-line fix rather than a
      debate. The requirement is what makes it decidable, and it belongs second.
- [ ] **Ask whether preferring `-20` is deliberate**, before calling it a defect. If it is, the answer
      is interesting in its own right and point 2 is the whole issue.
- [ ] **Say that interop is unaffected.** Leaving that out would overstate it, and overstating a small
      finding is how the next one gets ignored.
- [ ] **Carry the `-2` caveat honestly** — or lead with `[V2G20-169]`, which does not need it, and offer
      `[V2G2-169]` as the second citation.
- [ ] **File one issue, this one.** The TLS-1.2 finding in the same function is its own report on
      purpose.
- [ ] **Post under your own name, in your own words.**
