# 2026-08-11 — all 4 792 generated grammars in libcbv2g, read directly

The three filings in [`docs/reports/libcbv2g/`](../../reports/libcbv2g/) were found by round-tripping
**our** ISO 15118-20 corpus through EXIficient. That is a good way to find a defect and a bad way to
bound one: it can only see the grammars our vectors walk. The filings say so themselves —
*"a generator that cannot emit a type quietly removes it from everyone's coverage."*

So: read the generated code instead of the traffic.

| | |
|---|---|
| Target | [libcbv2g](https://github.com/EVerest/libcbv2g) **`03350be048b3`**, still `main` HEAD on 2026-08-11 — the commit the three filings cite, unmoved |
| Ours | [`tools/cbv2g-grammar-sweep/`](../../../tools/cbv2g-grammar-sweep/README.md), three static checks over the generated C |
| Scope | 16 generated files, 976 functions with a grammar, **4 792 grammar states**, 3 826 declared particles, 8 document grammars — appHand, DIN 70121, ISO 15118-2, and the five ISO 15118-20 families |
| Outcome | **One new finding** (the document-code sort key, which corrects our own issue A), **one correction owed to us**, and everything else structurally sound |
| Artifacts | [`check1-state-machines.txt`](check1-state-machines.txt) · [`check2-document-codes.txt`](check2-document-codes.txt) · [`check3-content-models.txt`](check3-content-models.txt) |

## Why this is readable at all

cbexigen writes its model into the C it emits. Above each function, the content model it derived from
the schema; inside it, the state machine one state at a time:

```c
// Particle: VendorSpecificDataContainer, WPT_DataContainerType (0, 16); LF_SystemSetupData, … (0, 1);
…
case 179:
    // Grammar: ID=179; read/write bits=2; LOOP (VendorSpecificDataContainer), START (WPT_LF_DataPackageList), END Element
        // Event: LOOP (VendorSpecificDataContainer, …); next=180
        // Event: END Element; next=3
        done = 1;
```

Both halves parse, which turns the library into a labelled graph per type plus the schema model it was
built from. Everything below is decided on that graph. Two of the three checks need nothing but the
checkout — deliberately, because a claim that only a schema licensee can verify is a claim that will
not be verified.

## Check 1 — the state machines against themselves

| invariant | states | result |
|---|---:|---|
| every reachable state can reach an accepting one | 4 792 | **8 fail** |
| every declared particle is offered by some reachable state | 3 826 | **8 fail** |
| each production's event code is its index in the production list | 4 792 | **clean** |
| code width is `ceil(log2(n+1))` over the productions | 4 792 | **clean** |
| encoder and decoder carry the same grammar for the same state | 8 file pairs | **clean** |

The 8 + 8 are issue **C** and its consequence, in both directions: `WPT_TxRxPackageSpecDataType`
(ids 74 *and* 75), `WPT_LF_TransmitterDataType` (82), `WPT_LF_ReceiverDataType` (90), and the four
particles stranded behind them — `PulseSeparationTime`, `PulseDuration`, `PackageSeparationTime`,
`TxPackageSpecData`. Nothing else in 4 792 states.

**A correction to us, not to them.** The first draft of this check reported those four particles as
being "in no grammar state". They are: ids 76, 77 and 78 exist and encode them. They are simply
*unreachable*. Issue C had it right — *"id 76 is unreachable"* — and this note had it wrong until the
generated C was read line by line. The wording matters to whoever goes looking for the state.

The three clean rows are the more useful half. They say that outside the `minOccurs="2"` construct the
generator's arithmetic is right everywhere, which is worth knowing before anyone spends a day looking
for a second one.

## Check 2 — the document element codes, and the new finding

EXI 1.0 Second Edition §8.5.1 builds one `SE` production per global element declaration, over the
qnames **sorted by local-name and then by uri**. cbexigen sorts by something else, and the sweep
identifies it exactly:

> **The order is that of sorting by the generated type name, with the element name as tiebreak.**
> Reproduced in **all eight** document grammars, to the code.

| document grammar | global elements | §8.5.1 | by type name | codes that differ |
|---|---:|---|---|---:|
| `appHand` | 2 | matches | matches | 0 |
| `din` | 1 | matches | matches | 0 |
| `iso2` | 1 | matches | matches | 0 |
| `iso20_acdp` | 34 | **deviates** | matches | **6** |
| `iso20_ac` | 42 | **deviates** | matches | 4 |
| `iso20` (CommonMessages) | 54 | **deviates** | matches | 4 |
| `iso20_dc` | 48 | **deviates** | matches | 4 |
| `iso20_wpt` | 38 | **deviates** | matches | 4 |

The three that match do so because a single-root document grammar cannot be misordered — not because
they follow a different rule.

**This corrects our own filing.** Issue A says the document grammar *"groups global elements sharing a
type"* and that *"ACDP is the only ISO 15118 message set where this can show, because it is the only
one where two global elements share a type."* Both halves are wrong:

- The mechanism is a **sort key**, not a grouping. Sorting by type name explains the ACDP pair *and*
  the `Signature` block, which type-sharing cannot: `Signature`, `SignatureMethod`,
  `SignatureProperties` and `SignatureProperty` share no type, and cbexigen still orders them
  `SignatureMethod`, `SignatureProperties`, `SignatureProperty`, `Signature` — which is
  `SignatureMethodType < SignaturePropertiesType < SignaturePropertyType < SignatureType`.
- **Five of eight** document grammars deviate, not one.

**What it does not change: the consequence is still ACDP's alone.** In the other four families every
misplaced element is from XMLDSig, and no ISO 15118 message is an EXI document rooted at one of them —
the signature is computed over an EXI **fragment** of `SignedInfo` (ours does it in
`V2GSignature.SignedInfoFragment`, through `EncodeFragment_SignedInfo`), and a fragment grammar does
not use the document element codes at all. So: a real deviation in five families, with a wire
consequence in one. Say both, or the filing over-claims.

What it buys is the answer to the question issue A actually asks — *is the grouping intentional?* It
is not a grouping, so the fix is not an ACDP special case: it is the sort key.

## Check 3 — every content model, against the schema it came from

For each type, enumerate the child sequences the schema permits (each optional taken and skipped, each
repeat at its minimum and one above, substitution groups expanded to their members, attributes
prepended) and walk each one through the generated state machine. A sequence the schema allows and the
machine cannot walk is a document that can neither be encoded nor decoded.

**261 complex types checked. 8 rejected — and 7 of them are already filed.**

| type | rejected | which filing |
|---|---:|---|
| `WPT_FinePositioningSetupReqType` · `SetupResType` | 1 of 4 each | **B** — `LF_SystemSetupData` unreachable with an empty container |
| `WPT_FinePositioningReqType` · `ResType` | 1 of 4 each | **B** — same, `WPT_LF_DataPackageList` |
| `WPT_TxRxPackageSpecDataType` | 2 of 2 | **C** |
| `WPT_LF_TransmitterDataType` | 4 of 4 | **C** |
| `WPT_LF_ReceiverDataType` | 2 of 2 | **C** |
| `iso2 BodyType` | 1 of 35 | new, and minor — see below |

That is the systematic negative, and it is the point of the exercise: **outside WPT, every one of the
261 types carries every document its schema permits.** ISO 15118-2, DIN's shared types, appHand, and
the ISO 15118-20 CommonMessages, AC, DC and ACDP families are structurally sound. Three days of looking
for a fourth grammar defect would have found nothing, and now nobody has to.

**The one new row is marginal and is recorded as such.** `<Body/>` with no child element is valid
against ISO 15118-2's schema — `BodyElement` is `minOccurs="0"` — and cbexigen's `BodyType` grammar has
no end-element production, so it cannot be written (`UNKNOWN_EVENT_FOR_ENCODING`) or read
(`UNKNOWN_EVENT_CODE`). Both directions fail loudly, no real message is affected, and the 35 message
codes are unshifted because the end-element would have sorted last anyway. Worth a sentence in the
umbrella report and not a filing of its own.

## What this does not decide

- **Bit-level correctness.** Check 3 asks whether a document can be *walked*, not whether the octets
  are the ones a spec-built processor writes. That is
  [`2026-08-07-exificient-iso20`](../2026-08-07-exificient-iso20/notes.md)'s job, and it is how B and C
  were found in the first place.
- **DIN 70121's content models.** We do not hold that schema, so check 3 skips its 69 types; checks 1
  and 2 cover them. DIN shares the ISO 15118-2 shapes throughout, so the risk of something hiding
  there is low but not zero.
- **Runtime limits.** `ARRAY_SIZE` ceilings, buffer sizes and the string-table caps are configuration
  rather than grammar. The `maxOccurs=16` list capped at two positions is *in* the grammar, and is
  already in issue B.

## Re-running it

```bash
bash tools/cbv2g-grammar-sweep/run.sh                        # the pinned commit
CBV2G_SWEEP_REF=main bash tools/cbv2g-grammar-sweep/run.sh    # ...has anything moved?
```

Exits 0 whatever it finds — it is a measurement, not an assertion. The companion
[`tools/cbv2g-defect-probe/`](../../../tools/cbv2g-defect-probe/README.md) is the one that asserts, and
is meant to stop passing when the filings are acted on.
