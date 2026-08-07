# interop-v2gdecoder — a second EXI oracle, independent of cbV2G

Our byte oracle for ISO 15118-2 is **libcbv2g** (`libs/EVSimulatorApp/libs/WWCP_ISO15118/tools/cbv2g-ref`),
and that is a problem the interop matrix cannot solve by itself: EVerest and tux-evse are cbexigen-derived
too, so agreeing with them is agreeing with our own generator's lineage. Only Josev has ever given us an
independent byte judgement, and only over the frames Josev happened to send.

**V2Gdecoder** ([FlUxIuS/V2Gdecoder](https://github.com/FlUxIuS/V2Gdecoder)) closes that gap for -2. It is
RISE-V2G plus **EXIficient** — different codec, different language, different author — and unlike a live
stack it encodes and decodes *anything*, on demand, offline.

We came to it sideways. The question was whether [ChargePoint's wireshark-v2g](https://github.com/ChargePoint/wireshark-v2g)
could validate our parser; it cannot, because its dissector is a pretty-printer over libcbv2g pinned at
`03350be048b3` — the very commit `cbv2g-ref` pins. But its `tools/docker/decoder` points at V2Gdecoder,
and *that* is the oracle.

## What it checks

For every frame, the full circle through the other implementation:

```
our bytes  --(their decode)-->  XML  --(their encode)-->  their bytes  ==?  our bytes
```

No XML-to-model mapping is needed, which is what makes it cheap — the same shape as
`regenerate-appprotocol-vectors.py` uses against cbV2G. Three outcomes, all informative:
**decode-fail** (they cannot read what we wrote), **mismatch** (they read it and write it back
differently — EXI is not canonical, so this is a byte diff to look at, not automatically a defect),
and **ok**.

## Running it

Needs a JRE (tested: OpenJDK 21) and network for the first fetch. No C toolchain, no Docker.

```bash
bash tools/interop-v2gdecoder/setup.sh
python3 tools/interop-v2gdecoder/roundtrip.py \
    libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EXI_Tests/Vectors/Iso15118_2.vectors.json \
    ISO15118ConformanceTests.Simulation/Vectors/Session.iso2-*.trace.json
```

It takes either a codec vector file (`vectors[].expectedHex`, a bare EXI document) or a session trace
(`exchanges[].{request,response}.frame`, V2GTP-framed — the 8-byte header is stripped). Budget about two
JVM starts per frame, ~1 s each.

This is a tool, not part of `dotnet test`: the offline run must stay green without Java or network. What
the runs *found* is checked in as ordinary offline tests — `Interop/ExiStringTableTests.cs`.

## Scope, and one trap

V2Gdecoder ships schemas for SupportedAppProtocol, ISO 15118-2:2013 and DIN 70121. **Not -20** —
RISE-V2G predates it. `roundtrip.py` skips -20 inputs by protocol rather than reporting 30 decode
failures that say nothing about us.

Their CLI writes its result to **stdout — and so does everything else**. Under a modern JVM log4j opens
with `WARNING: sun.reflect.Reflection.getCallerClass is not supported`, and a parse failure prints
`[Fatal Error] …` there too, *with a zero exit code*. The return code is therefore worthless and the
payload has to be recognised by shape; `Oracle._extract` does that, and the first version of this tool
reported 39 spurious encode failures before it did.

Their decoder is also *fuzzy*: it tries grammars until one parses. On a 3-byte
`supportedAppProtocolRes` that misfires — it reads our frame as a `MsgDataTypes` `Entry` and re-encodes
56 bytes that its own decoder then cannot read. A tool artefact, and the only one seen in 147 frames.

## What the first run found — 2026-08-07

186 frames — 39 cbV2G `-2` vectors, 17 SAP vectors, and 130 frames across the five `iso2` session
traces. **183 byte-exact, 3 mismatches, 0 decode failures.** Write-up:
[`docs/interop-runs/2026-08-07-v2gdecoder-oracle/notes.md`](../../docs/interop-runs/2026-08-07-v2gdecoder-oracle/notes.md).

The two real mismatches are both signed PnC requests, both short by exactly 35 bytes, and 35 is the
length of `http://www.w3.org/TR/canonical-exi/` — the one value occurring twice under the attribute
name `Algorithm`. EXIficient writes the second occurrence as an EXI value-partition hit; we write the
literal. Our miss-only encoder is a documented decision, so that half is not news. What is: **no cbV2G
vector repeats an `Algorithm` value**, so 39/39 agreement never tested the partition at all — and until
this run no foreign encoder had ever handed our decoder a real hit to read. It reads both, back to our
own octets.
