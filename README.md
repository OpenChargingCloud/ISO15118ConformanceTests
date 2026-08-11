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
| **Their station**, for our `EV→` runs | their SECC — **EXIficient** | `EvseV2G` · `Evse15118D20` · `IsoMux` — **cbV2G**¹ (OpenV2G in the 2023 image) | their SECC — **OpenEXI**/Nagasena | a responder replaying a **captured Audi** |
| **Their EV**, for our `←SECC` runs | their EVCC — **EXIficient** | `PyEvJosev` — **EVerest's fork of Josev**, so EXIficient again¹⁶ | their EV — **OpenEXI** | their **injector**, replaying captured cars (an Audi, a VW)¹⁷ |
| Versions met | current | 2023.10.0 · **2025.10.0** · **2026.02.1** (source build) | `60249c3` | v0.1 image · **`main` `fc51088`** (source build) |
| Directions | `EV→ ←SECC` throughout | `EV→` throughout · `←SECC` only where it adds something¹⁶ | `EV→ ←SECC` | `EV→ ←SECC` |

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
| AC, EIM | ✅ `Iso2LoopbackTests` | ✅ `EV→ ←SECC` | ✅ `EV→` ×2 sessions | — | ✅ `←SECC` a real VW's route¹⁸ · ✅ two Porsche routes, after a 40 W finding²³ |
| DC, EIM | ✅ `Iso2LoopbackTests` | ✅ `EV→ ←SECC` | ✅ `EV→` ×2 sessions¹ | — | ✅ `←SECC` the full captured-Audi session¹⁷ · ◐ `EV→` stops at `SessionSetup`² |
| Plug & Charge (over TLS) | ✅ `Iso2LoopbackTests` (signed auth + metering receipts) | ✅ `EV→ ←SECC`, signed msgs verified both ways | ◐ `EV→` chain accepted + our signature verified, on 2025.10.0 **and** 2026.02.1; their SIL has no contract-validating backend³ | — | — |
| Pause / Resume | ✅ `Iso2LoopbackTests` | ✅ `EV→` (`OK_OldSessionJoined`) | — | — | — |
| Signed tariffs (SalesTariff) | ✅ `Secc2TariffTests` + E2E | ✅ `EV→` their MO-signed tariff verified by us · `←SECC` their EV consumed ours | — | — | — |
| Renegotiation | ✅ `Iso2LoopbackTests` (EV- and SECC-triggered) | ✅ `EV→ ←SECC` [V2G2-841] | ▢ | — | — |
| TLS 1.2 (unilateral) | ✅ `TlsLoopbackTests` | ✅ `EV→` | ✅ `EV→` (the PnC session above) | — | ⛔ `←SECC` pinned to the profile's suites: **their configs offer neither**¹⁹ · ◐ unpinned, 4 exchanges (+ mutual TLS, their `CN=eMaid`) |

**ISO 15118-20**

| Scenario | Ours (C# loopback) | Josev | EVerest | eVDriveFlow | tux-evse |
|---|---|---|---|---|---|
| DC, Scheduled, EIM | ✅ `Iso20LoopbackTests` | ✅ `EV→ ←SECC` TCP + TLS | ✅ `EV→` ×2 sessions | ◐ `EV→` 12 exchanges, their SECC drops `DC_ChargeLoop`⁴ | — |
| DC, Dynamic | ✅ `Evcc20DynamicModeTests` + `Secc20DynamicModeTests` | ✅ `←SECC` only — their EV adopts the mode our station offers¹³ | ✅ `EV→` | ◐ `←SECC` 15 exchanges into the charge loop²⁰ | — |
| AC | ✅ `Iso20LoopbackTests` | ✅ `←SECC` TCP + TLS | ◐ `EV→` to `ScheduleExchange`, then their SIL's own-EV contactor coupling⁵ | — | — |
| BPT, AC + DC (incl. Dynamic) | ✅ `Evcc20BidirectionalTests`, `Secc20AcBptTests`, `Evcc20BptRankingTests` | ✅ `←SECC` their EV selects service 6 / 5 | ✅ `EV→` **DC_BPT ×2** (Scheduled + Dynamic), our discharge limit read back; ◐ AC_BPT negotiated, then their contactor wall¹¹ | ✅ `←SECC` **DC_BPT**, both envelopes crossed²² | — |
| Plug & Charge | ✅ `Iso20LoopbackTests` (signed auth verified at SECC) | ✅ `EV→ ←SECC` | ✅ `←SECC` their EV's signed `AuthorizationReq` verified by our SECC¹⁰ (`EV→`: commented out on their side) | — they implement none²⁸ | — |
| CertificateInstallation | ✅ `Iso20LoopbackTests` — full roundtrip, the EV unwraps a working contract key | ◐ `←SECC` our signed res verified; their impl ends at its own `NotImplementedError` | ◐ `←SECC` their EV's real OEM chain, built against their OEM root²⁶ — then the same wall | — | — |
| Pause / Resume | ✅ `Iso20LoopbackTests` (`OK_OldSessionJoined`) | ⛔ `EV→` their -20 session context stays empty, so it degrades to a graceful new session¹⁴ | ✅ `EV→` paused and resumed end to end over mutual TLS (`OK_OldSessionJoined`), the resumed half opening at `DcChargeParameterDiscovery`²⁵ | — | — |
| Signed tariffs (AbsolutePriceSchedule) | ✅ `Iso20LoopbackTests` — signature verified at the EV | ◐ `←SECC` their AC EVCC consumed our signed schedule; nothing external **verifies** it¹⁵ | ▢ | — | — |
| Renegotiation | ✅ `Secc20DynamicModeTests` (re-entry at ServiceDiscovery) | ◐ `←SECC` their EV sends a real `SessionStopReq(ServiceRenegotiation)` [V2G20-1477], then drops the link anyway¹⁴ | ◐ `←SECC` the same, in **DC** and against their fork²⁷ | — | — |
| Mutual TLS 1.3 | ✅ `MutualTlsLoopbackTests`, `BcMutualTlsLoopbackTests` | ✅ `EV→ ←SECC` (their P-256 PKI) | ✅ `EV→` full session ×2, our client on Windows⁶ | ✅ `←SECC` **secp521r1 both ways**²¹ | — |
| SDP discovery | ✅ `FullStackLoopbackTests` (SLAC→SDP→TLS→-20 DC) | ✅ `EV→ ←SECC` | ✅ `EV→` multicast (unicast: fixed in 2026.02.1) · `←SECC` **their EV discovers the recording fixture**⁸ | ✅ `←SECC` their EV found our SECC | — |
| Multi-protocol SAP offer | ✅ `MultiProtocolSapTests` | — | ✅ `EV→` IsoMux, all four offer shapes⁷ — **and over TLS**, where it routes a -20 session onto TLS 1.2¹² | — | — |
| WPT · ACDP | ▢ codec only — but the codec is now independently judged²⁴ | *no independent stack implements session state machines for them; the bytes are read by EXIficient* | | | |
| MCS | ✅ `Secc20McsTests` | — | ✅ `EV→` ×3 (Scheduled ×2, Dynamic) · `←SECC` their EV picked service **8** out of our catalogue⁸ | — | — |
| MCS_BPT | ✅ `Secc20McsTests` (ranking + envelope) | — | ✅ `EV→` ×2 complete sessions under service **9**, our discharge limits read back by their station⁹ | — | — |

Each note states the one fact its cell cannot hold. The reasoning, the run that produced it and the
defects it turned up live on the counterparty's own page, linked under **Deeper reading** below.

¹ Only the **2023.10.0** demo image was an independent-codec witness (OpenV2G). Current `EvseV2G` and
`Evse15118D20` sit on **cbV2G**, our own corpus generator — so byte agreement there is agreement with
ourselves, and the value of this column is behavioural. The independent byte judgement for `-2` comes
from elsewhere: since 2026-08-07 the whole `-2` corpus round-trips through **EXIficient**, offline and
on demand — see [`tools/interop-v2gdecoder/`](tools/interop-v2gdecoder/README.md).

² Their responder replays a captured car and refuses any request whose identifiers differ from the
recording — a property of their tool, not an interop verdict.

³ Their rule *"no `Contract` without TLS"* was the first external check of that requirement against us.
A complete charge and a PnC offer never came in the same session — but that is the **intended EIM path**,
not a wall: their `EvseManager` offers `ExternalPayment` alone once a session is authorized, and their
SIL's dummy token provider swipes at plug-in.

⁴ Their defect (optional element dereferenced; one more in the charge loop), three findings filed in the
run notes — and 12 of our -20 messages decoded clean by a second independent codec.

⁵ Their -20 AC SIL closes the contactor on a Control-Pilot `PowerOn` event, and in SIL that line is
driven by their own EV module following its own session — so driving it from outside is not enough.
Ours to get past, not theirs to fix. Reading their source to explain it did turn up something that is
theirs, on the same code path and not the cause of this:
[`everest-iso20-ac-contactor-latch.md`](docs/reports/everest-iso20-ac-contactor-latch.md).

⁶ 59 and 68 exchanges to `SessionStop` from Windows, once the app let a session name its TLS backend.
The session is real; **the curve is not the one -20 asks for, and that is theirs**:
`create_certs.sh -v iso-20` emits P-256 — with their own `TODO` beside it — where ISO 15118-20
prescribes secp521r1 or Ed448 for the PKI *and* the key exchange. Josev's -20 PKI is P-256 too. So for a
long time this project's -20 TLS met only -2-grade material from counterparties; eVDriveFlow is the
first that ships what the standard says (footnote ²¹).

⁷ `IsoMux` routes on *"mentions -20 anywhere"* and never reads SAP `Priority` — confirmed on the wire
against 2025.10.0, 2026.02.1, and a third time over TLS, with the same request and answer bytes every
time. `[V2G2-169]` and `[V2G20-169]` make selecting by the EV's ranking a *shall*, so it is a defect and
not only a surprise: the **twentieth filing**,
[`everest-isomux.md`](docs/reports/everest-isomux.md). Both modules behind
their mux already implement the rule.

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

¹² `IsoMux` terminates TLS at the **-2 profile** — 1.2 with the suite ISO 15118-2 prescribes, pinned in
code it shares with `EvseV2G` — and only then routes on the SAP offer. So a dual-stack EV gets a complete
**-20 session over TLS 1.2**, and a -20 EV that pins its own profile gets alert 70. It also corrected a
mirror of that layering on our side. `[V2G20-2356]` forbids the station to select -20 there, and between
the two halves their -20 backend is unreachable by any conformant EV: the **nineteenth filing**,
[`everest-isomux.md`](docs/reports/everest-isomux.md). The offer that
showed it was ours and broke the mirror requirement `[V2G20-1237]` — [our own item](docs/open-work.md).

¹³ ✅ in both columns, but **disjoint halves**: Dynamic ran `←SECC` against Josev and `EV→` against
EVerest, because our station could answer a Dynamic car long before our car could be one. Neither column
covers the mode alone.

¹⁴ Our side is complete for both; **theirs is the bound**. Josev's -20 states never fill the session
context, so a -20 resume degrades to a new session; and its EVCC drops the link after a real
`SessionStopReq(ServiceRenegotiation)` [V2G20-1477] that our SECC answers without ending the session — the
renegotiation branch of their `SessionStop` state is the one transition in that file that never builds the
message the next state needs, and their own framework refuses it. Filed:
[`josev-iso20-renegotiation.md`](docs/reports/josev-iso20-renegotiation.md).

¹⁵ The one cell where `◐` is a missing **verifier**, not a missing session: their EV consumed our signed
`AbsolutePriceSchedule` and ran on it, but Josev's EVCC-side tariff check is a literal `# TODO`.

¹⁶ **Their EV is Josev** — `PyEvJosev` wraps EVerest's fork of the same codebase the Josev column tests.
So the codec flips with the direction (cbV2G forward, EXIficient reverse), and a `←SECC` run here is
largely a re-run of that column — which is why the reverse direction was spent only on **MCS**, and why
-2 reverse against EVerest has deliberately never been run.

¹⁷ Their injector replays the capture at us with `expect` blocks reduced to protocol fields
(`scenario-relax.py` — message type and response code stay checked; the stock file aborts at the first
field our station legitimately answers differently, its recorded charger's EVSE ID). 25 exchanges,
`SessionSetup` to `SessionStop`, every code OK, at their `main` built from source — which also carries
our freshly-issued session id through every request, something the v0.1 image's player could not.

¹⁸ The AC capture exists only at their HEAD, converted by their own `pcap-iso15118`. Under their
`basic` compaction the route runs to `SessionStop` — including the VW stopping straight from the
charging phase, where **the recorded charger answered `FAILED_SequenceError` and ours answers `OK`**,
a divergence kept, not corrected. Uncompacted, the VW's double `Authorization` poll reached the arm of
our sequence guard that closed the connection instead of answering `FAILED_SequenceError` on the wire —
the first finding against us from this counterparty, and one only a replayer could produce: every
other peer polls only while our station says `Ongoing`. **Fixed and re-run the same day**: the refusal
now goes out in the request's own response type, and their injector decodes it.

²³ Both Taycan captures ask for **11,040 W** in their `ChargingProfile` — 3 × 230 V × 16 A, the
ubiquitous European AC charge point — and our station offered a rounded **11,000 W**, so [V2G2-761]
refused `PowerDelivery` by 0.4 %. Correct by the letter on both sides, and a bad trade for a station
built to test interoperability: it manufactures a failure no real charger would produce, at the last
message before charging. **Fixed and re-run the same day**: the offer is now the physical number, in
the plain schedule and in tuple 1 of the tariff offer, the recorded corpus moved with it (the offer,
the profile, and the AC energies 549 → 552 Wh), and both captures then ran to `SessionStop` — ten
exchanges, every response `OK`, their injector's own TAP reporting 12/12, and both flow reports ending
"the order matches the declared flow exactly". It is also the first AC session here to reach the charge
loop, which is how `charging_status_req` finally entered the verb table — from **their** converter and
**their** TAP output, not from a guess. The unfolded runs of the same captures are the second and third
real car to poll `Authorization` twice, and both confirm the 2026-08-06 fix: the refusal goes out as
`FAILED_SequenceError` instead of a closed socket. They were re-run too and are **unchanged**, which is
the answer rather than a gap — the session dies four messages before `PowerDelivery`, so a schedule fix
cannot reach it, and "changed nothing" is now a measurement instead of a claim.

²⁸ **Structural, and now established rather than assumed.** This cell held a `▢` and a condition — *first find out whether they do contract certificates at all.* They do not: no `CertificateInstallation` handler in either role's state machine, and the whole Plug & Charge vocabulary (`ContractCertificateChain`, `PnC_AReqAuthorizationMode`, `SignedInstallationData`, `OEMProvisioningCert`) occurs only in the xsdata-generated bindings, ISO's schema and the Sphinx output of both — plus two Table 214 timeout keys with no handler to time. Their README's *Supported features* does not list it, and `PnC` appears nowhere in their documentation. Both halves ship `authorization_services = [EIM]`. The already-recorded bytes agree: their `AuthorizationSetupRes` is 20 payload bytes against our PnC-offering 38, with no room for a `GenChallenge` and none in it. The audit also turned up a latent SECC defect — the authorization *mode* is hardcoded to EIM whatever the configurable service list says, which `[V2G20-1219]` and `[V2G20-2568]` each forbid — recorded as a note on [the existing filing](docs/reports/evdriveflow-authorization-setup.md) rather than raised, since it is unreachable in their shipped configuration and they claim no PnC. [`…-edf-pnc-source-audit`](docs/interop-runs/2026-08-11-edf-pnc-source-audit/notes.md).

²⁷ Their `PyEvJosev` EV is EVerest's fork of Josev, so this is the **same defect the Josev column carries**, now seen in **DC** and against the fork at `26f7988` rather than in AC against upstream. Our station signalled `ServiceRenegotiation` once mid-charge; their EV stopped the charge, ran welding detection and sent `SessionStopReq(ServiceRenegotiation)` — a frame *upstream cannot produce*, since its `DCWeldingDetection` hardcodes `Terminate` — and then closed the connection after our `SessionStopRes(OK)` left the session open. So the fork has fixed half of it. See [`…-iso20-renegotiation-reverse`](docs/interop-runs/2026-08-10-everest-iso20-renegotiation-reverse/notes.md) and [`josev-iso20-renegotiation.md`](docs/reports/josev-iso20-renegotiation.md).

²² Their EV picks service **6** out of our `{2, 6}` — the choice is theirs — and
`DC_ChargeParameterDiscovery` carried a real bidirectional envelope each way, each side's numbers read
by the other's codec: their car **48 kW / 137 A** of discharge against our station's **50 kW / 200 A**,
then a charge loop in `BPT_Dynamic_DC_CLReqControlMode`. No energy reverses — the session ends at their
charge-loop defect first — so this is the negotiation, in full, and not a discharge. One deviation,
recorded: their `ev_dummy_controller` starts at `present_soc = 0` (the GUI's field sets it), and an
empty battery correctly declares zero discharge, so the run patches that one line to 60 in their copy.
Both numbers are on file.

²⁵ **Found broken here, fixed, and re-run the same day.** Their resume is gated on mutual TLS —
`d20/state/session_setup.cpp` matches `SHA-512(session_id ‖ vehicle_cert_hash)` from the verified TLS
peer certificate, and `ConnectionPlain` returns none — so no earlier EIM run could have reached it. It
resumed on the first attempt with their minted vehicle credential; what failed was ours, replaying the
opening sequence into a session already past it
([first run](docs/interop-runs/2026-08-08-everest-pause-resume-tls/notes.md)). After the fix, **their own
log shows the difference**: `SessionSetupReq → AuthorizationSetupReq → … → ServiceSelectionReq →
DcChargeParameterDiscoveryReq` in the first half, `SessionSetupReq → DcChargeParameterDiscoveryReq` in
the resumed one — the five skipped messages, counted by the counterparty
([re-run](docs/interop-runs/2026-08-08-everest-pause-resume-tls-rerun/notes.md)). The station's binding
is still only checked by them; ours computes the same construction but the two are never compared,
because in this direction only their SECC's value is consulted.

²⁶ The last chain our validator knew only from material we minted ourselves. Their `PyEvJosev` with
`is_cert_install_needed: true` sends a signed `CertificateInstallationReq` carrying
`OEMRootCA → OEMSubCA1 → OEMSubCA2 → OEMProvCert` — a **third** self-signed root in their PKI, after the
V2G one their TLS uses and the MO one their contract chain is anchored at. Their OEM root **alone**
suffices, because their car ships its Sub-CAs in the message; their two Sub-CAs **without** the root do
not, `CustomRootTrust` refusing a non-self-signed anchor at message level exactly as it does at TLS; and
their **V2G** root — which their own request names in `RootCertificateIDList` — is refused while the
signature still verifies. That field is the car saying which roots it can check, not which root vouches
for it. The wall after that is Josev's, in this fork as in SwitchEV's, and the contract key we wrap
stays self-checked: their P-256 OEM leaf cannot join `-20`'s secp521r1 ECDH
([`2026-08-08-everest-oem-provisioning-chain`](docs/interop-runs/2026-08-08-everest-oem-provisioning-chain/notes.md)).

²⁴ Still no session state machine anywhere but ours — but "codec only" no longer means "judged only by
its own generator". Since 2026-08-07 every WPT and ACDP frame in the corpus is decoded and re-encoded by
**EXIficient**, which shares no line with cbexigen, and since 2026-08-08 they agree to the octet. Getting
there cost two deliberate changes: these were the only message sets where this codec had been
reproducing cbexigen's grammar rather than ISO's, and where the two disagree we now follow the schema —
[`2026-08-08-schema-conformant-acdp-wpt`](docs/interop-runs/2026-08-08-schema-conformant-acdp-wpt/notes.md).
The failure that decision turned up is the reason it was not close: our `ACDP_ConnectRes` decoded
**cleanly, as `ACDP_DisconnectReq`** — the wrong message, with nothing to report it. Both deviations are
drafted for libcbv2g in [`docs/reports/`](docs/reports/libcbv2g-grammar-deviations.md).

²¹ The capability this counterparty was chosen for, reached once the stdin wall fell:
`TLS_AES_256_GCM_SHA384` under TLS 1.3, both peers authenticated — their EV verified our station
against its own V2G root and presented `CN=VEHICLECert`, our station required and read it back — and
**secp521r1 on both sides**. That last part is ordinary in the standard and rare in the field: -20
prescribes secp521r1 (or Ed448) for the PKI and the key exchange, but **both other -20 counterparties
here ship P-256 test material** (footnote ⁶), so this is the first peer whose -20 PKI is the one -20
describes rather than -2's. There is a platform reason for the drift worth knowing: Schannel cannot do
P-521 for TLS at all, which is why the app carries a second, managed TLS backend — and why our own
Windows mutual-TLS tests use P-256. **That managed backend then carried the same session against them**
(`V2G_TLS_BACKEND=BouncyCastle`), so the -20-faithful profile — TLS 1.3, the suite pair, P-521 both
ways — has an external witness instead of only a loopback one. 15 exchanges either way, the same route
as plain TCP. Their shipped
certificates had to be regenerated with **their own** `generateCertificates.sh` first: the SECC leaf
expired in October 2022 (60 days, as the standard requires) and `cpoSubCA1` the day before the run.

²⁰ It used to read *"their EV quits at Authorization"*, recorded as an open question after two runs could
not move it. Reading their state machine settled it on 2026-08-06: their EV arms a "press Enter to stop"
listener on `sys.stdin` unconditionally, EOF returns immediately, and `process_reaction` then replaces the
message the state machine built with `SessionStopReq` in the first state that permits it — which is the
authorization one. The rig had started it with `docker exec -d`. **With stdin held open and nothing else
changed, 4 exchanges became 15**, through ScheduleExchange, CableCheck, PreCharge ×3, PowerDelivery and
into DC_ChargeLoop. It stops there on a defect of theirs: `hasattr` used as a presence test on an
`Optional[int]` copies our legally omitted `TargetSOC` over their own default, and `None * int` ends it.
Their EV also selects the **BPT** service on the way, so that cell is reachable now too.

¹⁹ Both their shipped configs pin one GnuTLS priority string, and its ECDSA half holds AES-GCM, AES-CCM,
ChaCha20 and two SHA-1 CBC suites — **not** `TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256` or its ECDH twin,
which is what ISO 15118-2 requires and what our station pins. Handshake: `no shared cipher`. Unpinned
(`V2G_INTEROP_TLS_SUITES=platform`, a deviation the run states rather than hides) the session runs to
`PaymentServiceSelection` and stops **on their side**: their EVCC signs the `AuthorizationReq` whenever
a `pki` block is configured rather than when Contract was selected, so an EIM scenario dies at
`no_challenge` — reproduced against **their own responder**, which means no scenario they ship runs over
TLS today. Their car does present a client certificate when asked.


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
loopback E2Es run both peers in-process. 1 400 tests, all four assemblies green. The **live** cross-checks against a
running Josev or EVerest are `[Explicit]` and stay out of the offline run — they need the other stack
on the wire. What each of them has proven is the matrix above.



## Deeper reading

| | |
|---|---|
| [`docs/josev-cross-validation.md`](docs/josev-cross-validation.md) | the independent **codec** (EXIficient), the counterparty with the most history here, and the only one that serves both roles well. Every -20 energy mode any independent stack implements, over TCP and TLS, plain and Plug & Charge, in both control modes. |
| [`docs/everest-cross-validation.md`](docs/everest-cross-validation.md) | the independent **charger**, the thing a car in the field actually meets, and the counterparty that has found the most defects in *this* project; almost all of them share one of two shapes, which that page names. [No unattempted cell left](docs/everest-cross-validation.md#current-state), two reports drafted and unsent, six structural walls named. |
| [`docs/evdriveflow-cross-validation.md`](docs/evdriveflow-cross-validation.md) | the **second** independent codec (OpenEXI), and the highest yield per exchange here: one defect of ours that every other oracle was structurally blind to, and four of theirs. The wall that held all four of its capabilities [turned out to be a closed file descriptor](docs/interop-runs/2026-08-06-edf-stdin-wall/notes.md), not a state machine. |
| [`docs/tux-evse-cross-validation.md`](docs/tux-evse-cross-validation.md) | a **replayer**, not a codec: their scenarios come from packet captures, so what it offers is a real car's route and the only DIN 70121 material this project has seen. As a responder it answers the car in its recording and no other; as an **injector at their HEAD** it drove our SECC through the full captured-Audi DC session and a VW AC route — and reached the one arm of our state machine no self-consistent test had ever executed. Over TLS it produced the first external check of our TLS profile, and [two findings drafted for them](docs/reports/tux-evse-tls.md). Their Tesla DIN capture is unreadable to us past the handshake — and the handshake alone [carried a vendor-proprietary protocol at priority 1](docs/interop-runs/2026-08-07-tesla-din-handshake/notes.md), an offer shape nothing here could have written for itself. |
| [`docs/open-work.md`](docs/open-work.md) | the inverse of the matrix above: every cell that is not `✅`, why, and who it waits on. **The to-do list.** |
| [`docs/interop-runs/`](docs/interop-runs/) | one write-up per live run: configuration, frame logs, divergences. **History, not a to-do list** — each note's `Next` section is a snapshot from that day, and later runs close items without editing it |
| [`docs/reports/`](docs/reports/README.md) | findings written up for the counterparty they belong to — **thirty-nine filings across six projects**, each a draft for a person to send, with the reproduction that makes it confirmable |
| [`tools/interop-*/`](tools/) | how to bring each counterparty up and drive it — [Josev](tools/interop-josev/README.md) · [EVerest](tools/interop-everest/README.md) · [eVDriveFlow](tools/interop-evdriveflow/README.md) · [tux-evse](tools/interop-tux-evse/README.md) |
| [`docs/assumed-values-sweep.md`](docs/assumed-values-sweep.md) | where our own assumptions replaced values the protocol supplies |


---

The stack all of this tests — the EXI codec, the state machines, the TLS/PKI, the CLI — is documented in
**[WWCP_ISO15118](libs/EVSimulatorApp/libs/WWCP_ISO15118)**, and the apps built on it in
**[EVSimulatorApp](libs/EVSimulatorApp)** one level above it.

This repository is only the judge.
