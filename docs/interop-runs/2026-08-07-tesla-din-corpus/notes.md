# 2026-08-07 — a DIN 70121 corpus, out of a protocol we cannot speak

**Result: 4,428 captured frames → 101 distinct → 100 round-tripped byte-exact through EXIficient, 1
defeated it. Our V2GTP framer walks all 101.**

The [handshake run](../2026-08-07-tesla-din-handshake/notes.md) earlier the same day ended by naming
what was still out of reach: *"2,215 transactions of DIN 70121 … and no schema in this project to
decode any of it."* That was true of **us**. It stopped being true of the **capture** the moment there
was an oracle in the rig that speaks DIN — V2Gdecoder ships `schemas_din/`.

| | |
|---|---|
| Material | tux-evse `afb-test/trace-logs/tesla-3-din.pcap` — a Tesla Model 3 at `DE*PNX*E12345*1` |
| Oracle | V2Gdecoder v1.1 (RISE-V2G 1.2.6 + EXIficient 1.0.4), `schemas_din` |
| Second reading | tux-evse `pcap-iso15118` (cbexigen), for the one frame the first could not read |
| Ours | conformance suite @ `0dbb71a`; **no DIN codec** — the framing layer only |
| Outcome | corpus checked in, four offline tests, one V2Gdecoder defect |

## What the capture actually is

```
4,428 V2GTP frames        2,214 each way
  101 distinct            98% of the capture is the charge loop repeating itself
```

A complete DC session: handshake, `SessionSetup`, `ServiceDiscovery`, `ServicePaymentSelection`,
`ContractAuthentication` ×3, `ChargeParameterDiscovery`, `CableCheck` ×383, `PreCharge` ×4,
`PowerDelivery`, `CurrentDemand` **×1,816**, `SessionStop`.

The distinct-frame counts are the interesting part. `CableCheckReq` occurs 383 times as **one** shape —
the car repeats the identical bytes while the station isolates. `CurrentDemandReq` has 16 shapes across
1,816 occurrences, its response 60. So the whole 885 KB capture is 101 frames and a lot of patience.

## Round-trip through EXIficient: 100 of 101

Same method as the [`-2` oracle run](../2026-08-07-v2gdecoder-oracle/notes.md): captured bytes → their
decode → their encode → compare. Note what is and is not being judged here. These are not our bytes, so
this says nothing about our encoder. It says that **an independent codec reads and reproduces real
field DIN exactly** — which makes the corpus trustworthy as ground truth, and is the precondition for
it being worth keeping.

## The one that failed, and why it is theirs

Frame 7 of the station→car direction — the `ChargeParameterDiscoveryRes`, the station's power offer.
V2Gdecoder returned this:

```xml
<ns4:SignatureValue xmlns:ns4="http://www.w3.org/2000/09/xmldsig#">EA==</ns4:SignatureValue>
```

64 bytes of a DIN response read as a 1-byte xmldsig fragment. Not a decode error — a *wrong answer*,
returned with no indication anything went wrong.

The mechanism is in their source. `dataprocess.fuzzyExiDecoded` tries `grammars[0]` (MsgDef), then
`grammars[1]` (AppProtocol), then `grammars[2]` (xmldsig), and **returns the first that does not
throw**. Nothing checks that the result fits. So when their DIN MsgDef grammar cannot decode a frame,
the xmldsig fallback is free to produce nonsense from the same bits, and does.

We had already met this once: the 3-byte `supportedAppProtocolRes` in the `-2` run, read as a
`MsgDataTypes` `Entry`. Two sightings, same mechanism, and the second one is worse — the first misfired
on an ambiguous 3-byte frame, this one on a 64-byte message whose real grammar simply is not there.

**The frame is sound.** tux-evse's cbexigen-based `pcap-iso15118` reads it without trouble:
a 10 kW station, 900 V max / 180 V min, 25 A, 1 A regulation tolerance, isolation still `invalid`
because the cable check has not run yet, and a `PMax` schedule of 10 kW over 86,400 s. Full decode in
[`param-discovery-second-reading.json`](param-discovery-second-reading.json).

That the two decoders agree everywhere else and disagree exactly here is what makes the finding
attributable rather than a shrug.

## What we can test, without a DIN codec

`V2GTP` framing is protocol-independent — the same structural point that made the handshake reachable
in this capture. Four offline tests in `Interop/TeslaDinCorpusTests.cs`:

| | |
|---|---|
| `TheCorpusIsAWholeSession` | the counts hold, and the session runs handshake to `SessionStopRes` |
| `EveryFrameIsWellFormedV2GTP` | header parses, declared length *is* the length that follows, payload type is mainstream or handshake |
| `TheFramerWalksARealSessionStreamWithoutLosingItsPlace` | all 101 concatenated into one stream and read back one at a time — the boundary between frames is never signalled, only computed, so one off-by-one desynchronises everything after it |
| `TheFrameThatDefeatedTheOracleIsStillInTheCorpus` | recorded as `grammar-miss(SignatureValue)`, not quietly dropped |

Every framing guard we have had until now only ever been run over frames this project produced. These
came from two vendors who never heard of us.

## Reproducing

```bash
bash tools/interop-v2gdecoder/setup.sh
python3 tools/interop-v2gdecoder/din-corpus.py <path>/tesla-3-din.pcap \
    --out ISO15118ConformanceTests.Simulation/Vectors/Din.tesla-session.corpus.json --roundtrip
```

The DIN schema set must be staged as `./schemas` in `--schemas` (default `~/v2gdec/din`); V2Gdecoder
resolves grammars from the working directory. **Do not let it detect the schema set.** With the ISO-2
set the DIN `SessionSetupReq` does not fail — it comes back as an ISO-2 `WeldingDetectionReq` with
`EVReady false`, `EVErrorCode Reserved_B`, `EVRESSSOC 0`. Entirely plausible, entirely wrong. The two
schema sets are pinned explicitly for that reason.

## What is still out of reach — and it is less than it was

The codec. We still cannot decode a single one of these messages ourselves, and nothing here changes
that. What changed is what stands behind the sentence: the session is no longer unreadable, it is read
by two independent decoders that agree; the frames are checked in byte-exact; and if a DIN codec is
ever written here, the first thing to point it at is a real car's session rather than something we made
up. Start with frame 7.

## Files

- [`../../../ISO15118ConformanceTests.Simulation/Vectors/Din.tesla-session.corpus.json`](../../../ISO15118ConformanceTests.Simulation/Vectors/Din.tesla-session.corpus.json)
  — the 101 distinct frames, with direction, first index, repetition count, message name and verdict.
- [`param-discovery-second-reading.json`](param-discovery-second-reading.json) — the frame V2Gdecoder
  missed, read by cbexigen.
