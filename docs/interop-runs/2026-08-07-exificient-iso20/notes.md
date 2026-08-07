# 2026-08-07 — ISO 15118-20 gets a second opinion, and it disagrees fifteen times

**Result: 347 frames, 332 byte-exact through EXIficient, 9 mismatches, 6 it cannot read. 3.7 seconds.**

Until today `-20` had exactly one byte oracle: **libcbv2g**, which is also our vector generator and also
what EVerest and tux-evse encode with. Every `-20` byte agreement this project could show was agreement
with a single implementation. [`tools/interop-exificient/`](../../../tools/interop-exificient/README.md)
ends that — EXIficient is a general schema-informed EXI processor, so it can be handed ISO's own `-20`
schemas directly, and it shares no line with cbexigen.

| | |
|---|---|
| Oracle | EXIficient 1.0.4, out of the V2Gdecoder jar already on the rig; OpenJDK 25 |
| Schemas | ISO's `-20` set, read from the app submodule; nothing copied into this repository |
| Method | our bytes → their decode → their encode → compare |
| Corpus | 8 vector files (107 frames) + 7 session traces (240 frames) |
| Ours | conformance suite @ `48c9871` |

## What was agreed

**332 of 347.** Both -20 control modes, AC and DC, EIM and Plug & Charge, five complete sessions,
signed messages and certificate chains — read by an independent codec and written back to the same
octets. That is the first `-20` byte evidence this project has that is not cbV2G agreeing with itself.

## What was not — and the one that matters

**Six frames a second codec cannot read at all**, failing with `Premature EOS found while reading data`
— EXIficient running out of bits before its grammar was satisfied:

| frame | set | size |
|---|---|---|
| `WPT_FinePositioningSetupReq` | WPT | 23 B |
| `WPT_FinePositioningSetupRes` | WPT | 23 B |
| `WPT_FinePositioningReq` | WPT | 19 B |
| `WPT_FinePositioningRes` | WPT | 19 B |
| `ACDP_DisconnectReq` | ACDP | 18 B |
| `AC_ChargeParameterDiscoveryRes_DER` | AC_DER_SAE | 241 B |

Note where they land. **WPT, ACDP and the DER extensions are exactly the message sets the interop matrix
marks as `codec only — no independent stack implements session state machines for them`.** They have
never been judged by anything except the generator that produced their expected bytes. The first time an
independent codec looks at them, six frames do not read.

**This is not yet a verdict on who is wrong.** Two readings fit and we have not separated them: our
encoder writes those messages incorrectly, or EXIficient's grammar for them differs from cbexigen's.
The frames are small enough (18–23 B for five of the six) that a bit-level comparison against the
schema should settle it quickly, and that is the next piece of work. What is already certain is that
the agreement these sets had was never independent.

## Nine mismatches, and two are old news

| frame | ours | theirs | delta |
|---|---:|---:|---:|
| `ServiceDetailRes` (×7, one per trace) | 138 B | 95 B | 43 |
| `AuthorizationReq` (PnC trace) | 913 B | 879 B | 34 |
| `ACDP_ConnectRes` | 20 B | 18 B | 2 |

The first two have the shape of the finding already recorded for `-2` in
[`ExiStringTableTests`](../../../ISO15118ConformanceTests.Simulation/Interop/ExiStringTableTests.cs):
EXIficient uses the EXI value partition for a string that occurs twice in a document, our encoder is
deliberately miss-only and writes the literal again. `ServiceDetailRes` carries repeated service
parameter names — `Connector` appears literally in our bytes and not in theirs. `AuthorizationReq` is a
signed message, and its Signature repeats the canonicalization URI exactly as the `-2` one did.

**Not confirmed to the byte, unlike the `-2` case.** There the substitution experiment closed it: make
the repeated value unique at the same length and their encoder produced our length exactly. Here the
delta is 34 while our canonicalization URI is 35 characters, so something is off by one and the same
experiment has not been run. Recorded as "the same shape", not as the same finding.

`ACDP_ConnectRes` — 2 bytes, diverging at offset 18 of 20 — is neither of those and is unexplained.

## The afternoon this cost, and the one line that fixed it

Worth writing down, because it wasted hours and the error message actively misleads.

Every attempt to run this failed with `Problem occured while building XML Schema Model` — Xerces
reporting it could not read `xmldsig-core-schema.xsd`, a file sitting next to the schema that imports
it, byte-identical to its source, well-formed, readable by the shell at that moment. Not deterministic:
the identical command against the identical file failed **0 times in 30** in one window and **12 in 12**
an hour later.

Excluded by measurement: memory, file descriptors, JVMs left running, a full `/tmp`, a private
`java.io.tmpdir`, invocation rate, corruption in the copy off the Windows mount, absolute versus
relative schema paths, `file:` URIs, the working directory, input and output paths, `accessExternalSchema`,
a fresh WSL distro, and all four JDKs on the box.

The answer was in the first eighty bytes of the file the whole time:

```xml
<!DOCTYPE schema PUBLIC "-//W3C//DTD XMLSchema 200102//EN"
                        "http://www.w3.org/2001/XMLSchema.dtd">
```

W3C's xmldsig schema — pulled in by ISO's `V2G_CI_CommonTypes.xsd`, and therefore by **every** `-20`
message set — carries a DOCTYPE. Xerces fetches that DTD from `w3.org` on every grammar build, and W3C
has rate-limited exactly that traffic for years. A corpus run is hundreds of requests in minutes; once
they start being refused, the failure surfaces as a complaint about the **local** file, naming a path
that is perfectly fine.

It explains everything that looked inexplicable, including the one clue that was there all along: the
only schema that *never* failed is SupportedAppProtocol — the one with no imports, and so no DOCTYPE
anywhere in its chain.

The fix is an `XMLEntityResolver` that resolves every `http(s):` entity to an empty stream, in
[`Roundtrip20.java`](../../../tools/interop-exificient/Roundtrip20.java). Its declarations describe the
XML Schema language and are not needed to build an XSModel; the declarations that matter live in the
DOCTYPE's internal subset, which stays. A conformance harness should refuse to touch the network during
schema construction anyway.

Wall clock for the whole corpus went from *never finishing* to **3.7 seconds**.

Two things I got wrong along the way and have corrected in place rather than quietly: I recorded
"relative schema path from the schema's own directory" as the fix after measuring 0/30 against 21/30,
and the same shape later failed 10/10; and I built a retry loop around the symptom, which reached 3,663
retries in one run and still did not finish. Both are gone.

## Reproducing

```bash
bash tools/interop-v2gdecoder/setup.sh      # the jar; needs a JDK for the driver, not just a JRE
python3 tools/interop-exificient/roundtrip20.py \
    libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EXI_Tests/Vectors/Iso15118_20.*.vectors.json \
    ISO15118ConformanceTests.Simulation/Vectors/Session.iso20-*.trace.json
```

## Next

1. **The six unreadable frames.** Five are under 25 bytes; a bit-level walk against the schema will say
   whether the fault is ours or EXIficient's. This is the piece with real conformance weight.
2. **Close the `ServiceDetailRes` and `AuthorizationReq` deltas** with the substitution experiment that
   closed the `-2` one, and explain the off-by-one against the 35-character URI.
3. **`ACDP_ConnectRes`** — two bytes, unexplained.

## Files

- [`roundtrip-results.json`](roundtrip-results.json) — verdicts, with both encodings for every frame
  that did not match.
