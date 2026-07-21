# exificient-ref — second, independent EXI oracle (Siemens EXIficient)

A tiny CLI around [EXIficient](https://github.com/EXIficient/exificient) (the Siemens
implementation of the generic W3C EXI 1.0 specification) used to cross-validate our
XMLDSig fragment wire encoding against an EXI processor that has **no shared lineage**
with cbV2G/cbexigen. Where `tools/cbv2g-ref` diffs bytes against the *reference*
encoder our generator is modelled on, this tool answers a different question: are the
bytes we (and cbV2G) produce valid, standards-conformant, schema-informed EXI that any
compliant processor can decode back to the intended values?

This is a **development tool only**. `dotnet test` never runs it (no Java/network in
CI, per the project's build rule) — it exists purely to produce the evidence recorded
below. There is nothing to regenerate on every change; re-run it only if the signed
fragments' wire shape changes.

## Build

Needs JDK ≥ 11 and Gradle (dependencies resolve from Maven Central on first run):

```sh
export JAVA_HOME="/path/to/jdk-21"   # Gradle 9.x requires JVM 17+
gradle compileJava
```

## Usage

```
gradle run --args="encode <xsd-entry-point> <fragment|document> <in.xml>  <out.hex>"
gradle run --args="decode <xsd-entry-point> <fragment|document> <in.hex>  <out.xml>"
```

`<xsd-entry-point>` is the top-level schema file; EXIficient's `XSDGrammarsBuilder`
follows `<xs:import>` transitively (Xerces resolves `schemaLocation` relative to the
importing file), so pointing at `V2G_CI_MsgDef.xsd` (-2) or `V2G_CI_CommonMessages.xsd`
(-20 CommonMessages) pulls in the header/body/datatypes schemas and
`xmldsig-core-schema.xsd` the same way our own generator's fragment grammar does.
Coding mode is BIT_PACKED with `FidelityOptions.createDefault()` (non-strict
schema-informed grammar) — the same convention cbV2G/cbexigen and our generator use.

## What was cross-checked

Both of the SignedInfo fragments already byte-diffed against cbV2G
([`Iso15118_2FragmentTests.cs`](../../Vanaheimr.V2G.Exi.Tests/Iso15118_2FragmentTests.cs),
[`Iso15118_20FragmentTests.cs`](../../Vanaheimr.V2G.Exi.Tests/Iso15118_20FragmentTests.cs))
were fed to EXIficient's **decoder** with the matching XSD entry point and
`fragment=true`:

| fixture | schema entry point | expected bytes (cbV2G-verified) |
|---|---|---|
| [`fixtures/iso2-signedinfo-expected.hex`](fixtures/iso2-signedinfo-expected.hex) | `Vanaheimr.V2G.Exi.Iso15118_2/Schemas/V2G_CI_MsgDef.xsd` | `Iso15118_2FragmentTests.SignedInfo_Fragment_MatchesCbV2G` |
| [`fixtures/iso20-common-signedinfo-expected.hex`](fixtures/iso20-common-signedinfo-expected.hex) | `Vanaheimr.V2G.Exi.Iso15118_20.CommonMessages/Schemas/V2G_CI_CommonMessages.xsd` | `Iso15118_20FragmentTests.SignedInfo_Fragment_MatchesCbV2G` |

Both decoded to exactly the expected `SignedInfo` content — same `CanonicalizationMethod`/
`SignatureMethod`/`DigestMethod` algorithm URIs, same `Reference URI`, same base64
digest bytes (1..32 for -2/SHA-256, 1..64 for -20/SHA-512) — see
[`fixtures/iso2-signedinfo-expected-decoded.xml`](fixtures/iso2-signedinfo-expected-decoded.xml)
and
[`fixtures/iso20-common-signedinfo-expected-decoded.xml`](fixtures/iso20-common-signedinfo-expected-decoded.xml).

**Conclusion:** the exact bytes our codec emits (byte-identical to cbV2G) are valid
schema-informed EXI per the real `xmldsig-core-schema.xsd` grammar, decodable by an
independent, generic EXI 1.0 processor with no relationship to cbV2G/cbexigen, and
they carry the intended cryptographic values. That is the property that actually
matters for XMLDSig correctness (the verifier must recover the exact bytes that were
hashed/signed), so this closes out the "external cross-validation" item for both
Phase 3 (-2) and Phase 4 (-20 CommonMessages).

## Known open point: EXIficient's own *encoder* takes more bits

Running `encode` on the equivalent XML input (`fixtures/iso2-signedinfo.xml`,
`fixtures/iso20-common-signedinfo.xml`) does **not** reproduce the cbV2G byte length —
EXIficient's own encoder emits a noticeably longer bitstream (243 vs. 173 bytes for
-2) for semantically identical content. This was investigated but not root-caused:

- It isn't whitespace/pretty-printing (minifying the input XML made no difference).
- It isn't the `mixed="true"`/`<xs:any>` wildcard extensibility points on
  `CanonicalizationMethodType`/`SignatureMethodType`/`DigestMethodType` (stripping
  them from a scratch copy of the schema changed the output by 1 byte, not ~70).
  cbV2G's own C structs don't model that wildcard content at all (see the generated
  `ANY` fields our codec exposes), so this was the leading hypothesis; it wasn't it.
- The trailing `DigestValue` binary octets are bit-identical in both streams (just
  shifted by whatever bit offset precedes them), confirming the divergence is
  entirely in how the three `anyURI` `Algorithm` string values (and/or the preceding
  event-code choices) get bit-packed, not in a structural/semantic difference.

Per the project's wire-semantics rule, this is **not** acted on — cbV2G byte-exact
match stays the authoritative conformance oracle, and this is a second-oracle
*validation* tool, not a wire-format source of truth. Recorded here so nobody
re-discovers this from scratch: if the encode-side gap is ever worth closing (e.g. to
also byte-diff against EXIficient directly), start by dumping EXIficient's grammar
event trace for the `SignatureMethod`/`CanonicalizationMethod`/`DigestMethod`
start-tags to see exactly which event get 2nd-level vs. 1st-level codes for the
`anyURI` `Algorithm` attribute.

## Files

| file | purpose |
|------|---------|
| `build.gradle` | EXIficient 1.0.7 dependency + `application` plugin (`mainClass = ExificientRef`) |
| `src/main/java/ExificientRef.java` | the encode/decode CLI |
| `fixtures/*.xml` | the exact SignedInfo content from the C# fragment tests, as standalone XML |
| `fixtures/*-expected.hex` | the cbV2G-verified `expectedHex` from the C# tests, space-separated |
| `fixtures/*-expected-decoded.xml` | EXIficient's decode of the above — the cross-validation evidence |

## Plug & Charge SignedInfo signing form (2026-07-21 — root-caused)

`fixtures/iso20-common-signedinfo-transforms.xml` is Josev's exact live PnC `SignedInfo` (a `Transforms`
element + SHA-256 URIs). Set `EXIF_CANONICAL=1` to encode in EXIficient's **Canonical EXI** (W3C exi-c14n)
mode instead of the default.

Josev's `SignedInfo` signature verifies against **none** of the fragment encodings built over the *combined*
`V2G_CI_CommonMessages` schema (our cbV2G-matched 210 B; EXIficient default 245 B; EXIficient Canonical EXI
246 B), even though our fragment codec is byte-exact for the reference *digest*. **Root cause (found by
decompiling Josev's `EXICodec.jar`):** Josev maps the XMLDSig namespace to `BuiltInSchema.XSDCore` →
`XMLDSIG_Core_Schema_Grammar`, a grammar built from **`xmldsig-core-schema.xsd` standalone**, so its EXI
*Fragment* top-level element event code is one bit narrower (far fewer global elements) and the whole bitstream
shifts. Josev's own codec emits a **209-byte** `SignedInfo`, and Josev's captured signature verifies against it
(`JosevPnCSignatureDiag.JosevSignsSignedInfoOverStandaloneXmldsigGrammar`).

Note: encoding this same `SignedInfo` here with the **standalone** `xmldsig-core-schema.xsd` as the entry point
(`encode …/xmldsig-core-schema.xsd fragment …`) via EXIficient's *runtime* `XSDGrammarsBuilder` gives **244 B**
— close but not byte-identical to Josev's **209 B** *pre-generated* grammar. So the faithful reproduction uses
Josev's own jar/grammar, not EXIficient's runtime build of the same schema. See
`Vanaheimr.V2G.Exi.Tests/Interop/JosevPnCSignatureDiag.cs` and
`docs/interop-runs/2026-07-21-iso20-dc-pnc-tls/notes.md`.
