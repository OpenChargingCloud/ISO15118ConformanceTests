# Vanaheimr.V2G.Exi.Prototype

Prototype EXI codec for ISO 15118 AppProtocol messages, .NET 10, AOT-friendly.
This iteration adds a vector-driven xUnit test suite so wire-format conformance
can be progressively raised by replacing self-encoded seed vectors with cbV2G
reference output.

## Structure

```
Vanaheimr.V2G.Exi.sln
├─ Vanaheimr.V2G.Exi.Prototype/         class library
│   ├─ Exi/                             BitReader, BitWriter, ExiPrimitives
│   ├─ V2GTP/                           8-byte transport header
│   └─ AppProtocol/                     Messages + hand-written codec
└─ Vanaheimr.V2G.Exi.Tests/             NUnit test project
    ├─ Infrastructure/
    │   ├─ HexUtil.cs                   hex parse + bit-level diff for failures
    │   ├─ VectorFile.cs                JSON DTOs
    │   └─ AppProtocolVectorSource.cs   [TestCaseSource] data + input binder
    ├─ Vectors/
    │   ├─ AppProtocol.vectors.json     seed vectors (self-encoded)
    │   └─ REPLACING_SEED_VECTORS.md    workflow for cbV2G upgrade
    ├─ ExiPrimitiveTests.cs             schema-less primitive coverage
    ├─ V2GTPFrameTests.cs               header roundtrip / rejection
    └─ AppProtocolVectorTests.cs        encode/decode/roundtrip per vector
```

## Run

```
dotnet test -c Release
```

## What "green" means today

The seed vectors in `Vectors/AppProtocol.vectors.json` were produced by a Python
simulator that mirrors the C# codec line-for-line. A green test run therefore
proves only **internal self-consistency**:

- `BitReader` and `BitWriter` are inverses.
- The codec's encode and decode paths are inverses.
- The C# implementation matches the Python reference in every detail.

What it does **not** yet prove:

- That the bytes match what an ISO 15118 EVSE actually expects on the wire.
- That a different ISO 15118 stack (cbV2G, OpenV2G, RISE V2G, Josev) accepts
  this output, or that we can decode theirs.

The plan to bridge this gap is in `Vectors/REPLACING_SEED_VECTORS.md` — short
version: build cbV2G as a small CLI, replay each vector through it, and patch
the resulting hex back into `AppProtocol.vectors.json`. Pin the cbV2G commit so
the conformance claim is reproducible.

## Failure output

When `expected != actual` the test names the vector, prints both byte
sequences, and pinpoints the first differing byte plus the bit-position within
that byte (MSB-first, matching the EXI bit-packed convention). This is the
output you want when staring at the third byte of `0xFE` wondering whether the
Priority field shifted by one.

## What this prototype still does NOT do

- Source generator (still hand-written codec, but in the exact shape the
  generator should emit).
- Wire-format validated against any external encoder.
- EXI string value tables (only "miss" — fine for AppProtocol, blocker for -2/-20).
- Lenient decoder mode for interop debugging.
- Header options document (AppProtocol doesn't use it; ISO 15118-20 may).

## Next milestones

1. **Replace seed vectors with cbV2G output** (see `REPLACING_SEED_VECTORS.md`).
2. EXI W3C testsuite for the primitives layer.
3. String value tables (local + global partitions).
4. `IIncrementalGenerator` consuming `.xsd` via `<AdditionalFiles>`, emitting
   the codec shape that's currently hand-written.
5. ISO 15118-20 codec in a separate assembly.
