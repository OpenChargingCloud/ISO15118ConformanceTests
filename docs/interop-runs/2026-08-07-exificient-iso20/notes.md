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

**Resolved the same day — three separate causes, and two of them are ours.** See
*The six, cleared up* below.

## Nine mismatches, and two are old news

| frame | ours | theirs | delta | |
|---|---:|---:|---:|---|
| `ServiceDetailRes` (×7, one per trace) | 138 B | 95 B | 43 | value partition, probably |
| `AuthorizationReq` (PnC trace) | 913 B | 879 B | 34 | value partition, probably |
| `ACDP_ConnectRes` | 20 B | 18 B | 2 | **not a partition difference — see cause A below** |

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

`ACDP_ConnectRes` looked like a third instance and is not one: it is the other half of the element
numbering defect, cause A below. Worth noting how it read before that was known — a small, plausible
byte delta on a message whose grammar we had no reason to doubt.

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

## The six, cleared up

Three causes, not one, and the two ACDP frames turn out to share theirs with a mismatch.

### A — our global-element numbering is wrong for ACDP, and only for ACDP (2 frames)

In schema-informed EXI the document grammar enumerates the schema's **global elements sorted by
qname**. Our generated dispatch writes a "document element selector" per message, and comparing that
numbering with the sorted one across all six `-20` sets gives:

| set | ours vs EXI's order |
|---|---|
| CommonMessages, AC, DC, WPT, AC_DER_SAE | identical |
| **ACDP** | **indices 1 and 2 are swapped** |

```
ours: 0 ACDP_ConnectReq   1 ACDP_DisconnectReq   2 ACDP_ConnectRes   3 ACDP_DisconnectRes …
spec: 0 ACDP_ConnectReq   1 ACDP_ConnectRes      2 ACDP_DisconnectReq 3 ACDP_DisconnectRes …
```

Those two indices are **exactly** the two ACDP frames that did not round-trip. The consequences follow
mechanically and match the observations to the byte:

- we write `ACDP_DisconnectReq` with selector 1; a conformant decoder reads element 1 =
  `ACDP_ConnectRes`, whose type has more content than we wrote → **`Premature EOS`**;
- we write `ACDP_ConnectRes` with selector 2; it is read as `ACDP_DisconnectReq`, a shorter type →
  decodes, re-encodes to **18 B against our 20** → the mismatch reported above. So that mismatch was
  never a value-partition difference at all.

The reason our order ties differently is visible in ISO's schema:

```xml
<xs:element name="ACDP_DisconnectReq" type="ACDP_ConnectReqType"/>
<!--  <xs:complexType name="ACDP_DisconnectReqType"> … -->   <!-- commented out by ISO -->
```

`ACDP_DisconnectReq` and `ACDP_DisconnectRes` have no types of their own; they reuse
`ACDP_ConnectReqType` and `ACDP_ConnectResType`. Our numbering groups the elements that share a type,
so the pair lands adjacent instead of alphabetical. ACDP is the only `-20` set with an aliased type,
which is why it is the only set affected — and why nothing caught it: the vectors are cbV2G's, cbV2G
numbers them the same way we do, and no other codec had ever looked.

**Ours to fix**, in the app's source generator.

### B — the four WPT frames are cbV2G's grammar, not the schema's (4 frames)

Our own generated code says so, in as many words:

```csharp
if (msg.VendorSpecificDataContainer.Count > 2)
    throw new ArgumentOutOfRangeException(nameof(msg),
        "VendorSpecificDataContainer: cbV2G's grammar for this position caps this list at 2 items.");
…
    throw new ArgumentException("WPT_LF_DataPackageList cannot be encoded while
        VendorSpecificDataContainer is empty: cbV2G's grammar for this position only reaches it
        after at least one list item.", nameof(msg));
```

The schema says something else: `VendorSpecificDataContainer` is `minOccurs="0" maxOccurs="16"` and
`WPT_LF_DataPackageList` is an independent `minOccurs="0"`. So at the position where both are absent,
the schema grammar offers three events — `SE(VendorSpecificDataContainer)=0`,
`SE(WPT_LF_DataPackageList)=1`, `EE=2` — and we write **1 for the end-element**, because our state
there only knows two. EXIficient reads that 1 as a start element, looks for content that is not
there, and reports `Premature EOS`.

`grep` puts the blast radius at exactly those two comments, sixteen times each, across exactly the
four `WPT_FinePositioning*` messages — the four that failed. The working sibling
`WPT_AlignmentCheckReq` has the ordinary loop to 16 and round-trips fine.

**Ours to fix as well** — but deliberately incurred, not an oversight: matching cbV2G byte-for-byte is
the project's stated rule for the codec, and here cbV2G's grammar and ISO's schema disagree. The rule
bought byte-exactness with the stacks that share cbexigen and cost readability by everyone else. That
trade was never visible before, because until today nobody else had read a WPT frame of ours.

### C — `AC_ChargeParameterDiscoveryRes_DER`: still open, and its provenance is the lead (1 frame)

No cbV2G-grammar marker in its generated code, so not cause B; the AC_DER_SAE numbering is correct, so
not cause A. What it does have is a `source` field:

```
AC_ChargeParameterDiscoveryRes_DER    Vanaheimr.V2G.Exi (C#)    241 B
```

**Its expected bytes are our own encoder's output.** The corpus file's own header says *"READ THIS
BEFORE TRUSTING A BYTE"* and gives provenance per vector; six of the sixteen AC_DER_SAE vectors are
self-generated because no reference encoder covers the DER extensions. This is the largest of them, and
the first independent codec ever to look at it cannot read it. Five other self-generated DER vectors
passed, so "self-generated" is not by itself the fault — but a 241-byte vector with no external
provenance is exactly where one would expect to find one.

## Next

1. **Fix A** — sort global elements by qname in the generator. Small, and it makes two ACDP messages
   readable by any conformant peer. Belongs in `libs/EVSimulatorApp/`.
2. **Decide B** — this is a policy question before it is a code change: stay byte-exact with cbV2G and
   unreadable to schema-following codecs, or follow the schema and diverge from our own corpus. Worth
   raising with EVerest too, since it is their generator's grammar.
3. **Diagnose C** — no reference bytes exist, so it needs a bit-level walk against the schema.
4. **Close the `ServiceDetailRes` and `AuthorizationReq` deltas** with the substitution experiment that
   closed the `-2` one, and explain the off-by-one against the 35-character URI. These remain the only
   mismatches still attributed to the value partition; `ACDP_ConnectRes` has moved to cause A.

## Files

- [`roundtrip-results.json`](roundtrip-results.json) — verdicts, with both encodings for every frame
  that did not match.
