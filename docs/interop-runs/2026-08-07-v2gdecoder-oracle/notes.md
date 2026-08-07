# 2026-08-07 — V2Gdecoder as a second EXI oracle, and what it found

**Result: 186 frames, 183 byte-exact, 3 mismatches, 0 decode failures.**

The question that started this was whether [ChargePoint's wireshark-v2g](https://github.com/ChargePoint/wireshark-v2g)
could validate our EXI parser. It cannot — and the reason is worth writing down, because it is the same
reason the interop matrix keeps understating how much independent evidence we actually have.

## Why the dissector is not an oracle

`extern/dependencies.cmake` fetches `EVerest/libcbv2g` at `GIT_TAG 03350be048b35b179905129005a97144a4bdcf93`.
That is byte-for-byte the pin in our own
[`tools/cbv2g-ref/CMakeLists.txt:23`](../../../libs/EVSimulatorApp/libs/WWCP_ISO15118/tools/cbv2g-ref/CMakeLists.txt).
Same library, same commit. Agreement would prove nothing; disagreement would mean a bug in our harness,
not in the codec.

Nor do the dissector's field tables help. `src/packet-v2giso20.c` references `struct iso20_*` 211 times
and touches the bitstream twice: it is a pretty-printer over cbV2G's decoded structs, not a second
reading of the schemas.

## Two findings from their patch set, before running anything

`extern/` carries two libcbv2g patches, and they point in opposite directions.

**`libcbv2g-fix-iso20-loop-grammars.patch` is stale.** It rewrites seven `grammar_id = 3` assignments in
the -20 list decoders. It is not in their `PATCH_COMMAND` chain, and checking the pinned commit shows
why: all seven loop back to their own grammar correctly (62, 81, 87, 116, 122, 137, 182). It targets an
older libcbv2g. Our oracle is unaffected.

**`libcbv2g-fix-iso20-secp521-buffer-size.patch` is live, and applies to our exact pin.** It raises
`iso20_secp521_EncryptedPrivateKeyType_BYTES_SIZE` from 94 to 128 because "the secp521 encrypted private
key can be up to 100 bytes when encoded". Read against the schema it points the other way: the -20
CommonMessages XSD declares `xs:length value="94"` — *exact*, not a maximum — and 94 is the AES-GCM shape
the transport actually has, IV 12 ‖ ciphertext 66 (the P-521 scalar) ‖ tag 16. cbV2G is right; whatever
peer emitted 100 bytes was not. (12 + 72 + 16 = 100 would be a scalar padded from 66 to 72 — a guess,
and only that.)

We did not follow the patch. What we did instead is pin down where the facet lives on our side:
`Interop/Secp521PrivateKeyFacetTests.cs`. The codec stays lenient — the source generator recognises
`xs:length` and deliberately ignores it, since length facets constrain the value space and not the EXI
encoding — and `ContractProvisioning.RecoverContractKey` is strict, rejecting anything but 94 by name.
A cbV2G-based peer fails to decode the frame at all where we hand the caller a value and refuse it one
layer up. Neither is wrong; a divergence in *where* a violation surfaces is what reads as "your message
is corrupt" in someone else's log.

## The oracle their docker tool points at

`tools/docker/decoder/Dockerfile` pulls **V2Gdecoder** (FlUxIuS): RISE-V2G 1.2.6 + **EXIficient 1.0.4**.
Different codec, different language, different author — and unlike Josev, which only ever judged the
frames Josev happened to send, it encodes and decodes anything offline. Setup and caveats:
[`tools/interop-v2gdecoder/README.md`](../../../tools/interop-v2gdecoder/README.md). Scope is SAP,
ISO 15118-2:2013 and DIN 70121; RISE-V2G predates -20.

Method, per frame: `our bytes → their decode → their encode → compare`. No XML-to-model mapping, so it
is cheap; the same shape `regenerate-appprotocol-vectors.py` uses against cbV2G.

| input | frames | ok | mismatch | decode-fail |
|---|---:|---:|---:|---:|
| `Iso15118_2.vectors.json` (cbV2G-generated) | 39 | 39 | — | — |
| `AppProtocol.vectors.json` | 17 | 16 | 1 | — |
| `Session.iso2-ac-eim.trace.json` | 24 | 24 | — | — |
| `Session.iso2-ac-eim-meter.trace.json` | 24 | 24 | — | — |
| `Session.iso2-ac-eim-sapboth.trace.json` | 24 | 24 | — | — |
| `Session.iso2-ac-pnc.trace.json` | 28 | 26 | 2 | — |
| `Session.iso2-dc-eim.trace.json` | 30 | 30 | — | — |
| **total** | **186** | **183** | **3** | **0** |

Zero decode failures is the headline: an independent implementation reads every frame we produce, across
five complete sessions, both directions, EIM and PnC, AC and DC.

## The one artefact

`res_ok_no_schemaid` — a 3-byte `supportedAppProtocolRes`. Their decoder is *fuzzy*: it tries grammars
until one parses, and on three bytes a wrong grammar succeeds. It reads our frame as a `MsgDataTypes`
`Entry` and re-encodes 56 bytes that its own decoder then cannot read back. The `_with_schemaid` and
`res_failed_no_schemaid` variants round-trip fine. Their tool, not our bytes — and the only artefact in
186 frames.

## The real finding: the corpus has a blind spot

The two remaining mismatches are the signed PnC `AuthorizationReq` (307 vs 272 B) and `MeteringReceiptReq`
(317 vs 282 B). Both short by exactly 35. Both decode, on both sides, to the identical infoset.

35 is the length of `http://www.w3.org/TR/canonical-exi/` — the only value in the document that occurs
twice under the attribute name `Algorithm`:

```
CanonicalizationMethod/@Algorithm = http://www.w3.org/TR/canonical-exi/          (1st)
SignatureMethod/@Algorithm        = http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256
Transform/@Algorithm              = http://www.w3.org/TR/canonical-exi/          (2nd)
DigestMethod/@Algorithm           = http://www.w3.org/2001/04/xmlenc#sha256
```

EXIficient writes the second occurrence as an EXI value-partition hit; we write the literal. Confirmed by
substitution rather than inference: make that second value unique while keeping its length, and their
encoder produces **307 bytes, ours exactly**. Leave it repeated and they produce 272.

Our miss-only encoder is a documented decision, not a discovery — see the remarks on
`ExiPrimitives.ReadStringValue`: cbV2G never emits hits, every checked-in vector is cbV2G's output, and
an encoder that began emitting them would invalidate all of them at once. The decoder was written to
accept what a conforming peer may send.

What is new is on either side of that decision:

1. **No cbV2G vector repeats an `Algorithm` value.** The signed ones carry no `<Transforms>` block at
   all. So 39/39 byte-exact agreement never exercised the partition — the vectors cannot tell a
   miss-only encoder from a conforming one. The session traces do, which is why the divergence shows up
   there and nowhere else. An oracle corpus that agrees on everything it can reach is not the same as an
   oracle corpus that reaches everything.

2. **Until this run no foreign encoder had ever handed our decoder a real partition hit.** That half of
   the design rested on intent alone. It now has two, from an encoder that is not ours, and reading them
   back and re-encoding lands on our own octets exactly — `ExiStringTableTests.cs`, offline, no Java.

Which way the risk runs is worth being clear about: writing literals costs bytes and nothing else, since
a conformant decoder reads them — EXIficient did, for all 186 frames. The exposure was always the other
direction, an EXIficient-based peer (Josev, RISE-V2G, by lineage eVDriveFlow's OpenEXI) sending us the
compact form. That is now tested rather than assumed.

## Open

- Whether EXI §7.3.3 *requires* the partition hit on encode, or merely permits it, is a question for the
  spec text — not settled here, and the answer changes only whether the 35 bytes are a deviation or a
  choice. It does not change interoperability in either direction, which is measured above.
- DIN 70121 is in V2Gdecoder's scope and untested by us: we have no DIN corpus beyond the Tesla
  handshake, which is SAP only.
- ~~The `-20` traces cannot be checked this way at all.~~ **Done the same day**, by driving EXIficient
  directly against ISO's own `-20` schemas — the class is in the same jar. 347 frames, 332 byte-exact,
  and six that a second codec cannot read, all in message sets no independent stack had ever touched.
  One of the six was a real encoder defect and is fixed (333 now); the other five are a deliberate
  choice to match cbV2G where cbV2G and the schema disagree.
  See [`2026-08-07-exificient-iso20`](../2026-08-07-exificient-iso20/notes.md).

## Files

- [`roundtrip-results.json`](roundtrip-results.json) — verdicts per frame; bytes retained only for the
  three that did not match, since everything that matched is already in the corpus.
