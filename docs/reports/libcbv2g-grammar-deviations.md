# Draft report to EVerest (libcbv2g / cbexigen) — three generated grammars

Status: **draft, not sent.** Three filings, written out ready to post in
[`libcbv2g/`](libcbv2g/) — this file is the account of how they were found and what is owed alongside
them. Post under your own name; see *Before sending* at the bottom.

Observed 2026-08-07/08 against **libcbv2g `03350be048b3`**, which is still `HEAD` of
`EVerest/libcbv2g` as of **2026-08-11** — so none of this is already fixed upstream. Schemas are ISO's,
cross-read with **EXIficient 1.0.4** on OpenJDK 25, and every source citation was re-read against that
checkout on 2026-08-08 and mechanically re-derived from it on 2026-08-11.

## First, what works

We use cbV2G as this project's reference encoder for ISO 15118-20 and have done for months; our whole
vector corpus is generated from it. On 2026-08-07 we pointed a second, unrelated codec at that corpus
for the first time — EXIficient, a general schema-informed EXI processor from the other implementation
lineage — and **332 of 347 frames round-tripped byte-for-byte**. Both control modes, AC and DC, EIM and
Plug & Charge, five complete sessions, signed messages and certificate chains.

That is the headline, and it belongs first in any of the three filings: the overwhelming majority of
what cbexigen generates agrees to the octet with an implementation that shares none of its code. Six
frames did not, in three causes — one of them a defect of our own, now fixed.

## The three filings

Each is written to be posted on its own — self-contained, no cross-references that break when pasted
alone, and the generated C kept to the two grammar comments that state the defect in the generator's
own words. Take the file, paste everything under its rule.

| | file | one line |
|---|---|---|
| **A** | [`libcbv2g/issue-a-acdp-document-element-order.md`](libcbv2g/issue-a-acdp-document-element-order.md) | Document element codes are ordered by **type name** rather than element qname; in ACDP one message decodes cleanly as another. Written as a **question** — see the note at the top of that file. |
| **B** | [`libcbv2g/issue-b-mid-sequence-particle-drops-a-field.md`](libcbv2g/issue-b-mid-sequence-particle-drops-a-field.md) | An optional element after an optional list is **silently dropped**, and the list is capped at two. |
| **C** | [`libcbv2g/issue-c-minoccurs-loop-has-no-exit.md`](libcbv2g/issue-c-minoccurs-loop-has-no-exit.md) | `minOccurs="2"` particles get a loop state with no exit; three WPT types cannot be encoded at all. |

**If only one gets attention, make it B.** A and C fail loudly; B returns success and loses a field.
B also *masks* C — the encoder never descends far enough to reach it — so they have to be fixed in
that order, and C is unreachable in any path that has not already lost data to B.

## And then the whole library was read, on 2026-08-11

All three were found by round-tripping our corpus, which bounds them by what our vectors walk. So the
generated C was read directly instead — every grammar in the library, not every grammar we exercise:
16 files, 976 functions, **4 792 states**, 3 826 particles, 8 document grammars
([`tools/cbv2g-grammar-sweep/`](../../tools/cbv2g-grammar-sweep/README.md),
[run notes](../interop-runs/2026-08-11-libcbv2g-grammar-sweep/notes.md)). Three things came of it.

**A is bigger and better understood, and one of its sentences was wrong.** The order is not a
*grouping* of elements sharing a type — it is a **sort by type name**, which reproduces the generated
order exactly in all eight document grammars and also explains the XMLDSig `Signature` block, where no
types are shared. Five of eight grammars deviate from §8.5.1, not one. The wire consequence is still
ACDP's alone, because the other four move only elements no ISO 15118 message is rooted at. The filing
now says all of that, and the ordering claim can be re-checked against libcbv2g alone.

**Nothing else is structurally wrong.** 261 complex types were held against the content model they were
generated from, by enumerating the child sequences the schema permits and walking each through the
generated state machine: **only the seven WPT types already in B and C reject anything.** Three further
invariants — event-code numbering, code width, encoder/decoder agreement — are clean across all 4 792
states. That is worth as much as a finding: it says where not to look.

**One new row, and it is minor.** An empty `<Body/>` is valid against ISO 15118-2's schema and
cbexigen's `BodyType` grammar has no end-element production, so neither direction can handle it — both
fail loudly, no real message is affected, and the 35 message codes are unshifted. Mentioned here rather
than filed.

## How this was found, and what it cost us

The run behind all three is [`2026-08-07-exificient-iso20`](../interop-runs/2026-08-07-exificient-iso20/notes.md):
our ISO 15118-20 corpus, generated with cbV2G, read for the first time by a codec from the other
implementation lineage.

Two things are worth carrying into the conversation, and both are about us rather than them.

**We reproduced A and B on purpose.** Byte-exactness with the reference encoder is how our vector
corpus earns its keep, so where cbexigen and the schema disagreed we followed cbexigen. We stopped on
2026-08-08 ([notes](../interop-runs/2026-08-08-schema-conformant-acdp-wpt/notes.md)), which moved six of
our vectors. That is why A is a question rather than a verdict.

**We got C's construct wrong ourselves, in the mirror image.** The first `minOccurs` occurrences of a
repeating particle are forced, so their start-element is a one-bit code with nothing to choose from; we
emitted the two-bit loop code. Theirs has the right widths and no way out, ours had a way out and the
wrong widths. And because their encoder cannot emit those types at all, our corpus had no reference
bytes for that whole subtree — which is how our version survived a year unseen. A generator that cannot
emit a type quietly removes it from everyone's coverage.

**Reading the code was not enough.** Every claim here was first derived from the generated C, and
`tools/cbv2g-defect-probe/` was written to run them instead. It changed two of the three: it showed B to
be silent data loss rather than a byte difference, and it caught that B masks C — which the source
reading had missed, and which is now the most useful sentence in the filing.

## Before sending

- [x] **Lead with what works.** 332 of 347 frames byte-exact against an independent codec, including
      complete sessions and Plug & Charge. That is the honest headline and it belongs first.
- [x] **Check the citations against current `master`.** Re-read 2026-08-08 and again 2026-08-11:
      `03350be048b3` is still upstream `HEAD`, and all three grammars are as described — ACDP's 6-bit
      codes 0/1/2/3, WPT ids 178/179/180, transmitter ids 81/82/83 with the `UNKNOWN_EVENT_CODE` dead
      end. On 2026-08-11 every id in all three filings was re-derived mechanically rather than re-read.
- [x] **Say how far the search went, so the filing is not read as three cherry-picked types.** All
      4 792 generated states swept 2026-08-11: three further invariants clean, 261 content models
      checked, nothing structurally wrong outside the seven WPT types already here. The sweep is
      [`tools/cbv2g-grammar-sweep/`](../../tools/cbv2g-grammar-sweep/README.md) and two of its three
      checks need no schemas, so a maintainer can run them.
- [x] **Correct A rather than defend it.** The sweep showed the ordering is a sort by type name, not a
      grouping of shared types, and that it reaches five document grammars. The filing was rewritten;
      the sentence claiming ACDP was the only place it could show is gone.
- [x] **Cite the specification, not an opinion.** EXI 1.0 Second Edition §8.5.1 for A. B and C need no
      specification — both contradict their own input schema.
- [x] **Reproduce A without ISO's schemas.** The three-element synthetic schema does it.
- [x] **Show C is not a schema problem.** Our vectors for the same type round-trip through EXIficient.
- [x] **Say plainly that we changed our own side**, and why that makes A a question rather than a
      verdict.
- [x] **Promote the transmitter failure out of a footnote.** It was drafted as an *Also seen* under B;
      reading the generated state machine line by line showed it is the worst of the three and
      independent of B, so it is now issue C.
- [x] **File A, B and C separately.** Written out as three self-contained bodies in `libcbv2g/`, each
      pasteable on its own — no cross-reference that breaks when it is the only thing on the page. The
      one place B and C have to mention each other (B masks C) is phrased to stand alone.
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
- [x] **Make the claims re-checkable after posting.** `tools/cbv2g-defect-probe/` takes
      `CBV2G_PROBE_REF`, and `CbV2GDefectProbeTests` runs it as an `[Explicit]` test, so
      *"is it fixed yet?"* is one command against `main` rather than a re-reading of this file. The
      check is written to **stop passing** when it is fixed.
- [x] **Decide how much generated C to paste.** Cut to the minimum that still proves the point: for C,
      the generator's own two `// Grammar: ID=…` comments, which state the defect in its own words and
      are greppable; for B, the state table rather than the C it came from; for A, none at all — the
      byte pair says it. Everything longer moved to the probe, which is linked.
- [ ] **Post under your own name, in your own words.**
