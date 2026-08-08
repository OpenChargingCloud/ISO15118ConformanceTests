# 2026-08-08 — where cbexigen and the schema disagree, follow the schema

**Result: the whole ISO 15118-20 corpus is now readable by an independent codec. 347 frames, 339
byte-exact, 8 mismatches, none unreadable — down from six unreadable the day before.**

Yesterday's [EXIficient run](../2026-08-07-exificient-iso20/notes.md) found six `-20` frames that a
second codec could not read, in three causes. One was a defect and was fixed the same day. The other
two were decisions this project had made on purpose and had never had to defend, because until then
nothing else had ever read an ACDP or WPT frame of ours. This is the day they were defended, and
reversed.

| | |
|---|---|
| Decision | ACDP and WPT follow ISO's schema and the EXI specification, not cbexigen |
| Mechanism | `Directory.Build.props`: `ExiDocumentElementOrder=ExiSorted`, `ExiParticleGrammar=SchemaConformant` |
| Reversible | yes — both properties still accept `CbV2GCompatible`, and the generator's own defaults are unchanged |
| Vectors moved | 6 of 347 (`ACDP_ConnectRes`, `ACDP_DisconnectReq`, four `WPT_FinePositioning*`) |
| Verified by | EXIficient 1.0.4 against ISO's own schemas; `dotnet test -c Release` green |

## A — the document element numbering

The claim yesterday was that this "needs the EXI 1.0 text on the document grammar, which is not settled
here". It is settled. §8.5.1 builds the `DocContent` grammar with one `SE` production per global
element, over the qnames "sorted lexicographically, first by local-name, then by uri"
([W3C, EXI 1.0 Second Edition, §8.5.1](https://www.w3.org/TR/exi/#informedDocGrammars)) — with no
provision for elements that share a named type.

Checked against an implementation as well, on a schema containing nothing but the ACDP shape: three
global elements, the first and third sharing a type, the second sorting between them.

```xml
<xs:element name="Alpha"   type="SharedType"/>
<xs:element name="Bravo"   type="BravoType"/>
<xs:element name="Charlie" type="SharedType"/>
```

| element | EXIficient | ours, before |
|---|---|---|
| `Alpha` | 0 | 0 |
| `Bravo` | **1** | 2 |
| `Charlie` | **2** | 1 |

So cbexigen departs from the specification, and we were reproducing the departure. In ISO 15118 that
matters exactly once — ACDP is the only set where two global elements share a named type, because ISO
commented out `ACDP_DisconnectReqType`/`ResType` and pointed the elements at the Connect types.

**What it cost:** `ACDP_DisconnectReq` was unreadable, and `ACDP_ConnectRes` decoded *cleanly, as
`ACDP_DisconnectReq`* — the wrong message, with nothing anywhere to report it. That is the worst
failure mode in the set and the reason this one was not a close call.

## B — the WPT mid-run particle grammar

Never an interpretation question. cbexigen's grammar for `VendorSpecificDataContainer`
(`minOccurs="0" maxOccurs="16"`) followed by an optional `WPT_LF_DataPackageList` contradicts its own
input schema: it unrolls two list positions and stops, and it hides the following particle from the
zero-item state. Two documents ISO permits therefore cannot be encoded at all — a third container item,
and the suffix without a preceding item. Reproducing that grammar meant our encoder *refused messages
the standard allows*, which is a worse thing for a conformance tool to do than to differ by a byte.

The visible symptom was one event code: with both absent, cbexigen's end-element is `1` and the
schema's is `2`. That is the single bit that changed in all four frames — last byte `0x20`→`0x40`,
`0x02`→`0x04`.

A test that asserted the old refusal is now a round-trip test of the message it used to reject.

## Why the trade was one-sided

- **Nothing is on the wire.** ACDP and WPT are exactly the sets the interop matrix marks *codec only —
  no independent stack implements session state machines for them*, cbexigen-derived stacks included.
  The byte compatibility given up here is compatibility with an encoder nobody runs for these messages.
- **The evidence improves.** Those six vectors were cbV2G's output checked against cbV2G. They are now
  checked through EXIficient, which shares no line with cbexigen. The corpus header of each file records
  which vectors deviate and why, so nothing is silently re-based.
- **It is reversible.** Both switches stay, the generator's defaults stay `CbV2GCompatible`, and
  `Directory.Build.props` carries the reasoning where anyone changing it will read it.
- **The blast radius was measured, not assumed.** Setting both properties solution-wide moved exactly
  six vectors and no others — every `-2`, CommonMessages, AC, DC and DER expected byte is unchanged,
  which is what the two constructs' analysis predicted.

## The corpus, before and after

| | first run | after C | after A and B |
|---|---:|---:|---:|
| byte-exact | 332 | 333 | **339** |
| mismatches | 9 | 9 | **8** |
| unreadable | 6 | 5 | **0** |

The eight remaining mismatches are seven `ServiceDetailRes` and one `AuthorizationReq`, all with the
value-partition shape recorded for `-2` in
[`ExiStringTableTests`](../../../ISO15118ConformanceTests.Simulation/Interop/ExiStringTableTests.cs):
EXIficient uses the EXI value partition for a string that occurs twice in a document, our encoder is
deliberately miss-only. `ACDP_ConnectRes` used to be a ninth and was never a partition difference at
all — it was the other half of A, and it cleared the moment A was decided. That is the confirmation
that the diagnosis was right.

## Reproducing

```bash
python3 tools/interop-exificient/roundtrip20.py \
    libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EXI_Tests/Vectors/Iso15118_20.*.vectors.json \
    ISO15118ConformanceTests.Simulation/Vectors/Session.iso20-*.trace.json
```

To see the numbering rule on its own, without ISO's schemas anywhere in it, encode the three-element
schema above through `Roundtrip20`'s XML probe and read the two bits after the one-byte EXI header.

## Next

- **File both with libcbv2g.** They are now defect reports, not differences of opinion: A departs from
  EXI 1.0 §8.5.1, and B contradicts the generator's own input schema. B is the more serious of the two
  for their users, since it makes valid documents unencodable.
- ~~**The value partition**~~ — **done the same day.** All eight are the string table, shown by
  substitution: [`2026-08-08-value-partition-20`](../2026-08-08-value-partition-20/notes.md). The
  off-by-one was real (an identifier costs bits of its own), and a repeated certificate turns out to be
  worth nothing, because `base64Binary` never enters the table. The `-20` corpus now has no unexplained
  frame in it.
