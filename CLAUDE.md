# ISO15118ConformanceTests

Conformance & interoperability test suite for the ISO 15118 stack in the `EVSimulatorApp` submodule
(EXI codec, EV↔EVSE state machines, TLS/PKI, Plug & Charge). This repository is the harness that
holds the app to account against independent stacks — Josev, EVerest, EVDriveFlow, TuxEVSE.

## Orientation

- **What is proven, and how:** `README.md` — the interop-status ledger.
- **Per-run write-ups and frame logs:** `docs/interop-runs/`.
- **The stack under test:** `EVSimulatorApp/` — its own README documents the codec and the simulation.

## Build & test

```
git submodule update --init --recursive
dotnet test -c Release
```

Must pass green without a C toolchain, Java, or network — record-mode interop checks replay frames
under `ISO15118ConformanceTests.Simulation/Traces/`, and the loopback E2Es run both peers in-process.
The ISO schemas must be present in `EVSimulatorApp/libs/WWCP_ISO15118/**/Schemas/` (see the app's
`tools/download-schemas.sh`).

## Ground rules

- The codec, the simulation library and the CLI are the app's — change them in `EVSimulatorApp/`,
  not here. This repository holds tests, recorded traces, and run notes.
- Live interop tests are `[Explicit]` and stay out of the offline run — they need the other stack on
  the wire.
- Never change wire semantics speculatively — only on a concrete byte diff against a reference
  encoder. That oracle corpus and the rule live with the codec, in the app.
