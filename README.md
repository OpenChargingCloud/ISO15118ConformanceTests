# ISO/IEC 15118 Conformance & Interoperability Test Suite

The conformance and interoperability tests for the
[WWCP_ISO15118](libs/EVSimulatorApp/libs/WWCP_ISO15118) ISO 15118 stack — its EXI codec, its EV↔EVSE
state machines, its TLS and PKI, its Plug & Charge. It is carried here as a submodule of a submodule,
inside [EVSimulatorApp](libs/EVSimulatorApp); this repository is the harness that proves it behaves
the way the standard and the independent stacks in the field expect.

The point of separating the two: the app can be built and shipped on its own, and the thing that
judges it — the corpus of recorded frames, the loopback E2Es, the live cross-checks against Josev and
EVerest — lives beside it rather than inside it, so "does our stack interoperate" is a question this
repository answers and the app does not have to carry. Here is the answer.

## What is here

```
ISO15118ConformanceTests.slnx
├─ ISO15118ConformanceTests.Simulation/   the conformance suite proper
│   ├─ Interop/     live cross-checks vs Josev, EVerest, EVDriveFlow, TuxEVSE (all [Explicit])
│   ├─ E2E/         full-stack loopback sessions (SLAC→SDP→TLS→SAP→-2/-20) to SessionStop
│   ├─ StateMachines/, Discovery/, Framing/, Metering/, Sap/, Slac/, Timing/, Transport/
│   ├─ Vectors/     the recorded session corpus the offline tests replay
│   └─ Traces/      the replay and recording machinery behind it
├─ ISO15118ConformanceTests.Pqc/          post-quantum-crypto experiment tests (ML-KEM/ML-DSA)
└─ libs/EVSimulatorApp/                   the stack under test, as a submodule
    ├─ …/WWCP_ISO15118_EXI_Tests/         the stack's codec tests — the byte-exact cbV2G and Josev
    │                                     oracle, carried into this solution so the offline run
    │                                     judges against a foreign encoder and not only ourselves
    └─ …/WWCP_ISO15118_Session_Tests/     and its transport unit tests, carried for the same reason:
                                          the offline gate runs here, so a test that runs only in
                                          the app's solution is one this repository cannot vouch for
```

Two oracles judge the bytes offline, and only one of them is independent of us: **cbV2G** generated the
vector corpus and shares a generator lineage with EVerest and tux-evse, while **EXIficient** — Josev's
codec — does not. Since 2026-08-07 it is driven over both halves of the corpus:
[`tools/interop-v2gdecoder/`](tools/interop-v2gdecoder/README.md) for `-2` and DIN,
[`tools/interop-exificient/`](tools/interop-exificient/README.md) for `-20`, where it found six frames a
second codec cannot read at all.

The implementation is **not** here. Since 2026-08-08 the codec *and* the session state machines and the
CLI live together in `libs/EVSimulatorApp/libs/WWCP_ISO15118/` — that submodule is the ISO 15118 stack,
`libs/EVSimulatorApp/` above it is the apps and the language ports, and this repository is the evidence.
Read [`WWCP_ISO15118`'s own README](libs/EVSimulatorApp/libs/WWCP_ISO15118/README.md) for how the stack
works; this one is about how it is held to account.


## The interop matrix — who we test against, and what happened

Four independent stacks sit on the other end of the live cross-checks (`JosevInteropTests`,
`EverestInteropTests`, `EvDriveFlowInteropTests`, `TuxEvseInteropTests`; all `[Explicit]`). They are
worth having *because* they differ — each brings a different EXI lineage and a different kind of
evidence:

| | [Josev](tools/interop-josev/README.md) | [EVerest](tools/interop-everest/README.md) | [eVDriveFlow](tools/interop-evdriveflow/README.md) | [tux-evse](tools/interop-tux-evse/README.md) |
|---|---|---|---|---|
| Who | SwitchEV (Python) | LF Energy — the stack on real chargers | EDF Lab | IoT.bzh (Rust) |
| **Their station**, for our `EV→` runs | their SECC — **EXIficient** | `EvseV2G` · `Evse15118D20` · `IsoMux` — **cbV2G** (OpenV2G in the 2023 image) ([details](docs/matrix/everest-iso2-dc-eim.md)) | their SECC — **OpenEXI**/Nagasena | a responder replaying a **captured Audi** |
| **Their EV**, for our `←SECC` runs | their EVCC — **EXIficient** | `PyEvJosev` — **EVerest's fork of Josev**, so EXIficient again ([details](docs/matrix/josev-is-everests-ev.md)) | their EV — **OpenEXI** | their **injector**, replaying captured cars (an Audi, a VW) ([details](docs/matrix/tux-iso2-dc-reverse.md)) |
| Versions met | current | 2023.10.0 · **2025.10.0** · **2026.02.1** (source build) | `60249c3` | v0.1 image · **`main` `fc51088`** (source build) |
| Directions | `EV→ ←SECC` throughout | `EV→` throughout · `←SECC` only where it adds something ([details](docs/matrix/josev-is-everests-ev.md)) | `EV→ ←SECC` | `EV→ ←SECC` |

The **Ours** column is our own C# stack against itself — a loopback E2E with both peers ours, which is
what runs in the offline suite and what every counterparty column is measured against. It says the
scenario exists and is guarded here; it says nothing about conformance, because both ends share our
assumptions. That is the whole reason for the columns to its right. Sessions recorded from the live runs
replay offline as part of the suite too, so the matrix does not rot silently when the code moves.
Evidence per cell lives under [`docs/interop-runs/`](docs/interop-runs/); the run-notes README explains
how to read one.

**Status:** ✅ complete live session &nbsp;·&nbsp; ◐ partial — the session ran only to the stated point,
**or** it ran through and one property is not checkable against that counterparty; the cell says which
&nbsp;·&nbsp; ⛔ blocked by a counterparty defect or limitation &nbsp;·&nbsp; ▢ not attempted yet
&nbsp;·&nbsp; — not applicable / not implemented on their side

**Which side is ours** — the arrow points the way the session is driven, and the label names *our* role:

| | |
|---|---|
| **`EV→`** | our C# **EVCC** drives *their* station. The "forward" direction: we are the car. |
| **`←SECC`** | *their* EV drives our C# **SECC**. The "reverse" direction: we are the charging station. |
| **`EV→ ←SECC`** | both, in separate sessions. |

**Transport rows and application rows** — the distinction that decides how two neighbouring cells relate.
The TLS row of each table (`TLS 1.2 (unilateral)` for `-2`, `Mutual TLS 1.3` for `-20`) measures the
**transport**: the version, the prescribed cipher suites, who authenticates, and whether the peer's chain
builds to a root we trust. *Unilateral* means only the station presents a certificate, which is what `-2`
prescribes; `-20` wants both sides and has its own row. Every other row is the **application layer riding
on that transport**. So `Plug & Charge` is not a second TLS measurement: `-2` forbids `Contract` without
TLS, so that row **presupposes** the transport row and measures the contract credential —
`PaymentDetails`, the signed `AuthorizationReq`, the station's verdict. One session can be ✅ in the
transport row and carry a second marker in the application row; read them as two questions, not two
verdicts on one thing.

**Two markers in one cell** mean the scenario runs and something in it does not: the first names what
works, the second what is missing. `✅ complete charge · ▢ nothing validates the contract` is a session
that ran end to end with one property nobody can check — not a failure, and not a clean pass either.

---

# We as an EVCC (Electric Vehicle)

Our C# **EVCC** drives *their* station. `Ours` is the loopback, where the station is ours too.

**ISO 15118-2**

| Scenario | Ours (C# loopback) | Josev | EVerest | eVDriveFlow | tux-evse |
|---|---|---|---|---|---|
| AC, EIM | ✅ `Iso2LoopbackTests` | ✅ | ✅ plain TCP · ✅ over TLS 1.2 ([details](docs/matrix/everest-iso2-ac-tls12.md)) | — | — |
| DC, EIM | ✅ `Iso2LoopbackTests` | ✅ | ✅ ([details](docs/matrix/everest-iso2-dc-eim.md)) | — | ◐ stops at `SessionSetup` ([details](docs/matrix/tux-iso2-dc-forward.md)) |
| Plug & Charge | ✅ `Iso2LoopbackTests` (signed auth + metering receipts) | ✅ | ✅ 30,16 kWh charged, 81 loops · ▢ nothing validates the contract itself ([details](docs/matrix/everest-iso2-pnc-charge.md)) | — | — |
| Contract provisioning | ✅ `Iso2LoopbackTests` — Install *and* Update, key unwrapped | — not implemented | ✅ Install, with our MO backend behind their station · ⛔ Update answers `OK` from an empty handler ([details](docs/matrix/everest-iso2-cert-update.md)) | — | — |
| Pause / Resume | ✅ `Iso2LoopbackTests` | ✅ `OK_OldSessionJoined` | — | — | — |
| Signed tariffs (SalesTariff) | ✅ E2E | ✅ their MO-signed tariff verified by us | — | — | — |
| Renegotiation | ✅ `Iso2RenegotiationSequenceTests` (AC + DC through `CableCheck`) | ✅ AC, `[V2G2-841]` | ⛔ their station fails its own cable check and goes `Inoperative` ([details](docs/matrix/everest-iso2-renegotiation.md)) | — | — |
| TLS 1.2 (unilateral) | ✅ `TlsLoopbackTests` · ✅ `trusted_ca_keys` on the wire, `[V2G2-651]` ([details](docs/matrix/ours-iso2-trusted-ca-keys.md)) | ✅ | ✅ the prescribed suite, their full chain against the root alone ([details](docs/matrix/everest-iso2-ac-tls12.md)) | — | — |

**ISO 15118-20**

| Scenario | Ours (C# loopback) | Josev | EVerest | eVDriveFlow | tux-evse |
|---|---|---|---|---|---|
| DC, Scheduled, EIM | ✅ `Iso20LoopbackTests` | ✅ TCP + TLS | ✅ | ✅ 12 exchanges · ⛔ their SECC drops `DC_ChargeLoop` ([details](docs/matrix/edf-iso20-dc-scheduled.md)) | — |
| DC, Dynamic | ✅ `Evcc20DynamicModeTests` | — | ✅ | — | — |
| AC | ✅ `Iso20LoopbackTests` | — | ✅ plain TCP · ✅ over mutual TLS 1.3 ([details](docs/matrix/everest-iso20-ac-forward.md)) | — | — |
| BPT, AC + DC (incl. Dynamic) | ✅ `Evcc20BidirectionalTests`, `Evcc20BptRankingTests` | — | ✅ DC_BPT and AC_BPT, plain and over mutual TLS 1.3 ([details](docs/matrix/everest-iso20-bpt-forward.md)) | — | — |
| Plug & Charge | ✅ `Iso20LoopbackTests` | ✅ | — commented out on their side | — they implement none ([details](docs/matrix/edf-iso20-pnc.md)) | — |
| CertificateInstallation | ✅ `Iso20LoopbackTests` — full roundtrip, contract key unwrapped | — | — | — | — |
| Pause / Resume | ✅ `Iso20LoopbackTests` | ⛔ their `-20` session context stays empty ([details](docs/matrix/josev-iso20-session-context.md)) | ✅ over mutual TLS, resuming at `DcChargeParameterDiscovery` ([details](docs/matrix/everest-iso20-pause-resume.md)) | — | — |
| Signed tariffs (AbsolutePriceSchedule) | ✅ signature verified at the EV | — | — they send none, deliberately ([details](docs/matrix/everest-iso20-tariffs.md)) | — | — |
| Mutual TLS 1.3 | ✅ `MutualTlsLoopbackTests`, `BcMutualTlsLoopbackTests` | ✅ | ✅ our client on Windows ([details](docs/matrix/everest-iso20-mtls-forward.md)) | — | — |
| SDP discovery | ✅ `FullStackLoopbackTests` | ✅ | ✅ multicast and unicast | — | — |
| Multi-protocol SAP offer | ✅ `MultiProtocolSapTests` | — | ✅ all four offer shapes · ⛔ over TLS it routes a `-20` session onto TLS 1.2 ([details](docs/matrix/everest-isomux-sap.md)) | — | — |
| WPT · ACDP | ▢ codec only, independently judged ([details](docs/matrix/ours-wpt-acdp.md)) | *no independent stack implements these state machines* | | | |

**ISO 15118-20 MCS**

| Scenario | Ours (C# loopback) | Josev | EVerest | eVDriveFlow | tux-evse |
|---|---|---|---|---|---|
| MCS | ✅ `Secc20McsTests` | — | ✅ Scheduled and Dynamic | — | — |
| MCS_BPT | ✅ `Secc20McsTests` (ranking + envelope) | — | ✅ service **9**, our discharge limits read back ([details](docs/matrix/everest-mcs-bpt.md)) | — | — |

---

# We as a SECC (Charging Station / EVSE)

*Their* EV drives our C# **SECC**. `Ours` is the same loopback, seen from the station's side.

**ISO 15118-2**

| Scenario | Ours (C# loopback) | Josev | EVerest | eVDriveFlow | tux-evse |
|---|---|---|---|---|---|
| AC, EIM | ✅ `Iso2LoopbackTests` | ✅ | ✅ their EV, EIM plain ([details](docs/matrix/everest-iso2-reverse.md)) | — | ✅ a real VW's route · ✅ two Porsche routes ([details](docs/matrix/tux-iso2-ac-reverse.md)) |
| DC, EIM | ✅ `Iso2LoopbackTests` | ✅ | — | — | ✅ the full captured-Audi session ([details](docs/matrix/tux-iso2-dc-reverse.md)) |
| Plug & Charge | ✅ `Iso2LoopbackTests` | ✅ signed auth *and* signed metering receipt verified, chain at their MO root ([details](docs/matrix/josev-pnc-chains.md)) | ✅ their EV over TLS ([details](docs/matrix/everest-iso2-reverse.md)) | — | — |
| Signed tariffs (SalesTariff) | ✅ `Secc2TariffTests` | ✅ their EV consumed ours | — | — | — |
| Renegotiation | ✅ `Iso2RenegotiationSequenceTests` | ✅ AC, `[V2G2-841]` | — | — | — |
| TLS 1.2 (unilateral) | ✅ `TlsLoopbackTests` | ✅ their EV validates our chain against their V2G root ([details](docs/matrix/josev-pnc-chains.md)) | — | — | ⛔ their configs offer neither prescribed suite · ◐ unpinned, 4 exchanges ([details](docs/matrix/tux-iso2-tls.md)) |

**ISO 15118-20**

| Scenario | Ours (C# loopback) | Josev | EVerest | eVDriveFlow | tux-evse |
|---|---|---|---|---|---|
| DC, Scheduled, EIM | ✅ `Iso20LoopbackTests` | ✅ TCP + TLS | — | — | — |
| DC, Dynamic | ✅ `Secc20DynamicModeTests` | ✅ their EV adopts the mode we offer ([details](docs/matrix/josev-iso20-dynamic-reverse.md)) | ✅ with a Scheduled control arm that switches with the offer ([details](docs/matrix/everest-iso20-dynamic-reverse.md)) | ✅ 15 exchanges into the charge loop ([details](docs/matrix/edf-iso20-dynamic-reverse.md)) | — |
| AC | ✅ `Iso20LoopbackTests` | ✅ TCP + TLS | ✅ 56 exchanges, 44 charge loops — over mutual TLS 1.3 and in Dynamic ([details](docs/matrix/everest-iso20-ac-reverse.md)) | — | — |
| BPT, AC + DC (incl. Dynamic) | ✅ `Secc20AcBptTests` | ✅ their EV selects service 6 / 5 | ✅ AC_BPT *and* DC_BPT out of our catalogue, all four AC variants ([details](docs/matrix/everest-iso20-bpt-reverse.md)) | ✅ DC_BPT, both envelopes crossed ([details](docs/matrix/edf-iso20-bpt-reverse.md)) | — |
| Plug & Charge | ✅ `Iso20LoopbackTests` (signed auth verified at SECC) | ✅ contract chain at their MO root, TLS client chain at their OEM root ([details](docs/matrix/josev-pnc-chains.md)) | ✅ their EV's signed `AuthorizationReq` verified, chain at their MO root ([details](docs/matrix/everest-iso20-pnc-reverse.md)) | — they implement none ([details](docs/matrix/edf-iso20-pnc.md)) | — |
| CertificateInstallation | ✅ `Iso20LoopbackTests` | ✅ our signed response verified · ⛔ their handler ends at `NotImplementedError` | ✅ their EV's real OEM chain, built against their OEM root · ⛔ the same wall ([details](docs/matrix/everest-iso20-certinstall.md)) | — | — |
| Pause / Resume | ✅ `Iso20LoopbackTests` | — | — | — | — |
| Signed tariffs (AbsolutePriceSchedule) | ✅ `Iso20LoopbackTests` | ✅ their AC EVCC consumed our signed schedule · ▢ nothing external verifies it ([details](docs/matrix/josev-iso20-tariffs-reverse.md)) | — | — | — |
| Renegotiation | ✅ `Secc20DynamicModeTests` | ✅ a real `SessionStopReq(ServiceRenegotiation)`, `[V2G20-1477]` · ⛔ then drops the link ([details](docs/matrix/josev-iso20-session-context.md)) | ✅ the same, in **DC** · ⛔ the same drop ([details](docs/matrix/everest-iso20-renegotiation.md)) | — | — |
| Mutual TLS 1.3 | ✅ `MutualTlsLoopbackTests` | ✅ their EV's client chain anchored at their OEM root · ⛔ the leaf is their OEM provisioning certificate, not a Vehicle one ([chain](docs/matrix/josev-pnc-chains.md), [credential](docs/matrix/josev-iso20-vehicle-cert.md)) | ✅ their EV presents an OEM vehicle certificate ([details](docs/matrix/everest-iso20-ac-reverse.md)) | ✅ secp521r1 both ways ([details](docs/matrix/edf-iso20-mtls.md)) | — |
| SDP discovery | ✅ `FullStackLoopbackTests` | ✅ | ✅ their EV discovers the recording fixture ([details](docs/matrix/everest-sdp-and-mcs-reverse.md)) | ✅ their EV found our SECC | — |

**ISO 15118-20 MCS**

| Scenario | Ours (C# loopback) | Josev | EVerest | eVDriveFlow | tux-evse |
|---|---|---|---|---|---|
| MCS | ✅ `Secc20McsTests` | — | ✅ their EV picked service **8** out of our catalogue ([details](docs/matrix/everest-sdp-and-mcs-reverse.md)) | — | — |
| MCS_BPT | ✅ `Secc20McsTests` | — | — | — | — |

---


The background behind a cell — what the run showed, what it did not, and which report it
produced — is **one document per cell** under [`docs/matrix/`](docs/matrix/README.md). The
reasoning and the defects live on the counterparty's own page, linked under **Deeper reading**
below.

## Run

```
git submodule update --init --recursive
bash libs/EVSimulatorApp/libs/WWCP_ISO15118/tools/download-schemas.sh
dotnet test -c Release
```

The middle step is not optional. The source generators run at build time from the ISO schemas in the
app's WWCP submodule (`libs/EVSimulatorApp/libs/WWCP_ISO15118/**/Schemas/`), and those schemas are ISO's —
not redistributed here, so a fresh clone carries only a placeholder `README.md` in each `Schemas/` and
the build stops at `EXIGEN001`. Running the script is you accepting the ISO Customer Licence
Agreement, which nobody can accept on your behalf; if you already have the files,
`SCHEMA_CACHE=<dir> bash …/download-schemas.sh` lays that copy out instead of fetching — `<dir>`
holding the `iso-2/`, `iso-20/` and `amd1/` directories the script would otherwise have created.

The offline run (`dotnet test`) needs no C toolchain, no Java and no network: the record-mode
cross-checks re-encode Josev's captured EXIficient frames through our codec
(`WWCP_ISO15118_EXI_Tests`), the session corpus under `Vectors/` guards our own wire output against
regression, the transport's own decisions are unit-tested in `WWCP_ISO15118_Session_Tests`, and the
loopback E2Es run both peers in-process. 1 462 tests, all four assemblies green. The **live** cross-checks against a
running Josev or EVerest are `[Explicit]` and stay out of the offline run — they need the other stack
on the wire. What each of them has proven is the matrix above.



## Deeper reading

| | |
|---|---|
| [`docs/josev-cross-validation.md`](docs/josev-cross-validation.md) | the independent **codec** (EXIficient), the counterparty with the most history here, and the only one that serves both roles well. Every -20 energy mode any independent stack implements, over TCP and TLS, plain and Plug & Charge, in both control modes. |
| [`docs/everest-cross-validation.md`](docs/everest-cross-validation.md) | the independent **charger**, the thing a car in the field actually meets, and the counterparty that has found the most defects in *this* project; almost all of them share one of two shapes, which that page names. [No unattempted cell left](docs/everest-cross-validation.md#current-state), the walls that remain named one at a time, and the drafts it produced indexed in [`docs/reports/`](docs/reports/README.md) — which is the only place their number is kept, because carrying it here is how it went four out of date. |
| [`docs/evdriveflow-cross-validation.md`](docs/evdriveflow-cross-validation.md) | the **second** independent codec (OpenEXI), and the highest yield per exchange here: one defect of ours that every other oracle was structurally blind to, and four of theirs. The wall that held all four of its capabilities [turned out to be a closed file descriptor](docs/interop-runs/2026-08-06-edf-stdin-wall/notes.md), not a state machine. |
| [`docs/tux-evse-cross-validation.md`](docs/tux-evse-cross-validation.md) | a **replayer**, not a codec: their scenarios come from packet captures, so what it offers is a real car's route and the only DIN 70121 material this project has seen. As a responder it answers the car in its recording and no other; as an **injector at their HEAD** it drove our SECC through the full captured-Audi DC session and a VW AC route — and reached the one arm of our state machine no self-consistent test had ever executed. Over TLS it produced the first external check of our TLS profile, and [two findings drafted for them](docs/reports/tux-evse-tls.md). Their Tesla DIN capture is unreadable to us past the handshake — and the handshake alone [carried a vendor-proprietary protocol at priority 1](docs/interop-runs/2026-08-07-tesla-din-handshake/notes.md), an offer shape nothing here could have written for itself. |
| [`docs/open-work.md`](docs/open-work.md) | the inverse of the matrix above: every cell that is not `✅`, why, and who it waits on. **The to-do list.** |
| [`docs/interop-runs/`](docs/interop-runs/) | one write-up per live run: configuration, frame logs, divergences. **History, not a to-do list** — each note's `Next` section is a snapshot from that day, and later runs close items without editing it |
| [`docs/reports/`](docs/reports/README.md) | findings written up for the counterparty they belong to — **forty-seven filings across six projects**, each a draft for a person to send, with the reproduction that makes it confirmable |
| [`tools/interop-*/`](tools/) | how to bring each counterparty up and drive it — [Josev](tools/interop-josev/README.md) · [EVerest](tools/interop-everest/README.md) · [eVDriveFlow](tools/interop-evdriveflow/README.md) · [tux-evse](tools/interop-tux-evse/README.md) |
| [`docs/assumed-values-sweep.md`](docs/assumed-values-sweep.md) | where our own assumptions replaced values the protocol supplies |


---

The stack all of this tests — the EXI codec, the state machines, the TLS/PKI, the CLI — is documented in
**[WWCP_ISO15118](libs/EVSimulatorApp/libs/WWCP_ISO15118)**, and the apps built on it in
**[EVSimulatorApp](libs/EVSimulatorApp)** one level above it.

This repository is only the judge.
