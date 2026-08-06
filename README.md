# ISO/IEC 15118 Conformance & Interoperability Test Suite

The conformance and interoperability tests for the [EVSimulatorApp](libs/EVSimulatorApp) ISO 15118 stack —
its EXI codec, its EV↔EVSE state machines, its TLS and PKI, its Plug & Charge. The app is carried
here as a submodule; this repository is the harness that proves it behaves the way the standard and
the independent stacks in the field expect.

The point of separating the two: the app can be built and shipped on its own, and the thing that
judges it — the corpus of recorded frames, the loopback E2Es, the live cross-checks against Josev and
EVerest — lives beside it rather than inside it, so "does our stack interoperate" is a question this
repository answers and the app does not have to carry. Here is the answer.

## The interop matrix — who we test against, and what happened

Four independent stacks sit on the other end of the live cross-checks (`JosevInteropTests`,
`EverestInteropTests`, `EvDriveFlowInteropTests`, `TuxEvseInteropTests`; all `[Explicit]`). They are
worth having *because* they differ — each brings a different EXI lineage and a different kind of
evidence:

| | [Josev](tools/interop-josev/README.md) | [EVerest](tools/interop-everest/README.md) | eVDriveFlow | tux-evse |
|---|---|---|---|---|
| Who | SwitchEV (Python) | LF Energy — the stack on real chargers | EDF Lab | IoT.bzh (Rust) |
| **Their station**, for our `EV→` runs | their SECC — **EXIficient** | `EvseV2G` · `Evse15118D20` · `IsoMux` — **cbV2G**¹ (OpenV2G in the 2023 image) | their SECC — **OpenEXI**/Nagasena | a responder replaying a **captured Audi** |
| **Their EV**, for our `←SECC` runs | their EVCC — **EXIficient** | `PyEvJosev` — **EVerest's fork of Josev**, so EXIficient again¹⁶ | their EV — **OpenEXI** | — (they only respond) |
| Versions met | current | 2023.10.0 · **2025.10.0** · **2026.02.1** (source build) | `60249c3` | v0.1 image |
| Directions | `EV→ ←SECC` throughout | `EV→` throughout · `←SECC` only where it adds something¹⁶ | `EV→ ←SECC` | `EV→` only |

The **Ours** column is our own C# stack against itself — a loopback E2E with both peers ours, which is
what runs in the offline suite and what every counterparty column is measured against. It says the
scenario exists and is guarded here; it says nothing about conformance, because both ends share our
assumptions. That is the whole reason for the columns to its right. Sessions recorded from the live runs
replay offline as part of the suite too, so the matrix does not rot silently when the code moves.
Evidence per cell lives under [`docs/interop-runs/`](docs/interop-runs/); the run-notes README explains
how to read one.

**Status:** ✅ complete live session &nbsp;·&nbsp; ◐ partial — ran to the stated point &nbsp;·&nbsp;
⛔ blocked by a counterparty defect or limitation &nbsp;·&nbsp; ▢ not attempted yet &nbsp;·&nbsp;
— not applicable / not implemented on their side

**Which side is ours** — the arrow points the way the session is driven, and the label names *our* role:

| | |
|---|---|
| **`EV→`** | our C# **EVCC** drives *their* station. The "forward" direction: we are the car. |
| **`←SECC`** | *their* EV drives our C# **SECC**. The "reverse" direction: we are the charging station. |
| **`EV→ ←SECC`** | both, in separate sessions. |

**ISO 15118-2**

| Scenario | Ours (C# loopback) | Josev | EVerest | eVDriveFlow | tux-evse |
|---|---|---|---|---|---|
| AC, EIM | ✅ `Iso2LoopbackTests` | ✅ `EV→ ←SECC` | ✅ `EV→` ×2 sessions | — | — |
| DC, EIM | ✅ `Iso2LoopbackTests` | ✅ `EV→ ←SECC` | ✅ `EV→` ×2 sessions¹ | — | ◐ `EV→` stops at `SessionSetup`² |
| Plug & Charge (over TLS) | ✅ `Iso2LoopbackTests` (signed auth + metering receipts) | ✅ `EV→ ←SECC`, signed msgs verified both ways | ◐ `EV→` chain accepted + our signature verified, on 2025.10.0 **and** 2026.02.1; their SIL has no contract-validating backend³ | — | — |
| Pause / Resume | ✅ `Iso2LoopbackTests` | ✅ `EV→` (`OK_OldSessionJoined`) | — | — | — |
| Signed tariffs (SalesTariff) | ✅ `Secc2TariffTests` + E2E | ✅ `EV→` their MO-signed tariff verified by us · `←SECC` their EV consumed ours | — | — | — |
| Renegotiation | ✅ `Iso2LoopbackTests` (EV- and SECC-triggered) | ✅ `EV→ ←SECC` [V2G2-841] | ▢ | — | — |
| TLS 1.2 (unilateral) | ✅ `TlsLoopbackTests` | ✅ `EV→` | ✅ `EV→` (the PnC session above) | — | — |

**ISO 15118-20**

| Scenario | Ours (C# loopback) | Josev | EVerest | eVDriveFlow | tux-evse |
|---|---|---|---|---|---|
| DC, Scheduled, EIM | ✅ `Iso20LoopbackTests` | ✅ `EV→ ←SECC` TCP + TLS | ✅ `EV→` ×2 sessions | ◐ `EV→` 12 exchanges, their SECC drops `DC_ChargeLoop`⁴ | — |
| DC, Dynamic | ✅ `Evcc20DynamicModeTests` + `Secc20DynamicModeTests` | ✅ `←SECC` only — their EV adopts the mode our station offers¹³ | ✅ `EV→` | ⛔ `←SECC` their EV quits at Authorization | — |
| AC | ✅ `Iso20LoopbackTests` | ✅ `←SECC` TCP + TLS | ◐ `EV→` to `ScheduleExchange`, then their SIL's own-EV contactor coupling⁵ | — | — |
| BPT, AC + DC (incl. Dynamic) | ✅ `Evcc20BidirectionalTests`, `Secc20AcBptTests`, `Evcc20BptRankingTests` | ✅ `←SECC` their EV selects service 6 / 5 | ✅ `EV→` **DC_BPT ×2** (Scheduled + Dynamic), our discharge limit read back; ◐ AC_BPT negotiated, then their contactor wall¹¹ | — | — |
| Plug & Charge | ✅ `Iso20LoopbackTests` (signed auth verified at SECC) | ✅ `EV→ ←SECC` | ✅ `←SECC` their EV's signed `AuthorizationReq` verified by our SECC¹⁰ (`EV→`: commented out on their side) | ▢ | — |
| CertificateInstallation | ✅ `Iso20LoopbackTests` — full roundtrip, the EV unwraps a working contract key | ◐ `←SECC` our signed res verified; their impl ends at its own `NotImplementedError` | — | — | — |
| Pause / Resume | ✅ `Iso20LoopbackTests` (`OK_OldSessionJoined`) | ⛔ `EV→` their -20 session context stays empty, so it degrades to a graceful new session¹⁴ | ▢ | — | — |
| Signed tariffs (AbsolutePriceSchedule) | ✅ `Iso20LoopbackTests` — signature verified at the EV | ◐ `←SECC` their AC EVCC consumed our signed schedule; nothing external **verifies** it¹⁵ | ▢ | — | — |
| Renegotiation | ✅ `Secc20DynamicModeTests` (re-entry at ServiceDiscovery) | ◐ `←SECC` their EV sends a real `SessionStopReq(ServiceRenegotiation)` [V2G20-1477], then drops the link anyway¹⁴ | ▢ | — | — |
| Mutual TLS 1.3 | ✅ `MutualTlsLoopbackTests`, `BcMutualTlsLoopbackTests` | ✅ `EV→ ←SECC` (their P-256 PKI) | ✅ `EV→` full session ×2, our client on Windows⁶ | — (plain TCP only) | — |
| SDP discovery | ✅ `FullStackLoopbackTests` (SLAC→SDP→TLS→-20 DC) | ✅ `EV→ ←SECC` | ✅ `EV→` multicast (unicast: fixed in 2026.02.1) · `←SECC` **their EV discovers the recording fixture**⁸ | ✅ `←SECC` their EV found our SECC | — |
| Multi-protocol SAP offer | ✅ `MultiProtocolSapTests` | — | ✅ `EV→` IsoMux, all four offer shapes⁷ — **and over TLS**, where it routes a -20 session onto TLS 1.2¹² | — | — |
| WPT · ACDP | ▢ codec only — no session state machine on either side | *codec-validated only — no independent stack implements session state machines for them* | | | |
| MCS | ✅ `Secc20McsTests` | — | ✅ `EV→` ×3 (Scheduled ×2, Dynamic) · `←SECC` their EV picked service **8** out of our catalogue⁸ | — | — |
| MCS_BPT | ✅ `Secc20McsTests` (ranking + envelope) | — | ✅ `EV→` ×2 complete sessions under service **9**, our discharge limits read back by their station⁹ | — | — |

Each note states the one fact its cell cannot hold. The reasoning, the run that produced it and the
defects it turned up live on the counterparty's own page, linked under **Deeper reading** below.

¹ Only the **2023.10.0** demo image was an independent-codec witness (OpenV2G). Current `EvseV2G` and
`Evse15118D20` sit on **cbV2G**, our own corpus generator — so byte agreement there is agreement with
ourselves, and the value of this column is behavioural.

² Their responder replays a captured car and refuses any request whose identifiers differ from the
recording — a property of their tool, not an interop verdict.

³ Their rule *"no `Contract` without TLS"* was the first external check of that requirement against us.
A complete charge and a PnC offer are **mutually exclusive** against their SIL: plugging the simulated
car in is what makes the charge possible and what authorizes the session, and their `EvseManager` drops
`Contract` for an already-authorized one.

⁴ Their defect (optional element dereferenced; one more in the charge loop), three findings filed in the
run notes — and 12 of our -20 messages decoded clean by a second independent codec.

⁵ Their -20 AC SIL waits on its own EV module's power-ready callback, which a foreign EV cannot produce.

⁶ 59 and 68 exchanges to `SessionStop` from Windows, once the app let a session name its TLS backend.
One bound survives and it is **theirs**: `create_certs.sh -v iso-20` emits P-256, so nothing here has
met secp521r1 material from a counterparty.

⁷ `IsoMux` routes on *"mentions -20 anywhere"* and never reads SAP `Priority` — confirmed on the wire
against 2025.10.0, 2026.02.1, and a third time over TLS.

⁸ The `←SECC` leg is the only one that tests **our** catalogue rather than theirs. It also needed two
fixes of ours to be *readable* at all, one in the app and one in the fixture.

⁹ Green on the second attempt: the first was refused with `FAILED_WrongChargeParameter`, correctly, and
that refusal is what proved the service/parameter coupling binds the EV too. Their `EvseManager` decoded
`dc_ev_maximum_power_limit: 3750000.0` at 3000 A / 1250 V. Megawatt **power** stays out of reach — their
MCS SIL is electrically a 22 kW charger.

¹⁰ Their `Evse15118D20` has -20 PnC commented out, so the `EV→` leg is theirs to fix; the `←SECC` leg
ran as a by-product of the MCS reverse session. The `EV→` result for **-2** is the separate cell above.

¹¹ **Neither of their configs was changed for this**, which is the finding: their SIL had been
advertising service 6 at every -20 DC run this project ever made, and our EV could not ask for it.

¹² `IsoMux` serves **TLS 1.2 only** (a 1.3 hello gets alert 70) and routes on the SAP offer regardless —
so a dual-stack EV gets a complete **-20 session over TLS 1.2**, and a conformant -20 EV reaches the -20
backend not at all. It also corrected a mirror of that layering on our side.

¹³ ✅ in both columns, but **disjoint halves**: Dynamic ran `←SECC` against Josev and `EV→` against
EVerest, because our station could answer a Dynamic car long before our car could be one. Neither column
covers the mode alone.

¹⁴ Our side is complete for both; **theirs is the bound**. Josev's -20 states never fill the session
context, so a -20 resume degrades to a new session; and its EVCC drops the link after a real
`SessionStopReq(ServiceRenegotiation)` [V2G20-1477] that our SECC answers without ending the session.

¹⁵ The one cell where `◐` is a missing **verifier**, not a missing session: their EV consumed our signed
`AbsolutePriceSchedule` and ran on it, but Josev's EVCC-side tariff check is a literal `# TODO`.

¹⁶ **Their EV is Josev** — `PyEvJosev` wraps EVerest's fork of the same codebase the Josev column tests.
So the codec flips with the direction (cbV2G forward, EXIficient reverse), and a `←SECC` run here is
largely a re-run of that column — which is why the reverse direction was spent only on **MCS**, and why
-2 reverse against EVerest has deliberately never been run.

**Every counterparty has a page of its own** — the long form of its column: each scenario, what it
caught, what it cost us, and what stays out of reach. They are not the same length, because the columns
are not, and padding the thin ones would misrepresent them.

- [`docs/josev-cross-validation.md`](docs/josev-cross-validation.md) — the independent **codec**
  (EXIficient), the counterparty with the most history here, and the only one that serves both roles
  well. Every -20 energy mode any independent stack implements, over TCP and TLS, plain and Plug &
  Charge, in both control modes.
- [`docs/everest-cross-validation.md`](docs/everest-cross-validation.md) — the independent **charger**,
  the thing a car in the field actually meets, and the counterparty that has found the most defects in
  *this* project; almost all of them share one of two shapes, which that page names.
  [No unattempted cell left](docs/everest-cross-validation.md#current-state), two reports drafted and
  unsent, six structural walls named.
- [`docs/evdriveflow-cross-validation.md`](docs/evdriveflow-cross-validation.md) — the **second**
  independent codec (OpenEXI), and the highest yield per exchange here: seventeen messages found one
  defect of ours that every other oracle was structurally blind to, and three of theirs. The four
  capabilities it was chosen for all sit behind a wall in their EV.
- [`docs/tux-evse-cross-validation.md`](docs/tux-evse-cross-validation.md) — a **replayer**, not a
  codec: their scenarios come from packet captures, so what it offers is a real car's route and the only
  DIN 70121 material this project has seen. As a responder it answers the car in its recording and no
  other; the direction its design favours is untried.

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
    └─ …/WWCP_ISO15118_EXI_Tests/         the app's codec tests — the byte-exact cbV2G and Josev
                                          oracle, carried into this solution so the offline run
                                          judges against a foreign encoder and not only ourselves
```

The codec, the simulation library and the CLI are **not** here — they are the app's, in
`libs/EVSimulatorApp/` (`simulation/`, `experiments/`, `libs/WWCP_ISO15118/`). Read
[`EVSimulatorApp`'s own README](libs/EVSimulatorApp/libs/WWCP_ISO15118/README.md) for how the codec works;
this one is about how it is held to account.

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
regression, and the loopback E2Es run both peers in-process. The **live** cross-checks against a
running Josev or EVerest are `[Explicit]` and stay out of the offline run — they need the other stack
on the wire. What each of them has proven is the matrix above.

## Deeper reading

| | |
|---|---|
| `docs/*-cross-validation.md` | one page per counterparty — its column in full, and what it cost us: [Josev](docs/josev-cross-validation.md) · [EVerest](docs/everest-cross-validation.md) · [eVDriveFlow](docs/evdriveflow-cross-validation.md) · [tux-evse](docs/tux-evse-cross-validation.md) |
| [`docs/interop-runs/`](docs/interop-runs/) | one write-up per live run: configuration, frame logs, divergences |
| [`docs/reports/`](docs/reports/) | findings written up for the counterparty they belong to |
| [`tools/interop-josev/`](tools/interop-josev/README.md), [`tools/interop-everest/`](tools/interop-everest/README.md) | how to bring each counterparty up and drive it |
| [`docs/assumed-values-sweep.md`](docs/assumed-values-sweep.md) | where our own assumptions replaced values the protocol supplies |

---

The stack all of this tests — the EXI codec, the state machines, the TLS/PKI, the CLI — is documented in
**[EVSimulatorApp](libs/EVSimulatorApp)**. This repository is only the judge.
