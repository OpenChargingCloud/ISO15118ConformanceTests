# 2026-08-07 — ISO 15118-20 gets a second opinion, and it disagrees fifteen times

**Result: 347 frames, 332 byte-exact through EXIficient, 9 mismatches, 6 it cannot read. 3.7 seconds.**

**Where it ended up: 339 byte-exact, 8 mismatches, nothing unreadable.** All three causes below are
settled — one was a defect and is fixed, two were decisions and were taken on 2026-08-08
([notes](../2026-08-08-schema-conformant-acdp-wpt/notes.md)). The eight remaining mismatches all have
the single value-partition cause already recorded for `-2`.

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

**Resolved the same day — three separate causes, all three ours.** Two are decisions (we match cbV2G
where cbV2G and the schema disagree) and one was a plain defect, now fixed: `minOccurs="2"` forces the
second occurrence of a particle, and a forced occurrence is not a choice. See *The six, cleared up*
below. The DER frame reads; the other five stand as they are, on purpose.

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
byte delta on a message whose grammar we had no reason to doubt. It disappeared the moment A was
decided, which is the confirmation: **eight mismatches now, and all eight are the value partition.**

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

To see *where* a frame stops making sense rather than only that it does, walk it event by event:

```bash
python3 tools/interop-exificient/walk20.py \
    libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EXI_Tests/Vectors/Iso15118_20.AC_DER_SAE.vectors.json \
    AC_ChargeParameterDiscoveryRes_DER AC_ChargeParameterDiscoveryReq_DER
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

> **Correction, made when the fix was attempted.** This section first said *"ours to fix"*, as though
> it were an oversight. It is not. The grouping is written into the generator with its reasoning and
> was verified against cbV2G's `encode_iso20_acdp_exiDocument` when it was implemented, and the app
> already carries a stated rule for exactly this situation — *"cbV2G byte-exact match stays the
> authoritative conformance oracle"* (`tools/exificient-ref/README.md`). So **A is the same policy as
> B, not a different kind of thing**, and calling it a bug was wrong.
>
> What was done instead: the alternative is now buildable rather than only arguable. The default is
> unchanged, and `<ExiDocumentElementOrder>ExiSorted</ExiDocumentElementOrder>` selects the
> specification order. Building `WWCP_ISO15118_20.ACDP` with it produces
> `ConnectReq=0, ConnectRes=1, DisconnectReq=2, DisconnectRes=3`, and the byte after the EXI header for
> `ACDP_DisconnectReq` becomes `08` — the byte EXIficient writes for that message. Details in the app's
> `tools/exificient-ref/README.md`; the ordering rule itself is unit-tested on a synthetic schema in
> `GeneratorDocumentOrderTests`.
>
> Which order is *correct* still needs the EXI 1.0 text on the document grammar, which is not settled
> here. What is settled is what the disagreement costs, and that either encoding is now a build
> property away.

> **Settled the next day, and the correction above was itself half wrong.** A and B are indeed the same
> *kind* of question — but neither is a matter of taste. EXI 1.0 Second Edition §8.5.1 gives the
> `DocContent` grammar one `SE` production per global element qname, "sorted lexicographically, first by
> local-name, then by uri", with no exception for elements sharing a type. Confirmed independently by
> having EXIficient encode a three-element schema in exactly the ACDP shape: indices 0/1/2, where our
> grouping gives 0/2/1. cbexigen departs from the specification here; we were copying the departure.
> **Decided 2026-08-08 to follow the specification** — see
> [`2026-08-08-schema-conformant-acdp-wpt`](../2026-08-08-schema-conformant-acdp-wpt/notes.md).

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

> **Decided 2026-08-08: follow the schema.** Note what this one costs beyond readability — cbexigen's
> grammar cannot express two perfectly valid documents at all (a third list item, and the suffix
> without a preceding item), so reproducing it made our encoder refuse messages ISO permits. Switched
> together with A; see
> [`2026-08-08-schema-conformant-acdp-wpt`](../2026-08-08-schema-conformant-acdp-wpt/notes.md).

### C — `AC_ChargeParameterDiscoveryRes_DER`: a forced occurrence is not a choice (1 frame)

**Ours, a plain defect, and fixed.** Unlike A and B this is not a fork between two defensible readings:
the schema and the EXI specification agree, and we were simply wrong.

The lead was its provenance. No cbV2G-grammar marker in its generated code, so not cause B; the
AC_DER_SAE numbering is correct, so not cause A. What it did have is a `source` field:

```
AC_ChargeParameterDiscoveryRes_DER    Vanaheimr.V2G.Exi (C#)    241 B
```

**Its expected bytes were our own encoder's output.** Six of the sixteen AC_DER_SAE vectors are
self-generated because no reference encoder covers the DER extensions, and this is the largest of them.
The other five passed, so "self-generated" was not by itself the fault — but a 241-byte vector with no
external provenance is exactly where one would expect to find one.

#### Where it goes wrong

`Roundtrip20` only reports *that* a frame cannot be read. `Premature EOS` says EXIficient ran out of
bits, not where — and a frame fails that way both when the first event code was wrong and everything
after it was noise, and when 240 of 241 bytes were fine. So
[`Walk20.java`](../../../tools/interop-exificient/Walk20.java) drives EXIficient's event API instead of
its SAX bridge and prints every event as it is decoded. The last agreement is unambiguous:

```
SE(CurveDataPoints)
  SE(CurveDataPoint)                     <- first point: xValue (0,1), yValue (0,2) — correct
  EE(CurveDataPoint)
  SE(CurveDataPoint)                     <- second point
    SE(xValue) SE(Exponent) CH "-64"     <- should be 0
                SE(Value)  CH "1"        <- should be 3
                  SE(⁂ @ 4 ` @ ؀ࠀ@ဘ ツ)  <- and from here, noise
```

`Exponent` is an 8-bit code; we wrote `1000 0000` (0 biased by −128) and EXIficient read `0100 0000`
(−64). It is reading our bits **one position early**, from the second `CurveDataPoint` onwards. We had
written one bit too many, and only there.

The schema says why:

```xml
<xs:element name="CurveDataPoint" type="DataTupleType" minOccurs="2" maxOccurs="10"/>
```

EXI unrolls a bounded particle into one grammar state per occurrence. Below `minOccurs` the state has a
**single** production — `SE(item)`, because ending the element there would be invalid — so its event
code is one bit. Only at `minOccurs` and above does the state also offer the end-element, widening the
code to two. Our generator gave every occurrence after the first the wide code. Right for
`minOccurs≤1`; one bit too many for anything else.

#### Confirmed without ISO's schemas

cbexigen cannot generate the DER schemas at all, so there is no third opinion on these particular
bytes. But the *rule* does not need ISO: ask EXIficient to encode a synthetic
`minOccurs="2" maxOccurs="10"` schema and read the bits it produces.

| document | body bits | second `SE` | list end |
|---|---|---|---|
| `minOccurs="1"`, 2 items | `0 0 0·00000000·0 00 0·00000000·0 01` | **2 bits** | 2-bit EE |
| `minOccurs="2"`, 2 items | `0 0 0·00000000·0 0 0·00000000·0 01` | **1 bit** | 2-bit EE |
| `minOccurs="2"`, 3 items | one more item at **2 bits** | | |
| either, 10 items (`maxOccurs`) | body ends `…0` | | **1-bit EE** |

Both schemas, all four lengths, fit to the bit with no slack: 120 bits exactly for the ten-item
`minOccurs="2"` document. That also settles a second, smaller thing the generator only handled as a
`maxOccurs=2` special case — **a list filled to its maximum ends with a one-bit end-element for any
maximum**, because that state has nothing left to offer. The special case was the general rule.

#### The blast radius, and why the corpus never saw it

ISO 15118 has exactly **five** particles with `minOccurs≥2`:

| particle | set | `minOccurs` / `maxOccurs` |
|---|---|---|
| `CurveDataPoint` | AC_DER_IEC | 2 / 10 |
| `CurveDataPoint` | AC_DER_SAE | 2 / 10 |
| `TxSpecData` | WPT | 2 / 255 |
| `RxSpecData` | WPT | 2 / 255 |
| `PulseSequenceOrder` | WPT | 2 / 255 |

Not one is in `-2`, CommonMessages, AC, DC or ACDP. All five are in message sets no reference encoder
covers: cbexigen cannot generate the DER schemas, and the cbV2G WPT corpus deliberately leaves
`LF_SystemSetupData` absent, which is where all three WPT ones live. So **no reference bytes exist for
any of them** — the vectors that exercise them are our own output, and the WPT ones are covered by
self-consistency round trips, which by construction cannot catch an encoder and decoder that are wrong
together. That is the whole reason a bug this basic survived: a corpus can only catch what something
else also encoded.

It also means there was nothing to stay byte-exact with, so unlike A and B this needed no switch.

#### The fix

`CodecEmitter.ForcedOccurrences` in the app: occurrences below `minOccurs` take the one-bit code, in all
four places that emit a list (sole child, run particle, run tail, and the self-loop with a following
particle) and in their four decode counterparts. The `maxOccurs=2` terminator special case became the
general one. Everything with `minOccurs≤1` is emitted character for character as before — asserted, in
`GeneratorForcedOccurrenceTests`, because the value of the vector corpus rests on those bytes not moving.

Only one vector in the whole corpus changes: `AC_ChargeParameterDiscoveryRes_DER`, still 241 B, its
`note` recording why. The suite stays green, and EXIficient now round-trips it byte-exact.

**332 → 333 of 347.** The remaining five are A (one ACDP) and B (four WPT), both policy, both unchanged
on purpose.

## Next

1. ~~Fix A~~ — **done as a switch first, then decided.** See both corrections above: A is the same kind
   of question as B, and the EXI 1.0 text settles it against cbexigen.
2. ~~**Decide A and B together**~~ — **done 2026-08-08: follow the schema.** Both are build properties
   (`ExiDocumentElementOrder`, `ExiParticleGrammar`), and the ISO codecs now set them in
   `Directory.Build.props`; the cbexigen bytes remain one property away. Still worth raising with
   EVerest, and now as two concrete defect reports rather than as a difference of opinion.
3. ~~Diagnose C~~ — **done, and fixed.** A forced occurrence (`minOccurs="2"`) took a two-bit
   start-element code where the grammar has a one-bit one. No switch: no reference encoder has ever
   written any of the five affected particles, so there was nothing to stay byte-exact with.
   `AC_ChargeParameterDiscoveryRes_DER` now round-trips.
4. **Check the other four `minOccurs≥2` particles against EXIficient** — `TxSpecData`, `RxSpecData` and
   `PulseSequenceOrder` are only reachable behind WPT's `LF_SystemSetupData`, which no vector populates,
   and `CurveDataPoint` in AC_DER_IEC is never given a curve. The fix is the same code path the DER SAE
   frame proves, but a vector that actually carries one would be better than an inference.
5. **Close the `ServiceDetailRes` and `AuthorizationReq` deltas** with the substitution experiment that
   closed the `-2` one, and explain the off-by-one against the 35-character URI. These remain the only
   mismatches still attributed to the value partition; `ACDP_ConnectRes` has moved to cause A.

## Files

- [`roundtrip-results.json`](roundtrip-results.json) — verdicts, with both encodings for every frame
  that did not match.
