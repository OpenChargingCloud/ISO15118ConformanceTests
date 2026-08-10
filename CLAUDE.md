# ISO15118ConformanceTests

Conformance & interoperability test suite for the ISO 15118 stack in the `EVSimulatorApp` submodule
(EXI codec, EV↔EVSE state machines, TLS/PKI, Plug & Charge). This repository is the harness that
holds the app to account against independent stacks — Josev, EVerest, EVDriveFlow, TuxEVSE.

## Orientation

- **What is proven, and how:** `README.md` — the interop matrix, per counterparty and scenario.
  **What is not:** `docs/open-work.md`, derived from that matrix. Read it before proposing work —
  the `## Next` sections in `docs/interop-runs/` are per-run snapshots, not a to-do list, and a
  later run closes an item without editing the earlier note.
  Each column has a long form of its own, `docs/<counterparty>-cross-validation.md`: Josev (the
  independent codec), EVerest (the independent charger, and what it found in us), eVDriveFlow (the
  second codec, and the FAILED-response finding), tux-evse (the replayer).
- **Per-run write-ups and frame logs:** `docs/interop-runs/`.
- **What the standard actually requires:** `docs/normative-basis.md` — which requirement text is
  available (locally, never in the repo), how much weight each document carries, and the rule for
  citing it: clause IDs and paraphrase, never ISO prose. Consult it before recording anything as
  "not decidable" — several such notes were decidable and are now decided.
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
captured frames (`WWCP_ISO15118_EXI_Tests`, the stack's codec tests, carried in this solution), the
session corpus under `ISO15118ConformanceTests.Simulation/Vectors/` guards our own wire output,
`WWCP_ISO15118_Session_Tests` unit-tests the transport's own decisions (carried here for the same
reason as the codec tests: the offline gate is this solution), and the loopback E2Es run both peers
in-process. Four assemblies, 1 370 tests.

## Ground rules

- **The stack is not here.** The codec, the session state machines (`WWCP_ISO15118_Session/`) and the
  two runnable peers (`WWCP_ISO15118_SECC/`, `WWCP_ISO15118_EVCC/` — one program per role, each with
  its own solution and README) all live in `libs/EVSimulatorApp/libs/WWCP_ISO15118/` — change them there.
  `libs/EVSimulatorApp/` above it is the apps and the language ports; the one thing of ours still in it
  is `simulation/EVSimulatorApp.Ocpp/`, a stub of a *different* protocol that reaches the stations
  through `ISessionBackend`. This repository holds tests, recorded traces, and run notes.
  (The state machines moved out of the app on 2026-08-08 — anything that says `simulation/` is stale.)
- Live interop tests are `[Explicit]` and stay out of the offline run — they need the other stack on
  the wire.
- Never change wire semantics speculatively — only on a concrete byte diff against a reference
  encoder. That oracle corpus and the rule live with the codec.
