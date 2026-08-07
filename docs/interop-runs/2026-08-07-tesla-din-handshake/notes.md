# 2026-08-07 — the Tesla DIN capture: what a protocol we cannot speak still had to say

`tesla-3-din.pcap` was the last unused capture in tux-evse's `trace-logs/`, and it had been written off
in three documents with the same sentence: *nothing here speaks DIN 70121, which is a capability
question rather than a scheduling one.* That is still true of the session. It turned out not to be true
of the **handshake**.

| | |
|---|---|
| Material | [tux-evse](https://github.com/tux-evse/iso15118-simulator-rs) `afb-test/trace-logs/tesla-3-din.pcap` — a real **Tesla Model 3** at a real charge point |
| Ours | conformance suite @ `fa64d88` |
| Method | offline: no rig, no session, no counterparty process — the capture read, two frames decoded by our own codec, and our SECC fed the car's real bytes |
| Outcome | **The capture is usable after all**, and it carries a protocol offer this project could not have written for itself |

Artifacts: `frames.head.log` (the first frames per direction), `sap-decoded.txt`, and the fixture
`ISO15118ConformanceTests.Simulation/Interop/TeslaDinHandshakeTests.cs`.

## Why any of it is reachable

The rest of the capture is DIN 70121 — a different grammar, and one this project has no schemas for.
The `SupportedAppProtocol` handshake is the exception, and not by accident: its schema
(`urn:iso:15118:2:2010:AppProtocol`) is **its own document type, deliberately protocol-independent**,
because it is the mechanism by which a car and a station agree which protocol to speak. It cannot
presuppose one. So our codec reads it whatever the offer names, and the first two frames of a DIN
session are ordinary test material.

That is a small structural point with a practical consequence: **every capture is at least partly
usable, whatever protocol it goes on to speak.** We had been treating protocol support as all-or-nothing.

## What the car offered

```
document order 1:  schemaId 1  priority 2  v2.0  urn:din:70121:2012:MsgDef
document order 2:  schemaId 2  priority 1  v0.7  urn:tesla:din:2018:MsgDef
```

**A vendor-proprietary protocol, at the highest priority, from a car in the field.**
`urn:tesla:din:2018:MsgDef`, version 0.7. Every offer this project had ever seen or built named
protocols from the standards, so the case the fallback exists for — *the entry the car wants most is
one nobody else can speak* — had never actually been on our wire.

The station it met (`DE*PNX*E12345*1`, a German charge point) did the right thing: declined the entry it
did not know and answered `OK_SuccessfulNegotiation` with **SchemaID 1**, the standard DIN entry. So
this is ordinary field behaviour, not an exotic capture — which is what makes it worth pinning.

## The quieter half, and the one closer to home

**Document order, SchemaID order and Priority order all disagree in this offer.**

Our own EVCC builds them to coincide: entry 0 gets SchemaID 1 and Priority 1, entry 1 gets SchemaID 2
and Priority 2 (`SapOffer`). That coincidence is precisely what let our SECC answer a **literal**
SchemaID 1 for months without any test noticing — the defect found on 2026-08-03 while building the
both-protocol offer for EVerest's mux, and one of the cleanest examples in
[`assumed-values-sweep.md`](../../assumed-values-sweep.md) of our own EVCC supplying what our own SECC
assumed.

This car takes the three apart. Its preferred entry is **second** in the document and carries
**SchemaID 2**; SchemaID 1 is the one it would rather not use. A station that conflates any two of the
three orderings is wrong here — and no offer this project can construct for itself would show it,
because our constructor is the very thing that ties them together.

It is worth being exact about what this does and does not prove. Our negotiation already orders by
`Priority` and already echoes the accepted entry's `SchemaID`; it was fixed in August and is correct.
What was missing was **material that could tell a correct implementation from a lucky one**. That is
what arrived here.

## What our station does with it

Fed the car's real frame, our SECC answers `Failed_NoNegotiation` — the only correct answer, since we
speak neither namespace — and **puts it on the wire** before ending the session. That is the same
distinction the sequence-guard fix turned on a day earlier: a refusal a car can read, versus a socket
that simply dies.

Four tests came out of it, all offline and all in the ordinary suite:

| | |
|---|---|
| `TheCarOffersAProprietaryNamespaceAtPriorityOne` | the offer decodes; the vendor namespace, its priority, and the three disagreeing orderings |
| `TheStationAnsweredWithTheSchemaIdOfAnEntryTheCarOffered` | the real charge point's answer, checked against the real offer |
| `OurStationRefusesTheRealOfferOnTheWire` | `Failed_NoNegotiation`, sent rather than implied |
| `AnUnknownTopPriorityEntryDoesNotHideTheOneBelowIt` | the same shape with a namespace we do speak — the fallback, and the echoed SchemaID |

The fourth is a construction, and says so: the capture cannot test the fallback against us directly,
because we refuse both of its namespaces. It reproduces the *shape* the Tesla established — proprietary
at priority 1, a standard protocol at priority 2, SchemaIDs deliberately not 1 and 2 — which is only
worth writing because a real car showed the shape occurs.

## How to reproduce

```bash
# the two frames, out of the capture, with no scapy and no tshark
python3 tools/interop-tux-evse/v2gtp-from-pcap.py path/to/tesla-3-din.pcap 2

# then the offline fixture, which carries the bytes inline
dotnet test -c Release ISO15118ConformanceTests.Simulation \
  --filter "FullyQualifiedName~TeslaDinHandshakeTests"
```

## What is still out of reach

The session. 2,215 transactions of DIN 70121 — `contract_authentication_req`, `param_discovery_req`,
a cable check, two pre-charges, twenty-one `current_demand_req` and a session stop — and no schema in
this project to decode any of it. Their `pcap-iso15118` converts it happily (`proto:Din`), so the
scenario file exists and could drive their injector against a DIN station; there just is not one here.

That remains a capability question, and this run does not change it. What it changes is the sentence
around it: **the capture is no longer unused.**

> **Later the same day.** Less of it is out of reach than this section assumed. The *codec* still is —
> we decode none of it ourselves — but the session no longer had to be read by us to be usable. Once
> V2Gdecoder was in the rig with its `schemas_din` set, the 4,428 frames collapsed to 101 distinct ones
> and 100 of them round-tripped through EXIficient byte-exact; the 101st is a defect in that tool, and
> tux-evse's converter reads it. The frames are now checked in as
> `Vectors/Din.tesla-session.corpus.json` and our V2GTP framer is tested against them.
> See [`2026-08-07-tesla-din-corpus`](../2026-08-07-tesla-din-corpus/notes.md).
