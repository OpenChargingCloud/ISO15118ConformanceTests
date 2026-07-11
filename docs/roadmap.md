# Current status assessment

The solution builds cleanly, all **71 tests are green** (`dotnet test -c Release`). What exists today:

| Component | State |
|---|---|
| [BitReader/BitWriter](Vanaheimr.V2G.Exi.Prototype/Exi/BitReader.cs) | Bit-packed streams, MSB-first — a solid foundation |
| [ExiPrimitives.cs](Vanaheimr.V2G.Exi.Prototype/Exi/ExiPrimitives.cs) | Unsigned integer, n-bit unsigned, string only in the "miss" case — **no value tables** |
| [V2GTP.cs](Vanaheimr.V2G.Exi.Prototype/V2GTP/V2GTP.cs) | 8-byte transport header |
| [SupportedAppProtocolCodec.cs](Vanaheimr.V2G.Exi.Prototype/AppProtocol/SupportedAppProtocolCodec.cs) | Hand-written SAP codec with cleanly documented grammar |
| [SourceGenerator](Vanaheimr.V2G.Exi.SourceGenerator/ExiCodecGenerator.cs) | `IIncrementalGenerator`: XSD → grammar plan → C# codec; secured by a diff test against the hand-written codec |
| Test infrastructure | Vector-driven (JSON), bit-exact diff on failure — exactly the right architecture |

The decisive weakness is stated honestly in the README: the seed vectors are **self-encoded**. Green only proves internal consistency, not wire conformance. On top of that, the [XsdReader](Vanaheimr.V2G.Exi.SourceGenerator/Xsd/XsdReader.cs) only understands the tiny XSD subset AppProtocol needs — the real 15118 schemas blow past that immediately.

# What -2 and -20 additionally require

**ISO 15118-2** (one schema set: `V2G_CI_MsgDef` + MsgHeader/MsgBody/MsgDataTypes + XMLDSig):
- All ~36 messages sit inside one `V2G_Message` wrapper; the body is a **substitution group** over an abstract `BodyElement` — the generator can't do that today.
- **Attributes** (AT events, e.g. `Id` for signatures), **xs:choice**, abstract types (`EntryType`/`IntervalType`), `maxOccurs="unbounded"`.
- Data types: `hexBinary` (SessionID), `base64Binary` (XMLDSig), **signed** integers (EXI encoding: sign bit + unsigned), `short`/`byte` for `PhysicalValueType`.
- **XMLDSig over EXI fragment grammars**: for Plug & Charge (AuthorizationReq, MeteringReceiptReq), the referenced body element must be canonically encoded as an EXI *fragment*, hashed, and the `SignedInfo` itself EXI-encoded and signed. This is notoriously the hardest part of 15118 — and unavoidable given the target picture (EV↔EVSE simulation with PnC).
- EXI options are fixed (bit-packed, strict, schema-informed, header `0x80`), but `valuePartitionCapacity` is unbounded → **string value tables (local + global) are a normative requirement**, even though strings rarely repeat in practice. A conformant decoder must be able to read hits.

**ISO 15118-20** (multiple schema sets: CommonMessages, AC, DC, WPT, ACDP + CommonTypes + XMLDSig):
- No more `V2G_Message` wrapper; every message is a global element with its own header (SessionID, TimeStamp, optional Signature).
- **One EXI grammar set per namespace** — the decoder selects the grammar via the V2GTP payload type (each message set has its own payload-type IDs) or the SchemaID negotiated via SAP. Architecturally that means one generated codec assembly per schema set, plus a dispatcher.
- More messages, deeper nesting, `RationalNumberType`, multiple signatures, stricter crypto suites. Bidirectional charging (Scheduled/Dynamic mode) makes the state machine bigger, but not the codec more complicated — the codec requirement is "the whole XSD subset correctly," not "new EXI features."

The roadmap in the README (replace vectors → value tables → extend generator → -20) is fundamentally right; below I flesh it out and extend it, mainly around fragment grammars and the multi-schema architecture.

# Plan for the next steps

**Phase 0 — Prove the foundation (make SAP wire-conformant)**
1. Build `libcbv2g` as a small CLI (JSON in → EXI hex out and back), pin the commit, regenerate all SAP seed vectors — exactly the workflow from [REPLACING_SEED_VECTORS.md](Vanaheimr.V2G.Exi.Tests/Vectors/REPLACING_SEED_VECTORS.md). Only after that is "green" a proof of conformance.
2. Close the vector gaps (priority bounds, the 20-entry case, non-ASCII namespaces — the list is already in the repo).

**Phase 1 — Complete the EXI primitive layer**
3. **String value tables** (local + global partition) with a stream-context object; compact-ID bit widths based on partition size. Mandatory on the decoder side, for canonicity on the encoder side.
4. Remaining EXI data types: signed integer, binary (hex/base64), boolean, generic enumeration; float/decimal/dateTime only if the schemas actually reference them (decide after the XSD inventory, not speculatively).
5. Secure the primitives against the **W3C EXI test suite** or EXIficient output.

**Phase 2 — Lift the generator to 15118-schema reality**
6. Extend XsdReader/GrammarBuilder: `xs:import`/`include` across multiple files and namespaces, attributes (AT events, lexicographic ordering), `xs:choice`, substitution groups + abstract elements, `unbounded`, anonymous types. Build the grammar per EXI spec §8.5.4 instead of today's ad-hoc patterns.
7. Target picture: **one generated assembly per schema set** (`…Exi.AppProtocol`, `…Exi.Iso15118_2`, `…Exi.Iso15118_20.CommonMessages`, `.DC`, `.AC`; DIN 70121 comes along almost for free and is valuable for field interop).
8. Milestone test: generate `V2G_CI_MsgDef.xsd` (-2) completely, differentially test `SessionSetupReq/Res` as the first real message against cbV2G, then build message by message.

**Phase 3 — Complete 15118-2 + XMLDSig**
9. All -2 messages with vector coverage; `PhysicalValueType` helpers.
10. **Fragment grammar encoding** for signed body elements + EXI-encoded `SignedInfo`; wire up .NET crypto (ECDSA P-256/SHA-256 for -2). Validate against signature examples from RISE-V2G/Josev.

**Phase 4 — 15118-20**
11. CommonMessages first (SessionSetup → ServiceDiscovery → Authorization → ScheduleExchange), then DC, then AC; WPT/ACDP as needed. Payload-type dispatcher in the V2GTP layer.

**Phase 5 — EV↔EVSE simulation**
12. SDP (UDP discovery), TCP/TLS session loop, minimal EVCC and SECC state machines (happy path AC + DC). Final test: your EVCC simulation against the **Josev SECC** and vice versa — this validates codec, V2GTP, sequencing, and timing all at once.

# Reference libraries for automated testing

I'd deliberately combine **three classes of oracles**, because they have independent sources of error:

1. **[EVerest/libcbv2g](https://github.com/EVerest/libcbv2g)** + generator **[cbexigen](https://github.com/EVerest/cbexigen)** (C, Apache-2.0) — *primary diff oracle*. Covers DIN 70121, -2, and -20, is actively maintained, and runs in production in the EVerest stack. As a CLI/Docker harness: same input → byte diff. Fast enough for CI on every commit. The XSDs ship with the cbexigen repo — this also solves your schema-sourcing problem.
2. **[EXIficient](https://github.com/EXIficient/exificient)** (Java, generic W3C EXI processor) — *spec oracle*. Important as a counter-check because cbV2G is a specialized code generator with its own simplifications: where both independently produce the same byte, confidence is high. EXIficient also fully supports value tables and **fragment encoding** — for Phase 1 and 3 it's the only usable reference tool. Practical wrapper: [FlUxIuS/V2Gdecoder](https://github.com/FlUxIuS/V2Gdecoder) (hex ↔ XML as a CLI). The old Python stacks internally use exactly this codec (`EXICodec.jar`), meaning "testing against Josev" tests EXIficient on the codec side.
3. **[SwitchEV/iso15118 (Josev Community)](https://github.com/SwitchEV/iso15118)** (Python, Apache-2.0, -2 and -20) — *session-level oracle*. Slow, but ideal as a counterpart for the end-to-end simulation (Phase 5): a complete SECC/EVCC including SDP and TLS. The EVerest fork [ext-switchev-iso15118](https://github.com/EVerest/ext-switchev-iso15118) is the more actively maintained variant.

Additionally, with a clearly bounded role:
- **[OpenV2G](https://github.com/Martin-P/OpenV2G)** (C, LGPL) — historical reference for DIN/-2, good as a third byte-level vote in disputed cases; no -20, effectively frozen.
- **RISE-V2G** (Java, archived, -2 only) — a treasure trove of PnC signature test data and a second full -2 stack.
- **[EVerest/libiso15118](https://github.com/EVerest/libiso15118)** (C++, -20-focused) — as a second -20 counterpart for the simulation.

**Test strategy for this:** wrap the reference encoders in Docker, check in generated vectors with a pinned commit as JSON (CI runs offline against the vectors; a separate, manually triggered job regenerates them). In addition, internal property-based roundtrip tests (e.g. CsCheck: arbitrary message → encode → decode → equal) and fuzzing the decoder with random bytes (clean errors instead of crashes) — the reference oracles don't catch that.

The single biggest chunk of work in the plan is Phase 2 (real grammar construction per the EXI spec); the biggest risk is Phase 3 (fragment signatures). Both can be kept well under control through early differential testing against two independent oracles — the test infrastructure you need for that already exists in this repo at its core.

Sources: [EVerest/libcbv2g](https://github.com/EVerest/libcbv2g), [EVerest/cbexigen](https://github.com/EVerest/cbexigen), [EVerest/libiso15118](https://github.com/EVerest/libiso15118), [SwitchEV/iso15118](https://github.com/SwitchEV/iso15118), [EVerest/ext-switchev-iso15118](https://github.com/EVerest/ext-switchev-iso15118), [chargebyte on cbexigen](https://chargebyte.com/artikel/bidirectional-charging-chargebyte-overcomes-exi-hurdle-with-release-of-own-open-source-software)
