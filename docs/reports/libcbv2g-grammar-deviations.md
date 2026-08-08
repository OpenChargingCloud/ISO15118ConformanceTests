# Draft report to EVerest (libcbv2g / cbexigen) — two generated grammars a schema-informed EXI processor cannot read

Status: **draft, not sent.** Observed 2026-08-07 against **libcbv2g @ `03350be048b3`**, ISO 15118-20
schemas as published by ISO, cross-read with **EXIficient 1.0.4** on OpenJDK 25. Post under your own
name; see *Before sending* at the bottom.

Two observations, **A** and **B**. They are separate filings — A is the document grammar, B is a
content grammar, and a fix for either leaves the other standing — reported together because they were
found in one run and because both surface the same way: a frame a general EXI processor cannot decode.

First the context, because it decides how much weight to put on the rest. We use cbV2G as this
project's reference encoder for ISO 15118-20 and have done so for months; our vector corpus is
generated from it. Yesterday we pointed a second, unrelated codec at that corpus for the first time —
EXIficient, a general schema-informed EXI processor from the other implementation lineage — and 332 of
347 frames round-tripped byte-for-byte. Both control modes, AC and DC, EIM and Plug & Charge, five
complete sessions, signed messages and certificate chains. **The overwhelming majority of what
cbexigen generates is byte-exact against an independent implementation**, which is the finding we would
lead with. Six frames were not, in three causes. One was a defect of our own and is fixed. The other
two are below.

Neither is hypothetical for us: we have stopped reproducing both, which changes six of our vectors and
means our ACDP and WPT bytes now differ from cbV2G's.

---

# Issue A — the document grammar groups global elements that share a named type, where EXI sorts them

**Title:** `exiDocument` numbers global elements by type-grouping rather than by qname, so ACDP message
identities are swapped relative to a specification-built processor

**Version:** libcbv2g `03350be048b3`, generated `iso20_ACDP_Encoder.c` / `_Decoder.c`
(`encode_iso20_acdp_exiDocument`).

## Summary

EXI 1.0 Second Edition §8.5.1 builds the `DocContent` grammar with one `SE` production per global
element declared in the schema, over the qnames "sorted lexicographically, first by local-name, then by
uri". There is no provision for elements that share a named type.

cbexigen appears to group elements that share a type, so that the type-sharing pair lands adjacent.
For most ISO 15118 schemas this makes no difference — every global element has a type of its own and
the two orders coincide. ACDP is the exception, because ISO commented out two complex types and pointed
the elements at the Connect types:

```xml
<xs:element name="ACDP_DisconnectReq" type="ACDP_ConnectReqType"/>
<!--  <xs:complexType name="ACDP_DisconnectReqType"> … -->   <!-- commented out in ISO's schema -->
```

The resulting indices differ:

| element | EXI §8.5.1 (and EXIficient) | cbexigen |
|---|---:|---:|
| `ACDP_ConnectReq` | 0 | 0 |
| `ACDP_ConnectRes` | 1 | 2 |
| `ACDP_DisconnectReq` | 2 | 1 |
| `ACDP_DisconnectRes` | 3 | 3 |

## Why this one is worth your time

The document element selector *is* the message identity. Swapping two of them does not produce a
decode error in both directions:

- an `ACDP_DisconnectReq` written with selector 1 is read as `ACDP_ConnectRes`, whose type has more
  content than was written → `Premature EOS`, loud and obvious;
- an `ACDP_ConnectRes` written with selector 2 is read as `ACDP_DisconnectReq`, a shorter type → it
  **decodes cleanly, as the wrong message**, and re-encodes to 18 bytes against the original 20. Nothing
  reports anything.

The second is the one to worry about. A peer pairing over ACDP would act on a disconnect request it was
never sent.

## Reproduced without ISO's schemas

So that this is a statement about the rule and not about one file, the same shape in three elements —
first and third sharing a type, second sorting between them:

```xml
<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
           xmlns="urn:test:order" targetNamespace="urn:test:order" elementFormDefault="qualified">
  <xs:element name="Alpha"   type="SharedType"/>
  <xs:element name="Bravo"   type="BravoType"/>
  <xs:element name="Charlie" type="SharedType"/>
  …
</xs:schema>
```

Asking EXIficient to encode `<Alpha/>`, `<Bravo/>` and `<Charlie/>` against it and reading the two bits
after the one-byte EXI header gives `0`, `1`, `2` — plain lexicographic order, the shared type grouping
nothing.

## Suggested fix

Sort the global element declarations by qname (local-name, then uri) when emitting the document
grammar, independently of their type. Offered rather than asserted: you may have a reason for the
grouping that we cannot see from the generated output, and if so we would rather hear it — it would
change what *we* do, since we reproduced your numbering deliberately for two years and only stopped
yesterday.

---

# Issue B — the mid-sequence particle grammar contradicts its own input schema, and makes two valid documents unencodable

**Title:** `WPT_FinePositioning*` grammars cap a `maxOccurs="16"` list at two items and hide the
following optional particle from the empty state

**Version:** libcbv2g `03350be048b3`, generated `iso20_WPT_Encoder.c`,
`encode_iso20_wpt_WPT_FinePositioningReqType`, grammar ids **178/179/180**.

## Summary

ISO's schema, for all four `WPT_FinePositioning{,Setup}Req/ResType`:

```xml
<xs:element name="VendorSpecificDataContainer" type="…" minOccurs="0" maxOccurs="16"/>
<xs:element name="WPT_LF_DataPackageList"      type="…" minOccurs="0"/>
```

Two independently optional particles, the first repeating up to sixteen times. The generated grammar:

```
cbexigen  state 178 (no items):   SE(list)=0                 EE=1
          state 179 (one item):   LOOP=0  SE(LF list)=1      EE=2
          state 180 (two items):          SE(LF list)=0      EE=1   <- no third item, ever

schema    state A   (no items):   SE(list)=0  SE(LF list)=1  EE=2
          state B   (n items):    LOOP=0      SE(LF list)=1  EE=2   <- loops to maxOccurs
```

Two consequences, both about documents ISO permits:

1. **A third `VendorSpecificDataContainer` cannot be encoded at all.** State 180 has no production for
   another item. The struct array is sized 16, so the ceiling is in the grammar and not in the storage.
2. **`WPT_LF_DataPackageList` is unreachable unless at least one container was written.** State 178
   offers only the list and the end-element.

## The visible symptom

One event code. With both particles absent, the generated encoder writes **1** for the end-element
where the schema grammar has **2**. A schema-informed processor reads that `1` as a start-element,
looks for content that is not there, and reports `Premature EOS` — which is what happened to all four
of our `WPT_FinePositioning*` frames, and only those. The sibling `WPT_AlignmentCheckReq`, which has an
ordinary bounded list with nothing after it, round-trips byte-exact.

In bytes, the four frames differ from ours by exactly one bit in the last octet: `0x20`→`0x40` and
`0x02`→`0x04`.

## Also seen, and possibly the same root cause

`encode_iso20_wpt_WPT_LF_TransmitterDataType` (states 81/82) fails at runtime with
`EXI_ERROR__UNKNOWN_EVENT_CODE` for `TxSpecData` even at the schema's own required minimum of two
items — state 82 loops with no exit production. We could not get that type encoded at all, which is why
we have no cbV2G reference bytes for anything behind `LF_SystemSetupData`. It looks like the same
family of problem (a bounded repeat followed by another particle) rather than a separate one, but we
have not proven that and are not filing it as its own issue.

## Suggested fix

Emit the schema's grammar for this construct: loop to `maxOccurs`, and keep the following particle
reachable from every state including the empty one. This is *shorter* than the unrolled form — in our
own generator the schema-conformant emitter has less code than the cbexigen-compatible one, because
there is nothing to unroll.

---

## Before sending

- [x] **Lead with what worked.** 332 of 347 frames byte-exact against an independent codec, including
      complete sessions and Plug & Charge. That is the honest headline and it belongs first.
- [x] **Reproduce A without ISO's schemas.** The three-element synthetic schema does it, and keeps the
      report about the rule rather than about one file.
- [x] **Cite the specification rather than an opinion.** EXI 1.0 Second Edition §8.5.1 for A. B needs no
      specification — it contradicts its own input schema.
- [x] **Read the generated source, not just the bytes.** Grammar ids 178/179/180 and 81/82 were read in
      the generated C, not inferred from the wire.
- [ ] **Check the citations against current `master`.** They were read against `03350be048b3` on
      2026-08-07; confirm the grammar ids and file names still hold before posting.
- [ ] **File A and B separately.** Different grammars, independent fixes. Cross-reference them only as
      "found in the same run".
- [ ] **Say plainly that we changed our side.** We reproduced both of these deliberately, for
      byte-compatibility with cbV2G, and stopped on 2026-08-08. That is context they are entitled to —
      and it is also the reason to ask rather than assert on A: if there is a rationale for the
      grouping, we would change back.
- [ ] **Decide whether to mention the TransmitterDataType runtime failure.** It is unproven as a
      separate issue and may resolve with B. Consider a sentence in B rather than a filing.
- [ ] **Post under your own name, in your own words.**
