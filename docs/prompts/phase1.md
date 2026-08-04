# Task: complete the EXI primitive layer (Phase 1)

## Context

You're working in the repo `D:\Coding\OpenChargingCloud\Vanaheimr.V2G.Exi` — a .NET 10 library
intended, long-term, to parse and serialize ISO 15118-2 and 15118-20 EXI messages
(goal: EV↔EVSE simulation). Current state:

- `WWCP_ISO15118_EXI/Exi/` — `BitReader`, `BitWriter` (bit-packed, MSB-first)
  and `ExiPrimitives` (unsigned integer, n-bit unsigned, string only in the "miss" case).
- `WWCP_ISO15118_EXI/AppProtocol/` — hand-written SupportedAppProtocol codec.
- `WWCP_ISO15118_EXI_SourceGenerator/` — Roslyn IIncrementalGenerator (XSD → codec),
  secured by a diff test against the hand-written codec. Do NOT touch it in this phase,
  unless an API change to the primitives forces it.
- `WWCP_ISO15118_EXI_Tests/` — NUnit, vector-driven, 71 tests green.
  `dotnet test -c Release` must still be fully green at the end.

Read before starting: `README.md`, `Exi/ExiPrimitives.cs`, `Exi/BitReader.cs`, `Exi/BitWriter.cs`,
`AppProtocol/SupportedAppProtocolCodec.cs`, and the test infrastructure under `Tests/Infrastructure/`.

## Goal of this phase

Complete the schema-less EXI primitive layer so it's a solid foundation for
ISO 15118-2/-20 codecs. Authoritative reference: the W3C specification
"Efficient XML Interchange (EXI) Format 1.0 (Second Edition)".
Relevant EXI options for the 15118 world: bit-packed, schema-informed strict,
no options document (header = 0x80), `valueMaxLength` and `valuePartitionCapacity`
unbounded — meaning string value tables are ACTIVE and a normative requirement.

### 1. String value tables (the core piece)

Implement the value partitions per EXI spec §7.3.3:

- **Local partition** per QName (in our context: per element, identified via a
  caller-supplied key, e.g. an int handle — the grammar layer knows the QNames)
  and **global partition** per stream.
- Encoding a string value:
  - **Local hit:** `UnsignedInteger(0)`, then a compact ID as an n-bit unsigned with
    n = ⌈log₂(m)⌉, m = current size of the local partition.
  - **Global hit:** `UnsignedInteger(1)`, then a compact ID with n = ⌈log₂(g)⌉,
    g = current size of the global partition.
  - **Miss:** `UnsignedInteger(len + 2)` + code points (as implemented today);
    afterward add the value to BOTH partitions (on decode too!).
  - Note the EXI convention: a partition of size 1 → compact ID with 0 bits.
  - This needs a stream-context object (e.g. `ExiStringTable` or
    `ExiEncoderContext`/`ExiDecoderContext`) passed by `ref`/instance alongside
    `BitReader`/`BitWriter` through the codec calls. Design the API so the source
    generator can call it mechanically later.
- The existing AppProtocol codec must be migrated to the new API and must produce
  **byte-identical output** to today (AppProtocol contains no repeated strings,
  so the wire form doesn't change — the existing vector tests prove this).

### 2. Missing EXI data types

Implement in `ExiPrimitives` (encode + decode for each, with an XML doc comment
explaining the spec location and bit layout — same style as the existing code):

- **Signed integer** (§7.1.5): 1 sign bit + unsigned integer;
  negative: magnitude = value − (−1), i.e. `(-v) - 1`.
- **Binary** (§7.1.1): `UnsignedInteger(byteCount)` + raw bytes
  (covers xs:hexBinary and xs:base64Binary — the distinction is purely lexical,
  identical on the wire).
- **Boolean** (§7.1.2): 1 bit (without a pattern facet; the 2-bit variant with
  a facet isn't needed by the 15118 schemas).
- **Float, decimal, dateTime: do NOT implement.** The 15118-2/-20 schemas don't use
  them (physical values are modelled as multiplier/value integer pairs).
  Instead, leave a short note in the code/README that they're deliberately absent.

### 3. Tests (the actual proof of value)

- **Hand-computed vectors:** for every new data type, test cases with hand-derived
  bit sequences (boundary values: 0, ±1, the 7-bit boundaries 127/128, ulong.MaxValue,
  long.MinValue, empty binary, empty string).
- **Value-table scenarios:** the same string twice in the same element (local hit),
  in different elements (global hit), interleaving hits and misses,
  compact-ID bit-width growth (1→2→4 entries), an encode→decode roundtrip,
  and: the decoder throws a clean `InvalidDataException` on a hit index outside the
  partition (no crash, no infinite loop).
- **Property-based roundtrips:** add CsCheck (or FsCheck) as a test dependency:
  arbitrary values → encode → decode → identical; for strings including non-BMP code
  points (surrogate pairs), for signed integer the full ± range.
- **EXIficient diff oracle (prepare, don't block on it):** create a
  `Primitives.vectors.json` under `Tests/Vectors/` in the style of the existing vector
  file, with a `referenceEncoder` field analogous to `REPLACING_SEED_VECTORS.md`. The
  values may initially come from our own implementation and must be marked as such
  (`generatorNote`), plus a short guide (markdown next to the file) on how to
  regenerate them with EXIficient/V2Gdecoder. Don't force a Java setup in this phase.
- All 71 existing tests stay green, in particular `GeneratedCodecDiffTests` and
  the AppProtocol vector tests (the wire format must not change).

## Guardrails

- .NET 10, AOT-friendly: no reflection, minimize allocations
  (value tables naturally need to allocate; Dictionary/List are fine).
- Follow the repo's code style: records, thorough XML doc comments
  explaining the EXI bit layout and spec references; no need for German-language
  commit messages.
- Work test-first where it makes sense; small, traceable commits are welcome,
  but only commit when the build is green.
- At the end, update `README.md`: adjust the "What this prototype still does NOT do"
  and "Next milestones" sections to the new state.

## Definition of Done

1. `dotnet test -c Release` fully green (existing + new tests).
2. Value tables: hit/miss implemented on both sides, covered by the
   scenario tests listed above.
3. Signed integer, binary, boolean implemented and backed by hand-computed vectors.
4. AppProtocol wire format byte-identical to before.
5. Property-based roundtrip tests run in the normal test suite.
6. README + vector docs updated.
