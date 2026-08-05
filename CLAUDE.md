# ISO15118ConformanceTests

Conformance & interoperability test suite for the ISO 15118 stack in the `EVSimulatorApp` submodule
(EXI codec, EV↔EVSE state machines, TLS/PKI, Plug & Charge). This repository is the harness that
holds the app to account against independent stacks — Josev, EVerest, EVDriveFlow, TuxEVSE.

## Orientation

- **What is proven, and how:** `README.md` — the interop-status ledger.
- **Per-run write-ups and frame logs:** `docs/interop-runs/`.
- **The stack under test:** `libs/EVSimulatorApp/` — its own README documents the codec and the simulation.

## Build & test

```
git submodule update --init --recursive
bash libs/EVSimulatorApp/libs/WWCP_ISO15118/tools/download-schemas.sh
dotnet test -c Release
```

The middle step is not optional: the source generators run at build time from the ISO schemas in
`libs/EVSimulatorApp/libs/WWCP_ISO15118/**/Schemas/`, and those are ISO's — not redistributed here, so a
fresh clone has only the placeholder `README.md` in each `Schemas/` and the build stops at
`EXIGEN001`. Running the script is you accepting the ISO Customer Licence Agreement;
`SCHEMA_CACHE=<dir>` lays out a copy you already have instead of fetching.

Must pass green without a C toolchain, Java, or network — the record-mode cross-checks replay Josev's
captured frames (`WWCP_ISO15118_EXI_Tests`, the app's codec tests, carried in this solution), the
session corpus under `ISO15118ConformanceTests.Simulation/Vectors/` guards our own wire output, and
the loopback E2Es run both peers in-process.

## Ground rules

- The codec, the simulation library and the CLI are the app's — change them in `libs/EVSimulatorApp/`,
  not here. This repository holds tests, recorded traces, and run notes.
- Live interop tests are `[Explicit]` and stay out of the offline run — they need the other stack on
  the wire.
- Never change wire semantics speculatively — only on a concrete byte diff against a reference
  encoder. That oracle corpus and the rule live with the codec, in the app.
