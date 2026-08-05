# ISO/IEC 15118 Conformance & Interoperability Test Suite

The conformance and interoperability tests for the [EVSimulatorApp](libs/EVSimulatorApp) ISO 15118 stack —
its EXI codec, its EV↔EVSE state machines, its TLS and PKI, its Plug & Charge. The app is carried
here as a submodule; this repository is the harness that proves it behaves the way the standard and
the independent stacks in the field expect.

The point of separating the two: the app can be built and shipped on its own, and the thing that
judges it — the corpus of recorded frames, the loopback E2Es, the live cross-checks against Josev and
EVerest — lives beside it rather than inside it, so "does our stack interoperate" is a question this
repository answers and the app does not have to carry.

**The answer, at a glance: [the interop matrix](#the-interop-matrix--who-we-test-against-and-what-happened)** —
which sessions ran against which independent stack (Josev, EVerest, eVDriveFlow, tux-evse), split by
-2/-20, AC/DC, EIM/PnC, TLS, and where each one stopped when it did.

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
on the wire. What each has proven is below.

---

## Interop status (Josev)

Cross-validated against **Josev** (SwitchEV/iso15118 @ `d645255`), an independent Python stack that encodes
with **EXIficient** and shares no lineage with our cbV2G oracle — the highest-value conformance signal short of
certified hardware. Tooling under [`tools/interop-josev/`](tools/interop-josev/README.md); full write-ups and
frame logs under [`docs/interop-runs/`](docs/interop-runs/). The `[Explicit]` `JosevInteropTests` gate keeps
all of this out of the offline CI run.

- **Record mode (byte-exact codec cross-check, in the offline run):** Josev's own EXIficient-encoded frames decode and
  re-encode *identically* through our codec — for -2, SAP + the full AC and DC charge loops; for -20, SAP +
  **all 30** frames of a full PnC DC session across both schema sets, including the **signed**
  `AuthorizationReq` (`JosevCapturedFrames{,Dc,20}Tests`). On the SAP frames, **our codec ≡ cbV2G ≡
  EXIficient**. This surfaced and **fixed** a real source-generator gap — the xmldsig `Transforms`
  EXI-canonicalisation grammar (`TransformType`'s `minOccurs=0 maxOccurs=unbounded` choice was a mandatory
  single choice with no END-Element alternative; `TransformsType`'s unbounded list left `ListMax=0`); the
  generator now models an optional/repeatable direct `xs:choice` as an EE-terminated optional run, all cbV2G
  vectors staying byte-exact.
- **Live over-the-wire, plain TCP — both directions:** a complete ISO 15118-20 **DC** session runs end to end
  to `SessionStop` both as our EVCC ↔ Josev SECC (forward) and Josev EVCC → our SECC (reverse). Together they
  caught and fixed **ten** real conformance bugs invisible to loopback (the V2GTP SAP payload id, the SAP -20
  namespace, EVCC SessionID adoption, dynamic service negotiation, `MaximumSupportingPoints`, a populated
  `EVPowerProfile`/`PowerToleranceAcceptance`, three SECC content bugs, and the DC **poll-loop self-looping** —
  a real EV polls CableCheck/PreCharge/PowerDelivery/WeldingDetection until each step completes). Both SECCs
  also accept `SessionStop` in any phase (graceful abort).
- **Live over-the-wire, TLS:** the same DC session runs over **TLS 1.2 (unilateral)** and **TLS 1.3 (mutual)**
  forward, and **TLS 1.3 mutual** reverse. Josev's PKI is **P-256** (not the -20-nominal secp521r1), so the
  Josev-facing TLS is the .NET `SslStream` backend; our secp521r1/Ed448 BouncyCastle backend stays proven in
  loopback. Found + fixed a client/server certificate-**chain** transmission bug (`SslStream` sent only the
  leaf, breaking a root-only peer) via `SslStreamCertificateContext`.
- **Live Plug & Charge over TLS — fully verified:** our SECC offers PnC + a `GenChallenge` and validates
  Josev's signed `AuthorizationReq` **end to end — `GenChallenge` echo, reference digest, and the ECDSA
  signature all verify** (live run [`2026-07-22-iso20-dc-pnc-tls-verified`](docs/interop-runs/2026-07-22-iso20-dc-pnc-tls-verified/):
  `challenge OK, digest OK, signature OK … grammar=xmldsig-standalone`, then the full DC charge loop to
  `SessionStop`). The signature verification took a short investigation, since Josev signs the `SignedInfo`
  over a **different EXI grammar** than we do: its `to_exi(signed_info, Namespace.XML_DSIG)` selects
  `BuiltInSchema.XSDCore` / `XMLDSIG_Core_Schema_Grammar` — a grammar built from **`xmldsig-core-schema.xsd`
  standalone** — whereas we (like cbV2G, our authoritative reference) encode the `SignedInfo` as a fragment of
  the full `V2G_CI_CommonMessages` schema set. The EXI *Fragment* grammar's leading element event-code width
  tracks the number of global elements in the loaded schema, so the standalone-xmldsig grammar yields a
  **209-byte** `SignedInfo` (one-bit-narrower top-level code, whole bitstream shifted) vs our/cbV2G **210-byte**
  form, though both decode identically. Our own generator reproduces the 209-byte form byte-for-byte from the
  same schema (`WWCP_ISO15118_XMLDSig` project; `XmlDsigStandaloneGrammarReproducesJosev`), so our SECC
  **verifies** Josev-style signatures via a standalone-xmldsig fallback (`XmlDsigInteropVerify`) while our
  default signing stays cbV2G-byte-exact. See `JosevPnCSignatureDiag`.
- **Live Plug & Charge, forward — our EVCC signs, Josev verifies:** the closing counterpart. `evcc
  --contract-cert <pfx>` switches the EVCC from EIM to a **signed** PnC `AuthorizationReq` (challenge echo +
  contract chain, signed in Josev's exact interop form via `XmlDsigInteropSign`: SHA-256 fragment digest +
  `Transforms`=[EXI C14N], SignedInfo over the **standalone xmldsig** grammar, `ecdsa-sha256` raw `r‖s`).
  A real Josev SECC — re-encoding everything with its own EXIficient codec — logs `=> Match: True` and
  **`Signature verified successfully`**, then runs the full DC PnC session to `SessionStop` over mutual
  TLS 1.3 ([`2026-07-22-iso20-dc-pnc-forward-signed`](docs/interop-runs/2026-07-22-iso20-dc-pnc-forward-signed/)).
  With that, **-20 PnC is live-validated in both directions** (they sign → we verify; we sign → they verify);
  CI guard: `Iso20LoopbackTests.DcPncSession_SignedAuthorization_VerifiesAtSecc`.
- **Live Renegotiation:** mid-session renegotiation in every direction Josev supports
  ([`2026-07-22-renegotiation`](docs/interop-runs/2026-07-22-renegotiation/)). **-2 both ways** [V2G2-841]:
  SECC-triggered (`secc --renegotiate` → `EVSENotification.ReNegotiation` → Josev answers
  `PowerDeliveryReq(Renegotiate)` + a second ChargeParameterDiscovery) and EV-initiated
  (`evcc --renegotiate` vs Josev's SECC) — both to `SessionStop`. **-20** [V2G20-1477]: our SECC's
  `ServiceRenegotiation` notification makes a Josev AC EVCC send a real
  `SessionStopReq(ServiceRenegotiation)`, which our SECC answers **without ending the session**
  (re-entry at ServiceDiscovery) — Josev then drops the link anyway (its EVCC posts the terminating stop
  notification before honouring its own `next_state = ServiceDiscovery`; its DC path even hardcodes
  `Terminate`), so the full -20 cycle incl. the second round is guarded by
  `Secc20DynamicModeTests.ServiceRenegotiation_ReentersServiceDiscovery_AndCompletes`.
- **Live Pause/Resume:** sessions can end with `ChargingSession.Pause` and be rejoined on a fresh
  connection ([`2026-07-22-pause-resume`](docs/interop-runs/2026-07-22-pause-resume/)). Forward vs a real
  Josev SECC: the **-2** flow works end to end — Josev preserves the session context across connections and
  answers the resumed `SessionSetupReq` (old id, after a fresh SDP discovery — Josev moves ports on pause)
  with **`OK_OldSessionJoined`**. For **-20**, Josev preserves an *empty* context (its -20 states never fill
  it) and degrades to a graceful new session — a Josev gap; our own -20 resume answers `OK_OldSessionJoined`
  (loopback E2Es for both protocols). CLI: `evcc --pause-resume`, or `--pause`/`--resume <hex>` as separate
  invocations; the SECC keeps accepting while paused.
- **Live ISO 15118-2 Plug & Charge — both directions:** the full -2 PnC message set runs live over TLS
  ([`2026-07-22-iso2-pnc-tls`](docs/interop-runs/2026-07-22-iso2-pnc-tls/)). Reverse (Josev EVCC → our
  SECC): Contract offered, `PaymentDetails` + GenChallenge, Josev's **signed `AuthorizationReq`** and
  **signed `MeteringReceiptReq`** both verify (`challenge OK, digest OK, signature OK,
  grammar=xmldsig-standalone` — Josev's -2 signatures use the same standalone-xmldsig SignedInfo form as
  its -20 ones), full session to `SessionStop`. Forward (our EVCC → Josev SECC): Contract payment with
  `contract.p12`, our signed `AuthorizationReq` gets `=> Match: True` + `Signature verified successfully`
  from Josev's own verifier. Three live findings fixed en route: `SAScheduleList` is mandatory with
  `EVSEProcessing=Finished` [V2G2-905], a SECC must not demand a receipt on *every* status response (a
  Josev EVCC then loops forever), and the -2 SAP offer must carry protocol version **2.0** (Josev matches
  major version, not just the namespace). CI: `Secc2PnCTests` + the `AcPncSession` loopback E2E.
- **Live smart charging / signed tariffs:** the last declared non-goal, closed in both protocols
  ([`2026-07-22-tariff`](docs/interop-runs/2026-07-22-tariff/)). **-2** (`--tariff-cert`): the SECC offers a
  two-tuple `SAScheduleList` whose SalesTariffs are digitally signed into ONE header signature (one reference
  per tariff, §7.9.2.5) and validates the EV's `PowerDeliveryReq(Start)` (`FAILED_TariffSelectionInvalid` /
  `FAILED_ChargingProfileInvalid` [V2G2-761]); the EVCC verifies the signature, picks the cheapest tuple by
  average `EPriceLevel`, and shapes its `ChargingProfile` to that tuple's `PMaxSchedule`. **-20**: the
  Scheduled-mode `ScheduleExchangeRes` carries a rich **signed `AbsolutePriceSchedule`** (power-banded EUR/kWh
  price rule stacks, ECDSA-P521/SHA-512) instead of the flat `PriceLevelSchedule`. Live, three runs: a Josev
  EVCC consumed our signed two-tuple offer, **chose the cheap tuple** and sent a PMax-shaped profile our
  validation accepted; a Josev AC EVCC consumed the signed -20 `AbsolutePriceSchedule`; and — the surprise —
  Josev's SECC **MO-signs its own SalesTariff** (MO Sub-CA2, the actual spec role), which our EVCC
  **live-verified** (`digests OK, ECDSA OK, grammar=xmldsig-standalone`) — a genuine external oracle for the
  -2 tariff-verification path. Honest residue: our -2 combined-grammar signing form and the -20 price-schedule
  signature have no external verifier (Josev's EVCC-side tariff check is a literal `# TODO`; nothing external
  touches -20 price-schedule signatures) — CI-guarded by `Secc2TariffTests` + the tariff loopback E2Es. One
  more Josev quirk: its pydantic `Reference` model requires the schema-optional `Transforms` on *receive* in
  -2 too, so our tariff references include `Transforms`=[EXI C14N].
- **Live -20 contract provisioning (CertificateInstallation):** our SECC announces the service, **verifies a
  real Josev EVCC's signed `CertificateInstallationReq` live** (OEM provisioning chain; digest + ECDSA over
  the standalone-xmldsig grammar) and issues a **signed, Josev-validated** `CertificateInstallationRes` —
  fresh P-521 dev contract, private scalar wrapped via secp521r1 ECDH → ConcatKDF-SHA512 → AES-256-GCM
  (`ContractProvisioning`; self-consistent-only crypto octets — no independent stack implements -20
  provisioning to diff against). Josev then stops at its own `NotImplementedError` (it implements
  cert-install on neither side), so the live exchange reaches the maximum possible; the **full roundtrip**
  (EVCC requests → SECC issues → EVCC unwraps a *working* contract key) runs in-repo
  (`Iso20LoopbackTests.DcCertInstallSession_ProvisionsAWorkingContractKey`). Three interop findings en route:
  Josev mis-frames the req with V2GTP payload type 0x8001 (a `create_next_message` default-arg bug — our -20
  SECC read path tolerates it), our new `OEMProvisioningCertificateChain` fragment codec is byte-identical to
  EXIficient's (1476 B, real-material digest + signature verify), and Josev's pydantic `Reference` model
  requires the schema-optional `Transforms` (our res now includes the EXI-C14N transform). See
  [`2026-07-22-iso20-certinstall-sdp`](docs/interop-runs/2026-07-22-iso20-certinstall-sdp/).

- **Live SDP discovery (no shim):** `secc --sdp --interface <nic>` now drives a real Josev EVCC end to end —
  the WWCP `SECC_SDPServer` binds `[::]:15118`, joins `FF02::1`, and answers the EVCC's `SDP_Request` with our
  TCP/TLS endpoint (verified: full PnC-over-TLS session to `SessionStop`, [`2026-07-22-iso20-dc-sdp-noshim`](docs/interop-runs/2026-07-22-iso20-dc-sdp-noshim/)).
  The multicast binding was never actually broken; the earlier "SDP multicast" shim was worked around a
  *policy* default — `SECC_SDPServerOptions.RejectNoTlsRequests` is `true` (TLS-deployment-oriented), so a
  **plaintext** SECC silently dropped a plaintext EVCC's `SDP_Request`. The CLI now sets it from our own TLS
  mode (`RejectNoTlsRequests = !noTls`), so plaintext `--sdp` discovery works too.

- **Live -20 AC:** full AC session (`ACChargeParameterDiscovery` + `ACChargeLoop`) to `SessionStop` against a
  real Josev EVCC, both over **plain TCP + `--sdp`** ([`2026-07-22-iso20-ac-eim-sdp`](docs/interop-runs/2026-07-22-iso20-ac-eim-sdp/))
  and over **mutual TLS 1.3 + `--sdp`** ([`2026-07-22-iso20-ac-tls-sdp`](docs/interop-runs/2026-07-22-iso20-ac-tls-sdp/)),
  each re-confirming plaintext/TLS `--sdp` discovery and PnC signature verify (`grammar=xmldsig-standalone`).

- **Live -20 bidirectional (BPT / V2G):** full **DC_BPT** and **AC_BPT** sessions to `SessionStop`
  ([`2026-07-22-iso20-dc-bpt-sdp`](docs/interop-runs/2026-07-22-iso20-dc-bpt-sdp/),
  [`2026-07-22-iso20-ac-bpt-sdp`](docs/interop-runs/2026-07-22-iso20-ac-bpt-sdp/)). The SECC advertises both the
  unidirectional and BPT energy-transfer services (DC `{2,6}`, AC `{1,5}`) and, because the -20 CPD/charge-loop
  energy-transfer-mode & control-mode are polymorphic (`BPT_*` derives from the unidirectional type), replies
  with the matching charge-**and-discharge** variant whenever the EV sends a BPT request — Josev selects
  `ServiceID 6`/`5` and runs the charge loop with discharge parameters. Backward-compatible: unidirectional
  runs still select service `2`/`1`.

- **Live -20 Dynamic control mode:** full **DC, DC_BPT and AC_BPT** sessions in **Dynamic** mode to
  `SessionStop` ([`2026-07-22-iso20-dynamic-sdp`](docs/interop-runs/2026-07-22-iso20-dynamic-sdp/)). The SECC
  offers both control-mode parameter sets (Scheduled=1, Dynamic=2; `secc --dynamic` puts Dynamic first, which
  is what a Josev EVCC then adopts) and answers ScheduleExchange + charge loop **strictly in kind** across all
  four control-mode variants — fixing a latent wire-type mismatch where a Dynamic-mode EV got Scheduled res
  types (guarded by `Secc20DynamicModeTests`).

All four -20 energy modes any independent stack implements — **DC, AC, DC_BPT, AC_BPT** — now run live against
Josev over TCP and TLS, plain and Plug & Charge, in Scheduled **and** Dynamic control mode. **WPT and ACDP
stay codec-validated only** (record mode, byte-exact vs cbV2G): no live run is possible because Josev — the
only independent -20 stack available — implements no WPT/ACDP session state machines (only AC/DC), and our own
WPT/ACDP projects are codec-only by the same token; a live run would need full session state machines built on
both sides.

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
| Plug & Charge (over TLS) | ✅ both directions, signed msgs verified both ways | ◐ chain accepted + our signature verified; their SIL has no eMAID backend³ | — | — |
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
| Plug & Charge | ✅ | — commented out on their side | ▢ | — |
| CertificateInstallation | ◐ our signed res verified; their impl ends at its own `NotImplementedError` | — | — | — |
| Mutual TLS 1.3 | ✅ (their P-256 PKI) | ✅ full session⁶ | — (plain TCP only) | — |
| SDP discovery | ✅ both directions | ✅ multicast (unicast: fixed in 2026.02.1) | ✅ their EV found our SECC | — |
| Multi-protocol SAP offer | — | ✅ IsoMux, all four offer shapes⁷ | — | — |
| WPT · ACDP | *codec-validated only — no independent stack implements session state machines for them* | | | |
| MCS | *first counterpart in sight: 2026.02.1 ships `config-sil-mcs.yaml`; our interop fixture has no MCS arm yet* | | | |

¹ EVerest's current `EvseV2G` sits on cbV2G — the encoder our vector corpus is generated from — so
byte-level agreement there is not independent. The 2023.10.0 demo image ran **OpenV2G**, which *was* an
independent-codec witness; Josev (EXIficient) and eVDriveFlow (OpenEXI) are the standing independent
lineages.
² Their responder replays a captured car and refuses any request whose identifiers differ from the
recording — a property of their tool, not an interop verdict.
³ Their station-side rule "no Contract without TLS" was also the first external check of that spec
requirement against us.
⁴ Their defect (optional element dereferenced; one more in the charge loop), three findings filed in
the run notes — and 12 of our -20 messages decoded clean by a second independent codec.
⁵ Their -20 AC SIL waits on its own EV module's power-ready callback, which a foreign EV cannot
produce; not reachable from the wire.
⁶ Complete charge over mutual TLS 1.3 on 2025.10.0; on 2026.02.1 the station side is re-validated
(bridged client). Caveat of ours: on Windows, Schannel refuses to present a test-PKI client chain, so
the conformant -20 TLS client remains proven from the macOS/BouncyCastle path.
⁷ And the finding that goes with it: `IsoMux` routes on "mentions -20 anywhere", never reading SAP
`Priority` — confirmed on the wire against 2025.10.0 **and** 2026.02.1.

**EVerest, current state:** the full forward matrix — -2 DC/AC, -20 DC Scheduled **and** Dynamic, `IsoMux`
in all four offer shapes, -20 DC over mutual TLS 1.3 — is green against **everest-core 2025.10.0** (demo
image, 02/03.08) **and re-validated against 2026.02.1 built from source**
([`2026-08-05-everest-2026021-matrix`](docs/interop-runs/2026-08-05-everest-2026021-matrix/notes.md)).
Standing deltas on 2026.02.1: their unicast-SDP loop shutdown is fixed, while the refused-TLS-handshake
one persists and turns out to be reachable from their *stock* SIL config by one `openssl s_client`
line — after it, the charger answers nothing while its process stays healthy (report ready to file,
[`docs/reports/everest-loop-shutdown.md`](docs/reports/everest-loop-shutdown.md)),
`IsoMux` still ignores SAP `Priority`, their stock SIL -20 config went Dynamic-only, and
`config-sil-mcs.yaml` now exists — the first MCS counterpart in sight (our fixture has no MCS arm yet).
Known bounds: -20 AC still stops at their SIL's own-EV contactor coupling; PnC not yet repeated on
2026.02.1; on Windows the -20 mutual-TLS client needs the BouncyCastle path made reachable (Schannel
refuses untrusted-root client chains — station side bridged and green).

---

The stack all of this tests — the EXI codec, the state machines, the TLS/PKI, the CLI — is documented in
**[EVSimulatorApp](libs/EVSimulatorApp)**. This repository is only the judge.
