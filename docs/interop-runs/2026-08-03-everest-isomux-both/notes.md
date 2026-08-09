# 2026-08-03 — EVerest `IsoMux`, both protocols in one offer

**Two complete ISO 15118-20 DC charges through their multiplexer, from an offer carrying both
protocols — and the second one proves the multiplexer does not read the priorities.**

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2025.10.0**, `IsoMux` in front of `EvseV2G` and `Evse15118D20` |
| Image | `ghcr.io/everest/everest-demo/manager:2025.10.0-patches` (2025.10.0) |
| Ours | `Vanaheimr.V2G.Exi` @ `4603bb8` + the `V2G_INTEROP_SAP_FIRST` knob |
| Session | DC, plain TCP, [`config-mux-ours.yaml`](../2026-08-03-everest-isomux-dc/config-mux-ours.yaml) — unchanged from the earlier run |
| Outcome | **-20 first: 100/100 `OK`. -2 first: 101/101 `OK` — and still routed to -20.** |
| Artifacts | [`flow.20-first.md`](flow.20-first.md), [`flow.2-first.md`](flow.2-first.md), both frame logs and traces, [`their-charger.log`](their-charger.log), [`their-selection-code.txt`](their-selection-code.txt) |

This is the run the [earlier IsoMux notes](../2026-08-03-everest-isomux-dc/notes.md) named as the one
thing they could not do: *"our EVCC offers one protocol per session, so the mux never had to choose
between two offers in one handshake."* It can now, and the answer is more interesting than expected.

## Run 1 — the offer a real car sends

`V2G_INTEROP_PROTOCOL=both`: -20 DC at SchemaID 1 / Priority 1, -2 at SchemaID 2 / Priority 2, in one
`SupportedAppProtocolReq`. Their side:

```
14:12:34  Incoming connection on eth0 …
14:12:34  handshake_req: Namespace: urn:iso:std:iso:15118:-20:DC, Version: 1.0, SchemaID: 1, Priority: 1
14:12:34  iso15118_20:Evs  :: Incoming connection from [::1]:46554
14:12:34  iso15118_20:Evs  :: Received session setup with evccid: EVCC01
```

The mux answered `SchemaID 1`, our EVCC mapped that back to its priority-1 entry, ran the -20 state
machine, and charged: 100 exchanges, every response `OK`, through `DC_CableCheck` (78 polls),
`DC_PreCharge`, the charge loop, `DC_WeldingDetection` and `SessionStop`.

**One entry logged, though two were sent** — which is the first hint at what run 2 confirms.

## Run 2 — the offer that tells the two rules apart

Routing a -20-first offer to the -20 backend is consistent with *two different* station rules: "follow
the EV's ranking" and "take -20 if it is mentioned at all". Only an offer that ranks **-2 above -20**
separates them. `V2G_INTEROP_SAP_FIRST=2` sends exactly that — -2 at SchemaID 1 / Priority 1, -20 at
SchemaID 2 / Priority 2, a legal offer meaning *"I can do both and I would rather speak -2"*:

```
14:16:31  Incoming connection on eth0 …
14:16:31  handshake_req: Namespace: urn:iso:15118:2:2013:MsgDef,        Version: 2.0, SchemaID: 1, Priority: 1
14:16:31  handshake_req: Namespace: urn:iso:std:iso:15118:-20:DC,       Version: 1.0, SchemaID: 2, Priority: 2
```

Both entries logged, so the mux read them both — and it answered **`SchemaID 2`** and handed the
connection to `Evse15118D20` anyway. The EV asked for -2 first and got -20.

Their code says why ([`their-selection-code.txt`](their-selection-code.txt),
`modules/EVSE/IsoMux/v2g_server.cpp`):

```cpp
for (i = 0; i < …AppProtocol.arrayLen; i++) {
    …
    // Check if it supports ISO-20
    const char* iso20_urn = "urn:iso:std:iso:15118:-20";
    if (strncmp(iso20_urn, proto_ns, strlen(iso20_urn)) == 0) {
        iso20 = true;
        free(proto_ns);
        return true;          // ← first -20 entry anywhere wins
    }
    free(proto_ns);
}
```

`Priority` is never read. The loop walks the array **in the order received**, and returns on the first
entry whose namespace starts with `urn:iso:std:iso:15118:-20`; anything else falls through to the -2
backend. So the routing rule is *"does this car mention -20 at all"*, not *"which protocol does this
car prefer"* — and the logging asymmetry between the two runs is the same `return`: run 1 stopped
logging after its first entry because that entry was already the match.

**Whether that is a defect depends on a requirement we have not checked.** The `Priority` field exists
to be ranked, and a station that ignores it takes a decision the EV was entitled to make; but this
project does not hold the ISO 15118-2 requirement text, so the note says what their code does and what
the wire showed, and stops there. Worth raising with them either way, because the *behaviour* is
surprising in a way a one-line comment in their loop would fix.

> **Checked 2026-08-09, and it is a defect.** `[V2G2-169]` makes selecting the EV's highest-ranked
> protocol a *shall*, and `[V2G20-169]` says it again in the `-20` series, so the `-2` document caveat
> in [`normative-basis.md`](../../normative-basis.md) does not decide anything here. Filed as the
> twentieth: [`reports/everest-isomux-sap-priority.md`](../../reports/everest-isomux-sap-priority.md).
> The report leads with neither clause, because reading their tree turned up something better —
> **`EvseV2G` and `Evse15118D20` both read `Priority` correctly**, each citing the requirement in a
> comment, and `v2g_sniff_apphandshake()` is a stripped copy of `EvseV2G`'s handler with exactly those
> two comparisons removed. Routed as the ranking called for, the `-2` backend would have answered
> SchemaID 1 by itself.

## What our side did, and why it is the point

Our EVCC **followed the station's answer rather than its own preference** — read `SchemaID 2`, mapped
it back to the -20 entry, and ran `Evcc20Dc`. That is the whole content of the multi-protocol offer:
the state machine is chosen after the handshake. A car that had picked its machine from its own
ranking (as ours did until this morning) would have run -2 against a -20 backend and produced a
message-set mismatch two exchanges in.

Also worth recording: run 2's response frame is `01fe80010000000480400080`, byte for byte the
`SupportedAppProtocolRes` our own -2-only SECC produces in the `iso2-ac-eim-sapboth` corpus trace —
the same "SchemaID 2" answer, from a foreign station. The corpus scenario recorded this morning
predicted the shape of a real station's reply, which is the nearest thing to external validation a
recorded trace gets.

## Reproduce

Setup as in the [earlier IsoMux run](../2026-08-03-everest-isomux-dc/notes.md); nothing about their
configuration changed. Two notes on the machine:

- **The network needs IPv6.** `IsoMux` binds its listener to the interface's link-local address, so a
  `docker network create` without `--ipv6` gets `bind() failed: Address family not supported by
  protocol` and no TCP port at all. The earlier run had it; a fresh `v2gnet` does not by default.
- **The colima port trap turned up again**, for the fourth time: a relay container created *after* its
  upstream was already listening still accepted host connections and dropped every byte. A second
  relay on a fresh port worked immediately. Neither the relay's socat nor the in-container forwarder
  logged the dead connections, so the symptom is invisible from inside.

```bash
V2G_INTEROP_SECC=127.0.0.1:15153 V2G_INTEROP_PROTOCOL=both V2G_INTEROP_MODE=dc dotnet test …
V2G_INTEROP_SECC=127.0.0.1:15153 V2G_INTEROP_PROTOCOL=both V2G_INTEROP_SAP_FIRST=2 \
  V2G_INTEROP_MODE=dc dotnet test …
```

## Next

- ~~**Report the priority handling to EVerest**, together with the accept-loop shutdown already on the
  list — after checking the requirement text, so the report says which of the two it is.~~
  **Done 2026-08-09**, and separately rather than together: the requirement text arrived on 2026-08-08
  and says it is a defect (`[V2G2-169]`, `[V2G20-169]`), so it is its own filing —
  [`reports/everest-isomux-sap-priority.md`](../../reports/everest-isomux-sap-priority.md).
- ~~**`config-sil-dc-isomux-tls.yaml`**, still untried.~~ **Done 2026-08-06**: it confirmed the priority
  handling a third time and added a sharper finding — the mux serves **TLS 1.2 only**, so a both-offer gets
  a complete **-20 session over TLS 1.2**, and a conformant -20 EV cannot reach the -20 backend at all
  ([`…-isomux-tls`](../2026-08-06-everest-isomux-tls/notes.md)).
- **A both-offer run against a -2-only station**, which no counterparty here currently is: `EvseV2G`
  alone would answer SchemaID 2 and exercise the branch the corpus trace covers in loopback.
