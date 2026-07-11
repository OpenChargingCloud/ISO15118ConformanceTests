# Vanaheimr.V2G.Exi

.NET 10 library for ISO 15118 EXI: parsing and serializing 15118-2 and
15118-20 messages, ultimate goal EV↔EVSE simulation.

## Orientation

- **Overall plan / current status:** `docs/roadmap.md`
- **Phase prompts for agent runs (Phase 0–5):** `docs/prompts/` (index: `docs/prompts/README.md`)
- Project structure and current prototype status: `README.md`

## Build & test

```
dotnet test -c Release
```

Must pass green without a C toolchain, Java, or network — external reference encoders
(cbV2G, EXIficient) are only used for vector (re-)generation, never for the test run itself.

## Ground rules

- Never change wire semantics speculatively — only based on a concrete byte diff
  against a reference encoder (vector files under `Vanaheimr.V2G.Exi.Tests/Vectors/`).
- Source generator: fail-loud philosophy — unknown XSD constructs produce
  build diagnostics, never get silently skipped.
- No hand-written codec code for -2/-20; everything runs through the generator.
  The hand-written AppProtocol codec remains as a diff reference.
