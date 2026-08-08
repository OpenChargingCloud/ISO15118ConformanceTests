# Draft report to EVerest (libcbv2g / cbexigen) — three generated grammars, and one type that cannot be encoded at all

Status: **draft, not sent.** Observed 2026-08-07/08 against **libcbv2g `03350be048b3`** — which is
still `HEAD` of `EVerest/libcbv2g` as of 2026-08-08, so none of this is already fixed upstream — with
ISO 15118-20 schemas as published by ISO, cross-read with **EXIficient 1.0.4** on OpenJDK 25. Every
source citation below was re-read against that checkout on 2026-08-08. Post under your own name; see
*Before sending* at the bottom.

Three observations, **A**, **B** and **C**. They are separate filings — three different generated
grammars, three independent fixes — reported together because one run turned up all three and because
**B hides C**: an encoder cannot reach C without first losing data to B.

If only one of them gets attention, make it **B**. A and C fail loudly; B returns success and drops a
field.

## First, what works

We use cbV2G as this project's reference encoder for ISO 15118-20 and have done for months; our whole
vector corpus is generated from it. On 2026-08-07 we pointed a second, unrelated codec at that corpus
for the first time — EXIficient, a general schema-informed EXI processor from the other implementation
lineage — and **332 of 347 frames round-tripped byte-for-byte**. Both control modes, AC and DC, EIM and
Plug & Charge, five complete sessions, signed messages and certificate chains.

That is the headline: the overwhelming majority of what cbexigen generates agrees to the octet with an
implementation that shares none of its code. Six frames did not, in three causes. One was a defect of
our own and is fixed. The other two are A and B. C is something else again — a type we could never get
cbV2G to encode at all, which is why our corpus has a hole rather than a mismatch there.

**We have changed our own side.** Since 2026-08-08 our ISO 15118-20 codecs no longer reproduce A or B;
they follow ISO's schema and the EXI specification, and six of our vectors moved accordingly. We
mention it because it is context you are owed, and because it is why A below is written as a question
rather than a verdict: we reproduced your numbering deliberately for two years, and if there is a
rationale we cannot see from the generated output, we would rather change back than be right.

---

# Issue A — the document grammar groups global elements that share a named type

**Title:** `encode_iso20_acdp_exiDocument` numbers global elements by type-grouping rather than by
qname, so two ACDP messages swap identity against a specification-built processor

**Where:** `lib/cbv2g/iso_20/iso20_ACDP_Encoder.c`, `encode_iso20_acdp_exiDocument`.

## Summary

EXI 1.0 Second Edition §8.5.1 builds the `DocContent` grammar with one `SE` production per global
element declared in the schema, over the qnames *sorted lexicographically, first by local-name, then by
uri*. There is no provision for elements that share a named type.

The generated encoder assigns its 6-bit document element code in a different order — the two elements
sharing `ACDP_ConnectReqType` land adjacent:

| element | EXI §8.5.1 | `iso20_ACDP_Encoder.c` |
|---|---:|---:|
| `ACDP_ConnectReq` | 0 | 0 |
| `ACDP_ConnectRes` | 1 | **2** |
| `ACDP_DisconnectReq` | 2 | **1** |
| `ACDP_DisconnectRes` | 3 | 3 |

ACDP is the only ISO 15118 message set where this can show, because it is the only one where two global
elements share a type — ISO commented out `ACDP_DisconnectReqType`/`ResType` and pointed the elements at
the Connect types:

```xml
<xs:element name="ACDP_DisconnectReq" type="ACDP_ConnectReqType"/>
<!--  <xs:complexType name="ACDP_DisconnectReqType"> … -->   <!-- commented out in ISO's schema -->
```

## Why this one is worth your time — shown with your own bytes

Swapping two document element codes does not fail symmetrically. Here are the two messages as
**libcbv2g itself encodes them** (from `tools/cbv2g-defect-probe/` in this repository, so nothing of
ours is in the input), each then handed to EXIficient:

```
ACDP_ConnectRes     cbV2G writes code 2   8008040000000000000000080e2cfaa062000108   (20 B)
                    cbV2G reads it back as ACDP_ConnectRes
                    EXIficient reads it as ACDP_DisconnectReq        <- clean decode, wrong message

ACDP_DisconnectReq  cbV2G writes code 1   8004040000000000000000080e2cfaa06200       (18 B)
                    cbV2G reads it back as ACDP_DisconnectReq
                    EXIficient: Premature EOS                        <- loud failure
```

The second row is the harmless one: `ACDP_DisconnectReq`'s code 1 is read as `ACDP_ConnectRes`, a type
with more content than was written, so the decoder runs out of bits and says so.

**The first row is the one to worry about.** `ACDP_ConnectRes` written with code 2 is read as
`ACDP_DisconnectReq`, a shorter type, so it decodes cleanly — as a different message, with nothing
anywhere to report it. A peer would act on a disconnect request it was never sent.

Note the middle line of each: cbV2G reads its own bytes back correctly. The encoder and decoder agree
with each other perfectly, which is precisely why this cannot be found by round-tripping and was not
found for as long as cbexigen-derived stacks only talked to each other.

## Reproduced without ISO's schemas

So that this is about the rule rather than about one file — three global elements, the first and third
sharing a type, the second sorting between them:

```xml
<xs:element name="Alpha"   type="SharedType"/>
<xs:element name="Bravo"   type="BravoType"/>
<xs:element name="Charlie" type="SharedType"/>
```

Ask EXIficient to encode `<Alpha/>`, `<Bravo/>` and `<Charlie/>` against that schema and read the two
bits after the one-byte EXI header: **0, 1, 2**. Plain lexicographic order; the shared type groups
nothing.

## Suggested fix

Sort the global element declarations by qname (local-name, then uri) when emitting the document
grammar, independently of their type. Offered rather than asserted — see the note above about why we
would like to hear the rationale if there is one.

---

# Issue B — the mid-sequence particle grammar contradicts its own input schema

**Title:** `WPT_FinePositioning*` grammars cap a `maxOccurs="16"` list at two items and hide the
following optional particle from the empty state

**Where:** `lib/cbv2g/iso_20/iso20_WPT_Encoder.c`,
`encode_iso20_wpt_WPT_FinePositioningReqType`, grammar ids **178 / 179 / 180** (and the
corresponding ids in the three sibling types).

## Summary

ISO's schema, for all four `WPT_FinePositioning{,Setup}Req/ResType`:

```xml
<xs:element name="VendorSpecificDataContainer" type="…" minOccurs="0" maxOccurs="16"/>
<xs:element name="WPT_LF_DataPackageList"      type="…" minOccurs="0"/>
```

Two independently optional particles, the first repeating up to sixteen times. The generated grammar:

```
generated  id 178 (no items):   SE(list)=0                 EE=1
           id 179 (one item):   LOOP=0  SE(LF list)=1      EE=2
           id 180 (two items):          SE(LF list)=0      EE=1   <- no third item, ever

schema     state A (no items):  SE(list)=0  SE(LF list)=1  EE=2
           state B (n items):   LOOP=0      SE(LF list)=1  EE=2   <- loops to maxOccurs
```

Two documents ISO permits therefore cannot be encoded:

1. **A third `VendorSpecificDataContainer`.** Id 180 has no production for another item. The struct
   array is sized 16, so the ceiling is in the grammar, not the storage.
2. **`WPT_LF_DataPackageList` without a preceding container.** Id 178 offers only the list and the
   end-element.

## The visible symptom on the wire

One event code. With both particles absent, the generated encoder writes **1** for the end-element
where the schema grammar has **2**. A schema-informed processor reads that `1` as a start-element,
looks for content that is not there, and reports `Premature EOS` — which is what happened to all four of
our `WPT_FinePositioning*` frames and to nothing else. The sibling `WPT_AlignmentCheckReq`, an ordinary
bounded list with nothing after it, round-trips byte-exact.

## The worse symptom, which is silent

Consequence 2 above is not a byte difference. Set `LF_SystemSetupData` on a
`WPT_FinePositioningSetupRes`, leave `VendorSpecificDataContainer` empty, and call
`encode_iso20_wpt_exiDocument`:

```
control, field absent      -> encoded  (0)  23 B
LF_SystemSetupData set     -> encoded  (0)  23 B     <- byte-identical to not setting it
```

The encoder **returns success and drops the field**. Same length as the message that never carried it.
A caller has no way to know: no error, no truncation, nothing short. Reproduce with
[`tools/cbv2g-defect-probe/`](../../tools/cbv2g-defect-probe/README.md) in this repository.

Of the three issues here this is the one we would fix first. A and C are loud; this one is not.

## Suggested fix

Emit the schema's grammar for this construct: loop to `maxOccurs`, and keep the following particle
reachable from every state including the empty one. In our own generator the schema-conformant emitter
is *shorter* than the cbexigen-compatible one, because there is nothing to unroll.

---

# Issue C — every `minOccurs="2"` repeating particle generates a loop state with no exit

**Title:** Repeating particles with `minOccurs≥2` produce a `LOOP` grammar state whose only other
branch is `EXI_ERROR__UNKNOWN_EVENT_CODE`, so all three affected WPT types are unencodable

**Where:** `lib/cbv2g/iso_20/iso20_WPT_Encoder.c` — three functions, same shape:

| type | particle | ids | dead end |
|---|---|---|---|
| `WPT_LF_TransmitterDataType` | `TxSpecData` (2, 255) | 81 / 82 / 83 | id 82 |
| `WPT_LF_ReceiverDataType` | `RxSpecData` (2, 255) | 88 / 89 / 90 | id 90 |
| `WPT_TxRxPackageSpecDataType` | `PulseSequenceOrder` (2, 255) | 74 / 75 / 76 | ids 74 **and** 75 |

## Summary

This is the most serious of the three, and unlike A and B it is not a difference of encoding — the
functions cannot succeed at all.

Take the transmitter. `TxSpecData` is `minOccurs="2" maxOccurs="255"`:

- **id 81** — 2 bits: `SE(TxSpecData)`=0 → id 82, or `EE`=1 → done.
- **id 82** — 1 bit: `LOOP(TxSpecData)`=0 → id 82. The `else`, reached when the array is exhausted, is
  `error = EXI_ERROR__UNKNOWN_EVENT_CODE;`. There is no other way out of id 82.
- **id 83** — the state that would write the optional `TxPackageSpecData`, which nothing reaches.

Trace any schema-valid instance. `TxSpecData` has at least two entries, so id 81 writes the first and
moves to 82; id 82 writes the rest and stays at 82; when the array runs out, 82 fails. The only path
that terminates is the **empty** list, which `minOccurs="2"` forbids.

`RxSpecData` is identical at ids 89/90. `PulseSequenceOrder` is worse: **both** of its states dead-end,
so id 76 (`PulseSeparationTime`) is unreachable and the type fails even before the loop.

It is worth stating as one issue rather than three: the three functions differ only in names, so this
looks like the generator's handling of `minOccurs≥2` repeating particles rather than three accidents.
ISO 15118 has exactly five such particles — these three, and `CurveDataPoint` in the two DER amendments,
which cbexigen does not generate at all.

Consequences beyond the three types: `LF_SystemSetupData` and therefore every
`WPT_FinePositioningSetupReq`/`Res` that carries one, and the whole `TxPackageSpecData` subtree.

## A note on where we sit

We got the same construct wrong in our own generator, in the mirror image: the first `minOccurs`
occurrences are *forced*, so their start-element is a one-bit code with nothing to choose from, and we
emitted the two-bit loop code for them. It made one of our DER frames unreadable and we fixed it on
2026-08-07. Yours has the right widths and no way out; ours had a way out and the wrong widths. The
unrolling of a bounded repeat with a non-trivial minimum seems to be a place where it is easy to be
wrong in more than one direction.

## Confirmed by running it, not only by reading it

[`tools/cbv2g-defect-probe/`](../../tools/cbv2g-defect-probe/README.md) in this repository builds
against `03350be048b3` and drives `encode_iso20_wpt_exiDocument` with minimal valid documents — two
entries each, the schema's own minimum:

```
no LF branch      must encode   -> encoded              (0)  28 B
receiver-2        claim: fails  -> UNKNOWN_EVENT_CODE (-150)  50 B
transmitter-2     claim: fails  -> UNKNOWN_EVENT_CODE (-150)  53 B
package-spec-2    claim: fails  -> UNKNOWN_EVENT_CODE (-150)  53 B
```

The control encodes; the three cases do not. All three types, confirmed at runtime.

### B masks C, which is why nobody has hit it

Worth stating because it cost us a false negative: the probe's first run reported all three cases
**encoding cleanly**, and issue C looked wrong. It is not — issue B was swallowing them. With an empty
`VendorSpecificDataContainer` the encoder never descends into `LF_SystemSetupData` at all (see B), so C
cannot fire. Give the container one item, and it fires immediately.

So the two defects have to be fixed in that order, and C is unreachable in any code path that has not
already lost data to B. That is presumably why an encoder that cannot emit three types has gone
unnoticed.

## Not a schema problem

We can encode this construct, and an independent codec agrees with the result: on 2026-08-08 we added
vectors that populate `LF_SystemSetupData` with two and with three `TxSpecData` entries, plus
`TxPackageSpecData` with two and three `PulseSequenceOrder` entries, and **EXIficient decodes all of
them and re-encodes to the identical octets**. So the grammar is expressible; this is a generator
defect, not an ambiguity in ISO's schema.

## What it cost us, which is why it is filed rather than shrugged at

Because cbV2G could not produce these bytes, our WPT corpus has no reference vectors behind
`LF_SystemSetupData` at all — that whole subtree was checked only against our own decoder for a year.
That is precisely how we came to ship a bug of our own in the same family of construct (a repeating
particle's forced prefix, `minOccurs="2"`, encoded one bit too wide), which nothing caught until
EXIficient read it. **A generator that cannot emit a type silently removes it from everyone's test
coverage.**

## Suggested fix

Give the `LOOP` state the exit productions the schema implies — the following particle and the
end-element — or, equivalently, generate the same self-looping shape already used for a bounded list
that ends a sequence. A fix in the generator should cover all three types at once.

While you are there: id 81's `EE` at 2 bits also permits an *empty* `TxSpecData`, which `minOccurs="2"`
forbids. Per EXI 1.0 §8.5.4.1.5 the first `{min occurs}` copies of the term carry no end-element
production at all, which is also why their event code is one bit rather than two.

---

## Before sending

- [x] **Lead with what works.** 332 of 347 frames byte-exact against an independent codec, including
      complete sessions and Plug & Charge. That is the honest headline and it belongs first.
- [x] **Check the citations against current `master`.** Re-read 2026-08-08: `03350be048b3` is still
      upstream `HEAD`, and all three grammars are as described — ACDP's 6-bit codes 0/1/2/3, WPT ids
      178/179/180, transmitter ids 81/82/83 with the `UNKNOWN_EVENT_CODE` dead end.
- [x] **Cite the specification, not an opinion.** EXI 1.0 Second Edition §8.5.1 for A. B and C need no
      specification — both contradict their own input schema.
- [x] **Reproduce A without ISO's schemas.** The three-element synthetic schema does it.
- [x] **Show C is not a schema problem.** Our vectors for the same type round-trip through EXIficient.
- [x] **Say plainly that we changed our own side**, and why that makes A a question rather than a
      verdict.
- [x] **Promote the transmitter failure out of a footnote.** It was drafted as an *Also seen* under B;
      reading the generated state machine line by line showed it is the worst of the three and
      independent of B, so it is now issue C.
- [ ] **File A, B and C separately.** Three grammars, three fixes. Cross-reference them only as "found
      in the same run".
- [x] **Name every type C affects**, so the issue does not invite a partial fix. Traced 2026-08-08:
      the receiver has the same dead end at id 90, and `WPT_TxRxPackageSpecDataType` has it at *both*
      of its states. Three types, one generator behaviour.
- [x] **Run the claims, do not only read them.** `tools/cbv2g-defect-probe/` builds against the cited
      commit and drives the public API. It confirmed C for all three types — and caught that B masks C,
      which the source reading had missed and which is now the report's strongest point. It also showed
      B to be silent data loss rather than a byte difference, which changed which issue we would fix
      first.
- [x] **Make A stand on their bytes, not ours.** The probe now emits cbV2G's own `ACDP_ConnectRes` and
      `ACDP_DisconnectReq` and shows cbV2G decoding both correctly; handing the same octets to
      EXIficient produces the silent misdecode and the `Premature EOS`. Nothing of ours is in the
      input any more.
- [ ] **Decide how much generated C to paste.** The excerpts above are minimal on purpose; a
      maintainer may prefer a link to the generator input instead, since the C is machine-written.
- [ ] **Post under your own name, in your own words.**
