# Draft report to IoT.bzh (tux-evse) — the one thing a capture knows that a config cannot, and the pipeline drops it

Status: **draft, not sent.** Found 2026-08-07 on `iso15118-simulator-rs` **`main` @ `fc51088`** built
from source, while mining their `tesla-3-din.pcap` for the parts a project without DIN 70121 can still
read. Post under your own name; see *Before sending* at the bottom.

Two observations, **E** and **F**, continuing the lettering of
[`tux-evse-tls.md`](tux-evse-tls.md) (A, B) and [`tux-evse-spin.md`](tux-evse-spin.md) (C, D). E is a
fidelity question about what a converted scenario carries; F is a one-line bug that only a DIN capture
can show. **File them separately** — E deserves a conversation, F deserves a patch.

Evidence: [`2026-08-07-tesla-din-handshake`](../interop-runs/2026-08-07-tesla-din-handshake/notes.md),
which includes their own capture's handshake decoded frame by frame.

---

# Issue E — the captured `SupportedAppProtocol` offer is decoded, then discarded

**Title:** `pcap-iso15118` parses the car's full protocol offer and emits a payload-free
`app-set-protocol`; `afb-evcc` can only ever offer one protocol from static config — so a replayed
capture never puts the car's real offer on the wire

**Version:** `iso15118-simulator-rs` `main` (`fc51088`).

## Summary

Their `tesla-3-din.pcap` is a real Tesla Model 3 at a real charge point. Decoded, its first frame — an
ordinary `SupportedAppProtocolReq` — says this:

```
document order 1:  schemaId 1  priority 2  v2.0  urn:din:70121:2012:MsgDef
document order 2:  schemaId 2  priority 1  v0.7  urn:tesla:din:2018:MsgDef
```

**A vendor-proprietary protocol, at the highest priority, from a car in the field.** The station it met
declined that entry and answered `OK_SuccessfulNegotiation` with SchemaID 1 — the standard DIN one. An
entirely ordinary negotiation, and one no synthetic test client produces.

Convert that capture and replay it, and none of it reaches the station under test. The scenario carries:

```json
{"uid":"app-set-protocol","verb":"din:app_proto_req","injector_only":true}
```

No namespaces, no priorities, no schema ids. At replay, `app_proto_req_cb` builds a **one-entry** offer
from the binding's `protocol:` config string. The station under test sees a single standard namespace,
whatever the car actually offered.

## Where it comes from

The striking part is that the information is already parsed, in a typed field, at the moment the
scenario is created.

```rust
// pcap-15118/src/pcap-import.rs:667   — the whole offer is kept
ctx.supported_protocols = payload.get_protocols();

// :646-649                            — and used exactly once, to resolve the station's answer
let proto = &ctx.supported_protocols[idx];
if schema == proto.get_schema() {
    ctx.session_protocol = v2g::ProtocolTagId::from_urn(proto_name)?;
}

// :660                                — then only the resolved single protocol is carried forward
ctx.scenario = RefCell::new(ScenarioLog::new(pkg_count, ctx.session_protocol, debug_only)?);

// :298                                — which emits a directive with no payload at all
format!("{{'uid':'app-set-protocol','verb':'{}:app_proto_req','injector_only':true}}", label)
```

That resolution loop is worth pointing out for the right reason: **it is correct.** Matching the
answered `schema` back to the offered entry is exactly the SchemaID semantics, and plenty of
implementations get it wrong. The negotiation is understood here — and then flattened to one label.

The replay side cannot consume more even if it were offered:

```rust
// afb-evcc/src/verbs.rs:210-221
fn app_proto_req_cb(afb_rqt: &AfbRequest, _args: &AfbRqtData, context: &AfbCtxData) -> … {
    let ctx = context.get_ref::<IsoMsgReqCtx>()?;
    let iso2_proto = V2G_PROTOCOLS_SUPPORTED_LIST[ctx.protocol as usize];
    let v2g_body = SupportedAppProtocolReq::new(iso2_proto)?.encode();
    …
}
```

`_args` is discarded, and the offer is one element indexed by the configured protocol.

## Why we think it is worth raising

Their simulator exists so other people's stacks can be checked against real vehicle behaviour, and
`SupportedAppProtocol` is the **first exchange of every session** — the one place where a packet
capture beats a synthetic client outright, because real cars offer things a test client never would:

- **proprietary namespaces**, which a station must decline without falling over;
- **an unknown entry at the highest priority**, which is the case the fallback exists for and which
  otherwise never gets exercised;
- **document order, SchemaID order and Priority order that disagree.** In this capture all three come
  apart — the preferred entry is second in the document and carries SchemaID 2. A generated offer
  almost always makes the three coincide, which lets a station that conflates them pass for years. (It
  did here: our own station answered a literal SchemaID 1 for months, because our own EV always
  assigned 1 to its first entry. A real car's offer is what makes that visible.)

None of that survives the pipeline today, so the negotiation stage of a replayed capture tests a
config string rather than a car.

## Suggested direction

Two independent halves; the first is useful on its own.

1. **Carry the offer into the scenario.** `supported_protocols` is already in hand at
   `pcap-import.rs:660`; emitting it as a `query` on the `app-set-protocol` transaction would make the
   scenario self-describing, and would be visible to anyone reading the JSON even before the injector
   can use it.
2. **Let `app_proto_req_cb` take an offer from `_args`**, falling back to the configured single
   protocol when none is given. That keeps every existing scenario working unchanged.

We are not asking for the proprietary protocol to be *supported* — only for the offer to be replayed
as captured, so the station under test gets the question the car actually asked.

---

# Issue F — the closing SDP verb is hardcoded `iso2:`, in every scenario including DIN ones

**Title:** `pcap-import.rs:318` emits `'verb':'iso2:sdp_evse_req'` literally, so a DIN scenario ends
with a transaction addressed to the wrong API

**Version:** as above.

## Summary

Convert their four captures and compare the two SDP transactions each scenario gets:

| capture | opening SDP | closing SDP |
|---|---|---|
| `audi-dc-iso2.pcap` | `iso2:sdp_evse_req` | `iso2:sdp_evse_req` |
| `vw-ac-iso2.pcap` | `iso2:sdp_evse_req` | `iso2:sdp_evse_req` |
| `porsche-taycan-4s-*.pcap` | `iso2:sdp_evse_req` | `iso2:sdp_evse_req` |
| **`tesla-3-din.pcap`** | **`din:sdp_evse_req`** | **`iso2:sdp_evse_req`** |

Every other transaction in that file is `din:`. Only the teardown is not.

## Where it comes from

The asymmetry is in one file, 24 lines apart. The opening SDP is templated with the detected protocol:

```rust
// pcap-15118/src/pcap-import.rs:294
format!("{{'uid':'sdp-evse','verb':'{}:sdp_evse_req',…,'query':{{'action':'discover'}}}}", label)
```

The closing one is a literal:

```rust
// pcap-15118/src/pcap-import.rs:318
JsoncObj::parse("{'uid':'sdp-evse','verb':'iso2:sdp_evse_req','injector_only':true,'query':{'action':'forget'}}")
```

`self.protocol` is in scope in `session_close`, so `self.protocol.to_label()` is all it needs.

It has stayed invisible because it is correct for three of the four captures — and the fourth is the
one nobody replays.

## How to reproduce

```bash
pcap-iso15118 --pcap_in=afb-test/trace-logs/tesla-3-din.pcap --json_out=tesla.json --compact=basic
python3 -c "import json;print([t['verb'] for t in json.load(open('tesla.json'))['binding'][0]['scenarios'][0]['transactions'] if 'sdp' in t['verb']])"
# ['din:sdp_evse_req', 'iso2:sdp_evse_req']
```

And for issue E, in the same file:

```bash
python3 -c "import json;print([t for t in json.load(open('tesla.json'))['binding'][0]['scenarios'][0]['transactions'] if 'app_proto' in t['verb']])"
# [{'uid': 'app-set-protocol', 'verb': 'din:app_proto_req', 'injector_only': True}]
```

The car's actual two-entry offer is in the pcap, in the first TCP frame of the session — 74 bytes,
including the literal string `urn:tesla:din:2018:MsgDef`.

---

## Before sending

- [x] **Verify both against their source**, not against the output alone: E at
      `pcap-import.rs:298/646-649/660/667` and `afb-evcc/src/verbs.rs:210-221`, F at
      `pcap-import.rs:294` against `:318`. All read from `fc51088` as built, 2026-08-07.
- [x] **Check F across every capture they ship**, so it is a pattern rather than one odd file — three
      ISO2 captures unaffected, the one DIN capture affected.
- [ ] **Lead with the Tesla offer, not with the code.** The decoded two lines are the whole argument
      for E; the source is only where to go afterwards.
- [ ] **Say plainly that we are not asking for DIN or for Tesla's protocol to be supported.** E is
      about replay fidelity. Conflating the two invites "we do not implement vendor protocols" as an
      answer to a question nobody asked.
- [ ] **File F on its own and offer the one-liner.** It is the kind of fix a maintainer merges in a
      minute, and bundling it with E would slow both down.
- [ ] **Credit the part they got right.** The SchemaID resolution loop at `:646-649` is correct, and
      saying so is honest rather than diplomatic — it is why E reads as a gap in the pipeline rather
      than a misunderstanding of the protocol.
