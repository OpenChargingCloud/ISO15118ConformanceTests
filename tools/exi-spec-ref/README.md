# exi-spec-ref — the EXI 1.0 Recommendation, checked in

[`exi-1.0-second-edition.html`](exi-1.0-second-edition.html) is W3C's *Efficient XML Interchange (EXI)
Format 1.0 (Second Edition)*, W3C Recommendation 11 February 2014, byte-identical to
<https://www.w3.org/TR/exi/> as retrieved on **2026-08-08**, with the four figures it references. Open
it in a browser; the stylesheet and the W3C logo load from the network and their absence is cosmetic.

## Why a copy, when `rfc8032-ref/` fetches instead

That directory is the house style — it ships a parser and pulls RFC 8032 from `rfc-editor.org` at run
time — and this one deliberately breaks it, for a reason this repository learned the hard way.

Every `-20` schema imports W3C's `xmldsig-core-schema.xsd`, which opens with a DOCTYPE pointing at
`http://www.w3.org/2001/XMLSchema.dtd`. Xerces fetches that DTD on **every** grammar build, W3C has
rate-limited exactly that traffic for years, and once the requests start being refused the failure is
reported against the local file — a file that is present, readable and correct. It cost an afternoon;
the whole story is in
[`2026-08-07-exificient-iso20`](../../docs/interop-runs/2026-08-07-exificient-iso20/notes.md).

Having been bitten by depending on `w3.org` being reachable, it would be odd to make the one document
that settles our wire-format arguments depend on it too. So the document is here, and
[`fetch.sh`](fetch.sh) exists to *verify* it rather than to supply it:

```bash
bash tools/exi-spec-ref/fetch.sh            # re-download and check against SHA256SUMS
bash tools/exi-spec-ref/fetch.sh --update   # overwrite and rewrite the sums; read the diff
```

Nothing in `dotnet test` reads any of this. It is documentation, and the offline run stays green
without it.

## Licence

W3C publishes its Recommendations under the [W3C Document
Licence](https://www.w3.org/Consortium/Legal/copyright-documents), which permits copying and
redistributing the document **unmodified**, with the copyright notice and licence reference intact.
Both are in the file, untouched. `SHA256SUMS` is what makes "unmodified" checkable rather than claimed;
only the local filename differs from W3C's, which leaves the document itself byte-for-byte theirs.

> **This is not a precedent for ISO's schemas.** Those are licensed quite differently, are not
> redistributed by this repository, and are fetched per-developer by
> `libs/EVSimulatorApp/libs/WWCP_ISO15118/tools/download-schemas.sh` under ISO's Customer Licence
> Agreement — see the build section of the root `README.md`. A W3C Recommendation being freely
> redistributable says nothing about them.

## What this project actually leans on

The sections that have decided a wire-format question here, so the next argument starts from the text
rather than from a download:

| § | What it settles | Where it was used |
|---|---|---|
| **8.5.1** *Schema-informed Document Grammar* [`#informedDocGrammars`](exi-1.0-second-edition.html#informedDocGrammars) | `DocContent` has one `SE` production per global element, over the qnames sorted lexicographically by local-name then uri — with no exception for elements sharing a named type | ACDP element numbering: cbexigen groups the type-sharing pair, we no longer do ([2026-08-08](../../docs/interop-runs/2026-08-08-schema-conformant-acdp-wpt/notes.md)) |
| **8.5.4.1.5** *Particles* [`#particles`](exi-1.0-second-edition.html#particles) | how a bounded particle unrolls — `{min occurs}` copies of the term, then `{max occurs} − {min occurs}` further optional ones. The forced prefix is right there in the text | the forced-occurrence event-code width, cause C ([2026-08-07](../../docs/interop-runs/2026-08-07-exificient-iso20/notes.md)) |
| **8.5.4.1.6** *Element Terms* [`#elementTerms`](exi-1.0-second-edition.html#elementTerms) | a substitution group's members are sorted by name, then target namespace | the substitution-choice ordering in the generated codecs |
| **7.3.3** *Partitions Optimized for Frequent use of String Literals* [`#encodingOptimizedForMisses`](exi-1.0-second-edition.html#encodingOptimizedForMisses) | the value partitions: a hit is `0` plus an n-bit compact identifier, a miss is the literal with its length incremented | the value-partition deltas in both corpora ([2026-08-08](../../docs/interop-runs/2026-08-08-value-partition/notes.md)) |
| **7.1.9** *n-bit Unsigned Integer* [`#encodingBoundedUnsigned`](exi-1.0-second-edition.html#encodingBoundedUnsigned) | the bounded-range integer form | `Exponent` read as −64 where 0 was written, the one bit that gave cause C away |

Two things this directory has already earned, on the day it was added:

- **§8.5.4.1.5 states cause C outright.** That fix was derived from EXIficient's behaviour and a
  synthetic-schema measurement, with the specification unread. It says the same thing in as many words.
  Deriving it was not wasted — but reading first would have been cheaper.
- **A citation that looked wrong is right.** Several files here cite §7.3.3 for the value partition, and
  its title — *"…Frequent use of String Literals"* — reads like a compression option rather than the
  mechanism. It is the mechanism: those are the partitions holding local-names and **value** content
  items. Checked before changing anything, and nothing needed changing.

And a caution worth carrying: where the specification says what is *legal*, it usually does not say
what an encoder will *cost*. §7.3.3 defines the hit encoding exactly, and the same repeated URI still
comes to 35 bytes in `-2` and 34 in `-20`. For that this repository measures, with
[`tools/interop-exificient/`](../interop-exificient/README.md).
