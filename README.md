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
| Their EXI | **EXIficient** | **cbV2G**¹ (OpenV2G in the 2023 image) | **OpenEXI**/Nagasena | replays a **captured Audi** |
| Versions met | current | 2023.10.0 · **2025.10.0** · **2026.02.1** (source build) | `60249c3` | v0.1 image |
| Directions | forward + reverse | forward | forward + reverse | forward (responder) |

Every row below also runs **in-repo** as a loopback E2E (both peers ours) — the matrix counts only
what a *foreign* stack has confirmed. Sessions recorded from these runs replay offline as part of the
suite, so the matrix does not rot silently when the code moves. Evidence per cell lives under
[`docs/interop-runs/`](docs/interop-runs/); the run-notes README explains how to read one.

✅ complete live session &nbsp;·&nbsp; ◐ partial — ran to the stated point &nbsp;·&nbsp; ⛔ blocked by a
counterparty defect or limitation &nbsp;·&nbsp; ▢ not attempted yet &nbsp;·&nbsp; — not applicable /
not implemented on their side

**ISO 15118-2**

| Scenario | Josev | EVerest | eVDriveFlow | tux-evse |
|---|---|---|---|---|
| AC, EIM | ✅ both directions | ✅ ×2 sessions | — | — |
| DC, EIM | ✅ both directions | ✅ ×2 sessions¹ | — | ◐ stops at `SessionSetup`² |
| Plug & Charge (over TLS) | ✅ both directions, signed msgs verified both ways | ◐ chain accepted + our signature verified, on 2025.10.0 **and** 2026.02.1; their SIL has no contract-validating backend³ | — | — |
| Pause / Resume | ✅ forward (`OK_OldSessionJoined`) | — | — | — |
| Signed tariffs (SalesTariff) | ✅ both roles, incl. their MO-signed tariff verified by us | — | — | — |
| TLS 1.2 (unilateral) | ✅ | ✅ (the PnC session above) | — | — |

**ISO 15118-20**

| Scenario | Josev | EVerest | eVDriveFlow | tux-evse |
|---|---|---|---|---|
| DC, Scheduled, EIM | ✅ TCP + TLS | ✅ ×2 sessions | ◐ 12 exchanges, their SECC drops `DC_ChargeLoop`⁴ | — |
| DC, Dynamic | ✅ | ✅ | ⛔ their EV quits at Authorization | — |
| AC | ✅ TCP + TLS | ◐ to `ScheduleExchange`, then their SIL's own-EV contactor coupling⁵ | — | — |
| BPT, AC + DC (incl. Dynamic) | ✅ | ▢ (their 2026.02.1 SIL now advertises BPT) | — | — |
| Plug & Charge | ✅ | ✅ **reverse**: their EV's signed `AuthorizationReq` verified by our SECC¹⁰ (forward: commented out on their side) | ▢ | — |
| CertificateInstallation | ◐ our signed res verified; their impl ends at its own `NotImplementedError` | — | — | — |
| Mutual TLS 1.3 | ✅ (their P-256 PKI) | ✅ full session ×2, our client on Windows⁶ | — (plain TCP only) | — |
| SDP discovery | ✅ both directions | ✅ multicast (unicast: fixed in 2026.02.1) | ✅ their EV found our SECC | — |
| Multi-protocol SAP offer | — | ✅ IsoMux, all four offer shapes⁷ | — | — |
| WPT · ACDP | *codec-validated only — no independent stack implements session state machines for them* | | | |
| MCS | — | ✅ **both directions** — ×3 forward (Scheduled ×2, Dynamic), and their EV picked service **8** out of our catalogue in reverse⁸ | — | — |
| MCS_BPT | — | ✅ ×2 complete sessions under service **9**, our discharge limits read back by their station⁹ | — | — |

¹ EVerest's current `EvseV2G` sits on cbV2G — the encoder our vector corpus is generated from — so
byte-level agreement there is not independent. The 2023.10.0 demo image ran **OpenV2G**, which *was* an
independent-codec witness; Josev (EXIficient) and eVDriveFlow (OpenEXI) are the standing independent
lineages.
² Their responder replays a captured car and refuses any request whose identifiers differ from the
recording — a property of their tool, not an interop verdict.
³ Their station-side rule "no Contract without TLS" was also the first external check of that spec
requirement against us, and still holds on 2026.02.1. Two structural limits sit behind that cell: a
contract/eMAID has no validator in their SIL (their PnC-capable configuration wants an OCPP 2.0.1
CSMS), and their `EvseManager` drops `Contract` from the offer for an already-authorized session — so
plugging the simulated car in, which is what makes a *complete charge* possible, is also what makes
PnC unreachable. -20 PnC is a single commented-out line in their module, unchanged since 2025.10.
⁴ Their defect (optional element dereferenced; one more in the charge loop), three findings filed in
the run notes — and 12 of our -20 messages decoded clean by a second independent codec.
⁵ Their -20 AC SIL waits on its own EV module's power-ready callback, which a foreign EV cannot
produce; not reachable from the wire.
⁶ Complete charge over mutual TLS 1.3 on 2025.10.0 (macOS), and **×2 against 2026.02.1 driven from
Windows** — 59 and 68 exchanges to `SessionStop`, their side logging `Handshake complete` /
`Verify certificate result is okay`
([`2026-08-06-everest-iso20-tls13-windows`](docs/interop-runs/2026-08-06-everest-iso20-tls13-windows/notes.md)).
The Schannel caveat that stood here is retired at both ends: the app lets a session name its TLS backend
(`V2G_TLS_BACKEND=BouncyCastle`), and that is the only variable the live run added. One bound survives,
and it is about *their* material rather than ours — their `create_certs.sh -v iso-20` emits **P-256**
(`EC_CURVE=prime256v1`, with their own `TODO` beside it), so the secp521r1 half of our -20 profile still
has no counterparty that generates it.
⁷ And the finding that goes with it: `IsoMux` routes on "mentions -20 anywhere", never reading SAP
`Priority` — confirmed on the wire against 2025.10.0 **and** 2026.02.1.
⁸ The reverse leg is the one that tests *our* catalogue rather than theirs: offered `{ 8, 9 }` by
`Secc20Mcs`, their `PyEvJosev` selected **8** and ran to completion
([`2026-08-06-everest-mcs-reverse`](docs/interop-runs/2026-08-06-everest-mcs-reverse/notes.md)). It also
took an app fix to be *readable* at all — `Secc20Base.SelectedEnergyServiceId` was `protected` while its
EVCC counterpart was public, and in reverse the station is the only side that can report the choice.
The three forward sessions validated the **catalogue** only; the envelope followed a day later under MCS_BPT
(see ⁹), where their `EvseManager` decoded `dc_ev_maximum_power_limit: 3750000.0` at 3000 A and 1250 V.
What no run against this counterpart can show is megawatt *power*: their MCS SIL is electrically an
ordinary charger and clamps to 22 kW whatever is declared.
⁹ Green on the second attempt, and the first one is why the cell is trustworthy. On 2026-08-05 the
station refused us at `DC_ChargeParameterDiscoveryRes` with `FAILED_WrongChargeParameter` — correctly: no
`Evcc20*` built the `BPT_*` request types, because our bidirectional work had been done station-side where
the EV's message decides the direction. That refusal was the first external confirmation that the
service/parameter coupling binds the EV too, and it drove the fix; on 2026-08-06 the same scenario ran
twice to `SessionStop` and their station logged `Max discharge current 3000.000000A`
([`2026-08-06-everest-mcs-bpt-complete`](docs/interop-runs/2026-08-06-everest-mcs-bpt-complete/notes.md)).
Getting there turned up two further defects of ours, both fixed: `SelectEnergyTransferService` followed
the *station's* catalogue order, so `PreferredEnergyServiceIds` — documented "best first" — was a filter
and never a ranking (the same defect shape as ⁷, pointed at ourselves); and the harness's own MCS_BPT
probe inherited the DC envelope, so its first session declared 50 kW under service 9. Only a station that
logs what it received could show the second — a loopback peer clamps nothing and reports nothing.
¹⁰ **-20** Plug & Charge against EVerest had never run in either direction until 2026-08-06: their
`Evse15118D20` still has it commented out on the station side, so the forward leg is theirs to fix, and
the reverse leg had simply not been attempted. It ran as a by-product of the MCS reverse session — their
`PyEvJosev` authorized with their own `everest-aux` MO contract and our SECC verified the signature,
digest and challenge. The -2 direction, where *they* verify *our* signature, is the separate result in ³.

**EVerest, current state:** the full forward matrix — -2 DC/AC, -20 DC Scheduled **and** Dynamic, `IsoMux`
in all four offer shapes, -20 DC over mutual TLS 1.3 — is green against **everest-core 2025.10.0** (demo
image, 02/03.08) **and re-validated against 2026.02.1 built from source**
([`2026-08-05-everest-2026021-matrix`](docs/interop-runs/2026-08-05-everest-2026021-matrix/notes.md)).
Standing deltas on 2026.02.1: their unicast-SDP loop shutdown is fixed, while the refused-TLS-handshake
one persists and turns out to be reachable from their *stock* SIL config by one `openssl s_client`
line — after it, the charger answers nothing while its process stays healthy (report ready to file,
[`docs/reports/everest-loop-shutdown.md`](docs/reports/everest-loop-shutdown.md)),
`IsoMux` still ignores SAP `Priority`, their stock SIL -20 config went Dynamic-only, and
`config-sil-mcs.yaml` now exists — **which has since been run**: three complete MCS sessions, our service
id 8 read back by their stack as MCS, the first external witness this project has ever had for MCS
([`2026-08-05-everest-mcs`](docs/interop-runs/2026-08-05-everest-mcs/notes.md)).
PnC was repeated too: our signed -2 `AuthorizationReq` verifies against their station on 2026.02.1 as
it did on 2025.10, and the wall behind it is theirs (nothing in the SIL validates a contract).
Known bounds: -20 AC still stops at their SIL's own-EV contactor coupling. The Windows/TLS bound is
gone — two -20 DC sessions over mutual TLS 1.3 now run from Windows against 2026.02.1, client chain and
all (see ⁶).

**Josev has a page of its own**, because it is the counterparty with the most history here and the only
one that serves both roles well: [`docs/josev-cross-validation.md`](docs/josev-cross-validation.md) —
every scenario, what each one caught, and what stays out of reach.

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
| [`docs/josev-cross-validation.md`](docs/josev-cross-validation.md) | the Josev column in full — every scenario, every bug it caught |
| [`docs/interop-runs/`](docs/interop-runs/) | one write-up per live run: configuration, frame logs, divergences |
| [`docs/reports/`](docs/reports/) | findings written up for the counterparty they belong to |
| [`tools/interop-josev/`](tools/interop-josev/README.md), [`tools/interop-everest/`](tools/interop-everest/README.md) | how to bring each counterparty up and drive it |
| [`docs/assumed-values-sweep.md`](docs/assumed-values-sweep.md) | where our own assumptions replaced values the protocol supplies |

---

The stack all of this tests — the EXI codec, the state machines, the TLS/PKI, the CLI — is documented in
**[EVSimulatorApp](libs/EVSimulatorApp)**. This repository is only the judge.
