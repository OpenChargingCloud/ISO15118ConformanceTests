# Roadmap & status

Last updated: **2026-07-25**. Authoritative per-phase detail lives in
[`docs/prompts/`](prompts/README.md) (the phase prompts + their status table) and the
[`README.md`](../README.md); this file is the bird's-eye plan and the "why".

## Current status

**All phases (0–5) are complete.** The solution builds cleanly and **all 1116 tests are green**
(`dotnet test -c Release`, measured 2026-08-01: 911 in `Vanaheimr.V2G.Exi.Tests`, 197 in
`Vanaheimr.V2G.Simulation.Tests`, 8 in `Vanaheimr.V2G.Experiments.Pqc.Tests`) — offline, with no C
toolchain, JRE, or network beyond loopback. The live over-the-wire interop tests stay
`[Explicit] [Category("Interop")]` and script-driven: eight of them now, four counterparties × two
directions, and they are *not* in that count.

ISO 15118-2 and -20 are **feature-complete at session level** and validated live against the independent
Josev stack in every direction Josev supports — the feature-gap list is **empty**:

- **Both protocols, all four -20 energy modes** (DC, AC, DC_BPT, AC_BPT), plain TCP **and** TLS
  (1.2 unilateral / 1.3 mutual), EIM **and** Plug & Charge, Scheduled **and** Dynamic control mode.
- **Plug & Charge live in both directions in both protocols** — they sign → we verify, we sign → they
  verify (dual-grammar: our cbV2G-byte-exact combined form + Josev's standalone-xmldsig form).
- **-2 PnC session flow**: PaymentDetails, signed AuthorizationReq, signed MeteringReceiptReq — all live.
- **-20 contract provisioning** (CertificateInstallation): live to the maximum Josev allows, full
  roundtrip incl. working-key unwrap in-repo.
- **Pause/Resume** ([V2G2-740]) and **Renegotiation** (-2 [V2G2-841], -20 ServiceRenegotiation
  [V2G20-1477]) — live where Josev can follow, CI-guarded beyond.
- **Smart charging / signed tariffs** (the last declared non-goal, closed 2026-07-23): signed -2
  SalesTariff offers (§7.9.2.5) with EVCC-side verification, cheapest-tuple choice and PMax-shaped
  ChargingProfiles (SECC-validated, [V2G2-761]); signed -20 AbsolutePriceSchedule. Live highlight: our
  EVCC **verified a real MO-Sub-CA2-signed Josev SalesTariff** — an external oracle for the tariff
  verification path.

En route, the live runs caught and fixed **~15 real conformance bugs** invisible to loopback on our side
and documented **~12 Josev bugs/gaps**. The full story: [Phase 5 closing report](phase5-report.md) (DoD
scorecard + honest-gaps ledger) and the per-run write-ups under [`docs/interop-runs/`](interop-runs/).

The scope of that claim is worth stating precisely: **validated against Josev**, which is one live peer.
Two peers of the same lineage cannot show what they have agreed to be wrong about, and ~15 fixes from
the first one is a reason to expect more from the second. Harnesses for three further counterparties are
written and **have not been run** — see [Live counterparties beyond Josev](#live-counterparties-beyond-josev)
for what each would prove, and [What remains](#what-remains) for what is blocking them.

| Phase | Scope | Status |
|---|---|---|
| 0 | SupportedAppProtocol wire conformance vs cbV2G (real seed vectors, pinned commit) | ✅ **done** |
| 1 | EXI primitive layer (signed int, binary, boolean, string value tables) | ✅ **done** |
| 2 | Source generator lifted to the real ISO 15118-2 schema set | ✅ **done** |
| 3 | All 17 -2 message pairs + XMLDSig over EXI fragments (ECDSA-P256/SHA-256) | ✅ **done** |
| 4 | ISO 15118-20: five codec assemblies (CommonMessages/DC/AC/WPT/ACDP) + V2GTP dispatch + XMLDSig | ✅ **done** |
| 5 | EV↔EVSE simulation (SLAC, SDP, TCP/TLS incl. mutual, state machines, Josev interop, PnC/cert-install/pause/renegotiation/tariffs live) | ✅ **done** |

**Beyond the phases** — three additions outside the original roadmap, each honest about its own limits;
detail and what stays parked in [Completed extras](#completed-extras):

| Addition | Status |
|---|---|
| **AC DER** (-20 Amendment 1) — two AC grammar variants (`AC_DER_IEC`, `AC_DER_SAE`); cross-validated against EXIficient (decode direction). **No session wiring** (payload type / `ProtocolNamespace` live in the amendment text we don't have). [`docs/ac-der.md`](ac-der.md) | ✅ codec done |
| **MCS** (Megawatt Charging System) — `Secc20Mcs`/`Evcc20Mcs`: the DC message set under service ids 8/9 with a megawatt envelope; no codec work needed. **Ids taken from EVerest, not validated against a live MCS counterpart.** | ✅ done |
| **PQC experiments** — ML-KEM-1024 TLS session + ML-DSA-87 signatures, BC ↔ .NET cross-validated, with the EXI/CBOR/JSON size verdict. **Wire-non-conformant by design**, never a production default. [`docs/experiments/pqc.md`](experiments/pqc.md) | ✅ experiment done |

What exists today, at a glance:

| Component | State |
|---|---|
| ✅ [BitReader/BitWriter](../Vanaheimr.V2G.Exi.Prototype/Exi/BitReader.cs) | Bit-packed streams, MSB-first |
| ✅ [ExiPrimitives](../Vanaheimr.V2G.Exi.Prototype/Exi/ExiPrimitives.cs) + `ExiStringTable` | Unsigned/signed integer, binary, boolean, n-bit, string values incl. local+global value tables (decode-side; encode is miss-only, matching cbV2G) |
| ✅ [V2GTP](../Vanaheimr.V2G.Exi.Prototype/V2GTP/V2GTP.cs) + [Dispatch](../Vanaheimr.V2G.Exi.Dispatch/V2GTPDispatcher.cs) | 8-byte transport header; payload-type → codec dispatcher over all seven sets |
| ✅ [SourceGenerator](../Vanaheimr.V2G.Exi.SourceGenerator/ExiCodecGenerator.cs) | `IIncrementalGenerator`: XSD set → grammar plan → C# document + fragment codecs; fail-loud on unknown constructs; emits block-scoped namespaces |
| ✅ ISO 15118-2 codec | All 17 message pairs **byte-exact vs cbV2G**; signed `AuthorizationReq` byte-exact; `SignedInfo` fragment cross-checked vs EXIficient |
| ✅ ISO 15118-20 codecs (×5) | CommonMessages/DC/AC/WPT/ACDP all generate + compile + byte-exact vs cbV2G; XMLDSig for CommonMessages/DC/AC (ECDSA-P521/SHA-512 **and** Ed448 via BouncyCastle) |
| ✅ ISO 15118-20 Amd 1 AC DER (×2) | `AC_DER_IEC`/`AC_DER_SAE` — grammar variants of AC, not further message sets; cross-validated vs EXIficient (decode direction), no cbV2G reference exists. Codec only, no session wiring — see [Completed extras](#completed-extras) |
| ✅ [Simulation](../Vanaheimr.V2G.Simulation/) (Phase 5) | Full in-repo stack over loopback: **SLAC** pairing (real UDP match) → **SDP** discovery seam → **TLS** (two backends: .NET SslStream + BouncyCastle -20-faithful P-521/Ed448 mutual TLS) → SAP → -2/-20 AC/DC sessions to SessionStop; a full-stack SLAC→SDP→TLS→session E2E; CLI with stage/backend flags. Live vs Josev: all four -20 energy modes + both control modes, PnC both directions in both protocols, cert-install, pause/resume, renegotiation, signed tariffs |
| ✅ Test infrastructure | Vector-driven (JSON), bit-exact diff on failure; property-based round-trips (CsCheck); reference oracles pinned under `tools/` |

The original "decisive weakness" (self-encoded seed vectors that only proved internal
consistency) is **closed everywhere**: `expectedHex` is generated by EVerest's libcbv2g at a
pinned commit, so green proves wire conformance. The `SignedInfo` fragments (-2 and -20
CommonMessages) are additionally cross-validated against EXIficient, an independent
W3C-EXI processor.

The last self-referential corner fell on 2026-07-25: `Primitives.vectors.json` — the schema-less
EXI §7.1 datatypes, which cbV2G structurally cannot cover — is now reproduced byte-for-byte by
EXIficient's `BitEncoderChannel` (23/23, `tools/exificient-ref/primitives.py`). That includes five
**non-ASCII** string vectors sourced *from* EXIficient, since cbV2G rejects code points > U+007F
outright; the astral ones (U+1F600, one code point but two UTF-16 units) pin our rune-wise encoding
against a code-unit-wise reading. **No vector file in the repo now proves only its own consistency.**

## What remains

Everything the roadmap targeted is done — see [`phase5-report.md`](phase5-report.md) for the full
scorecard. What is left over is either a **structural non-goal** (no independent counterpart exists to
validate against), a small cleanup, or — since 2026-08-01 — a run that has not happened yet:

**⬜ Run the three new counterparties.** Harnesses exist for
[tux-evse](../tools/interop-tux-evse/README.md), [eVDriveFlow](../tools/interop-evdriveflow/README.md)
and [EVerest](../tools/interop-everest/README.md), with `[Explicit]` fixtures, a recorder and a flow
report — and **not one byte has crossed between our stack and any of the three**. Josev is still the only
counterparty with recorded sessions. Everything checked so far is what was checkable without a peer, so
nothing here should be read as an interop result. What each run would prove, and what it cannot, is in
[Live counterparties beyond Josev](#live-counterparties-beyond-josev).

The blocker is a machine, not code: tux-evse needs Linux veth pairs and podman, EVerest a full
`everest-core` build. eVDriveFlow is the likeliest to run on a developer box (Python + conda), and
whether SDP over link-local IPv6 works there is itself the first question. Each harness carries its own
*confirm on first contact* list — those are questions, not statements, and the first run is what turns
them into either.

**Remaining non-goals** (would need something that doesn't exist yet):
- ⬜ **WPT / ACDP session state machines** — codecs are byte-exact vs cbV2G, but no independent stack
  implements WPT/ACDP sessions (Josev has AC/DC only), so a live run would require building state
  machines on *both* sides with no oracle for the behaviour.
- ⬜ **-2 `CertificateInstallation`/`CertificateUpdate` live** — the messages are codec-tested; a live
  run would need Josev's -2 CERTIFICATE VAS wiring on both sides (its service path is unimplemented).
  *Possibly no longer structural:* EVerest carries `Iso15118InternetVas` and
  `StaticISO15118VASProvider` modules, and `EvseV2G` declares an `ISO15118_vas` requirement. Whether
  either covers the -2 certificate service is unchecked — worth asking on first contact rather than
  assuming this stays a non-goal.
- ⬜ **Self-consistent-only crypto/grammar spots** — the -20 contract-provisioning crypto octets
  (ECDH/ConcatKDF/AES-GCM), our -2 combined-grammar *tariff-signing* form, -20 price-schedule
  signatures, and two WPT grammar shapes: schema-valid and CI-guarded, but nothing external produces
  or checks them (documented per case).

Cleanups / smaller follow-ups (not blockers):
- ⬜ **Slim down Hermod** — the SLAC stage pulls the heavy `Hermod`/`Styx` chain into the core
  Simulation library (a deliberate Option-A tradeoff); revisit once Hermod is leaner, or split
  SLAC into a separate integration project.
- ⬜ **SDP over the wire in CI** — only the SDP message layer + result mapping are CI-tested; the
  live UDP/IPv6 multicast exchange isn't (an EVCC+SECC in one process on one host can't hear each
  other's multicast). A two-host or loopback-unicast test mode would close this. (Live it works —
  every `--sdp` interop run exercises it, both directions.)

**Future ideas** (beyond the original roadmap, parked with a concrete trigger each). Things that
have already landed are in [Completed extras](#completed-extras) below:
- ⬜ **ISO 15118-8 wireless-link demo** — -8 profiles 802.11n as the wireless PHY/DLL (buses,
  pantograph, WPT); it carries **no EXI schemas or messages**, and from IP upward our stack runs
  unchanged — so the honest slice is a link-agnosticism demo, not codec work: virtual 802.11 radios
  via Linux `mac80211_hwsim`, `hostapd` (EVSE as AP) + `wpa_supplicant` (EV as station) doing a real
  802.11 association in software — both canonical, independent implementations of the only layer -8
  actually touches — then the existing SDP → TLS → session pipeline (ideally vs Josev) over that
  link, as an optional front stage analogous to SLAC. Limits: this validates 802.11 conformance, not
  -8-specific RF/channel/timing requirements (hardware territory); WSL2's stock kernel lacks
  `mac80211_hwsim` (custom kernel or a small Linux VM).
- ⬜ **VDV 261 bus-depot VAS** (preconditioning) — see the research notes in the session task list:
  not EXI over the cable, but a 15118-negotiated VAS after which the SECC bridges IPv6 (RS/RA) and
  the bus talks HTTPS/JSON to the dispositive backend; VDV 463 schemas are public
  (github.com/VDVde/VDV463), Siemens DepotFinity documents a VDV-261 REST API. Trigger: access to a
  testable counterpart (e.g. a DepotFinity sandbox).
- 🔁 **Standing: track the EVerest counterparts** (task #82) — the counterpart stacks are moving
  targets and were reshuffled into the EVerest monorepo in early 2026 (see "EVerest higher-layer
  stacks" under Reference libraries). Periodically pull libcbv2g/cbexigen, Josev/ext-switchev and the
  monorepo modules, re-run our vector + loopback + live-interop suites against the current versions,
  and reconcile the drift — new counterpart features to match, our own bugs to fix, fresh counterpart
  bugs to document (~12 Josev findings so far). Also the natural moment to revisit the pinned
  `03350be` codec commit. Standing up an EVerest node once — which unlocks `EvseV2G` /
  `Evse15118D20` / `IsoMux` as live counterparts — is no longer a design question but a scheduling
  one: the harness is written and waiting ([`tools/interop-everest/`](../tools/interop-everest/README.md)).

### Completed extras

Work that landed **outside the original phase plan** — real, tested, and each honest about what it
is not. Both started life as "future ideas"; what remains parked is listed with each.

#### ✅ AC DER (-20 Amendment 1) — codec done (2026-07-25)

Full write-up: [`docs/ac-der.md`](ac-der.md).

It turned out **not** to be a new message set: both amendment schemas import the base AC schema,
leave the message roots commented out, and contribute six `DER_*` **substitution-group members**
extending AC's own types — structurally the same pattern AC already uses for `BPT_*`, so **the
source generator needed no changes**. Shipped as two grammar variants of AC
(`Iso15118_20.AC_DER_IEC`, `…_SAE`, each compiling AC + DER + CommonTypes + xmldsig), 5 tests.

Measured surprise, contradicting the initial expectation: a **plain, non-DER AC message encodes
byte-identically under both grammars** — the added members don't push the event code over an n-bit
boundary — so plain AC traffic stays compatible and only DER-carrying messages are unreadable to a
plain AC peer. A regression test pins that, since a further amendment adding more members to the
same group could silently change it.

**Validation.** A cbexigen byte oracle was attempted and is **blocked upstream**: cbexigen crashes
analysing the amendment schemas (`IndexError` in `SchemaAnalyzer.__replace_particle_list_in_parent`)
because a substitution-group head — here `CLReqControlMode` — receiving members from *two* schemas
is registered twice and never de-duplicated. Not patched on purpose: a self-patched oracle is not
independent for the construct under test. **Externally cross-validated against EXIficient instead** —
calibrated first on a plain AC message where cbV2G ground truth exists, then our AC+DER bytes decoded
correctly against the amendment grammar, the inherited fields coming back in the `:-20:AC` namespace
and the DER-only fields in `:-20:AC-DER-IEC`. Decode direction only (EXIficient's encoder profile
differs for all our message sets) and outside `dotnet test` since it needs Java; fixtures in
`tools/exificient-ref/fixtures/`.

**Parked:** V2GTP dispatch and SAP negotiation (the payload type / `ProtocolNamespace` are in the
amendment *text*, which we don't have — guessing is exactly what the ground rule forbids); the SAE
`DER_*` members (four mandatory limit structures, worth building once there's an encode-side oracle).
EVerest doesn't implement AC DER either, so there is no live counterpart.

#### ✅ MCS — Megawatt Charging System (2026-07-25)

`Secc20Mcs` / `Evcc20Mcs` + `Secc20McsTests` (2 tests).

The earlier assessment held exactly: **no codec work, no new XSD**. MCS is the **DC message set**
advertised under different service ids, so both classes are thin subclasses of the DC pair and the
whole difference is three things:

- **service ids 8 (MCS) / 9 (MCS_BPT)** instead of DC's 2 / 6 — `serviceIDType` is a plain
  `xs:unsignedShort`, so no schema needed changing to carry them;
- the **`Connector`** parameter naming the MCS connector family (1 = MCS, 4 = rMCS, 5 = xMCS); the
  ServiceDetail parameter set is otherwise structurally identical to DC's
  (Connector / ControlMode / MobilityNeedsMode / Pricing);
- a **megawatt envelope** in the charge-parameter response (3.75 MW / 3000 A / 1250 V), which is why
  `Secc20Dc`'s limits became `virtual`.

`Evcc20Base.SelectedEnergyServiceId` is now exposed, because which service a session settled on is
otherwise invisible from outside — and it is the *only* thing distinguishing an MCS session from a DC
one on the wire. A megawatt vehicle at a plain DC charger falls back to service 2/6 rather than
aborting; the second test pins that, so MCS support is strictly additive.

**Not externally validated.** The service ids and connector values are taken from EVerest's
`libiso15118`, whose neighbouring values (AC=1, DC=2, DC_BPT=6) match ours exactly — but nothing here
has been byte-diffed or run against a live MCS counterpart, and the physical limits are plausible
headline figures rather than values read out of the Amendment text.

**Parked:** the limits and behavioural detail from the Amendment text, and a live run against
`Evse15118D20` — the only maintained stack that implements MCS (see the counterpart-tracking task).

*Concrete since 2026-08-01:* EVerest ships **`config/config-sil-mcs.yaml`**, so the live counterpart
for MCS is a named configuration rather than a hope, and the harness to drive it exists
([`tools/interop-everest/`](../tools/interop-everest/README.md)). It is deliberately last in that
harness's scenario order — the plain -2 and -20 sessions have to be clean before a service-id question
is worth asking — but it is the first thing that could turn "not externally validated" above into
either a confirmation or a finding.

#### ✅ Post-quantum crypto experiments (2026-07-23)

`Vanaheimr.V2G.Experiments.Pqc` + tests; results in [`docs/experiments/pqc.md`](experiments/pqc.md).
Both experiments run in CI, clearly flagged **wire-non-conformant** (both editions pin classical
suites; Ed448 is EC, *not* post-quantum) — never a production default:

1. A complete **-20 DC session over an ML-KEM-1024 (FIPS 203) TLS 1.3 key exchange** via the
   BouncyCastle backend (`BcTlsOptions.ExperimentalNamedGroups`; BC 2.6.2 has the pure-ML-KEM
   codepoints, not yet the browser hybrid), with a classical-vs-PQC negative control.
2. An **ML-DSA-87 (FIPS 204) signature suite** behind an experimental URI — the generated EXI codec
   carries the 4 627-byte signature unchanged, full sign→encode→decode→verify roundtrip,
   **cross-validated between BouncyCastle and .NET 10's native `MLDsa`** (two independent FIPS 204
   implementations, both directions, raw key interchange — an internal oracle for the primitive).

Headline measurement (EXI vs CBOR vs JSON): the PnC `AuthorizationReq` flips from ~10 % signature
(P-521) to **~80 % signature** (ML-DSA-87); EXI's saving over base64-JSON (2.3 KB) is smaller than
the signature it carries (4.6 KB), and against binary-clean **CBOR it collapses to ~330 B = 5.4 %** —
in a PQC 15118, the encoding choice becomes a rounding error.

**Parked:** the `X25519MLKEM768` *hybrid* once BC ships it; PQC certificate chains (projected ~23 KB
per 3-cert chain). Trigger for anything beyond the experiment: a 15118 draft/amendment (or CharIN
profile) with actual PQC commitments; no 15118-external oracle exists either way.

**Resolved across Phase 5** (each was once an open gap):
- ✅ **Mutual-TLS client-certificate context** (2026-07-25) — `TcpV2GClient` always wrapped the client
  certificate in an `SslStreamCertificateContext`, even with no chain to send. `Create` builds a chain
  over the leaf against the **platform** trust store (no custom-trust hook), so it fails for any
  certificate whose issuer the machine does not know — a real EV with an uninstalled OEM root, not just
  our test CA. Now the context is used only when there are intermediates to transmit; otherwise the leaf
  goes out via `ClientCertificates` and path building is left to the peer, per the -2/-20 trust model.
  Compounded by a test-PKI defect: every run minted a root with the *identical* subject, and Windows'
  name-indexed certificate cache then mixed roots across runs (`NotSignatureValid`) — fixed with a unique
  `CommonNameSuffix`. Full write-up in [`phase5-report.md`](phase5-report.md) §5.
- ✅ **EVCC-side live SDP** (2026-07-23) — the CLI EVCC's `EVCC_SDPClient` timed out against a live
  Josev SECC; root cause was neither bind nor scope handling but the client's hardcoded
  `IPV6_MULTICAST_LOOP = off`: on a single-host setup (Josev in Docker/WSL on the same machine) the
  ff02::1 SDP_Request only reaches a *local* SECC via multicast loopback. Now an option
  (`MulticastLoopback`, default off = real-hardware behaviour) which the CLI enables, plus the
  EVCC-side mirror of the NoTLS policy fix (`RejectNoTlsResponses` follows the CLI's TLS mode).
  `evcc --sdp` verified live (plaintext + TLS); every forward interop script now uses it natively —
  the in-script python SDP probes are gone.
- ✅ **SLAC pairing**, **SDP discovery** (incl. live no-shim `--sdp` after the `RejectNoTlsRequests`
  policy fix), **mutual TLS 1.3** (two backends: .NET `SslStream` P-256, BouncyCastle -20-faithful
  secp521r1/Ed448), **Vehicle certificate** (CharIN 2nd-gen PKI), **full-stack E2E**
  (SLAC → SDP → mTLS → SAP → session), and the **xmldsig `Transforms` generator gap** found by record mode.
- ✅ **Live Josev interop far beyond record mode:** complete sessions in both directions, plain + TLS,
  all four -20 energy modes, both control modes ([V2G20-2656] both parameter sets offered, answered in
  kind), graceful any-phase `SessionStop`, poll-phase self-looping.
- ✅ **Plug & Charge live, both directions, both protocols** — incl. the dual-grammar signature story
  (our combined cbV2G-byte-exact form + Josev's standalone-xmldsig form, sign and verify both ways).
- ✅ **-20 contract provisioning** live to Josev's maximum + full in-repo roundtrip with working key unwrap.
- ✅ **Pause/Resume** ([V2G2-740]) and **Renegotiation** ([V2G2-841]/[V2G20-1477]) — live where Josev
  can follow; its limits documented as Josev gaps, the full cycles CI-guarded.
- ✅ **Smart charging / signed tariffs** — signed -2 SalesTariff offers + EVCC verification +
  cheapest-tuple choice + PMax-shaped, SECC-validated ChargingProfiles; signed -20
  AbsolutePriceSchedule; live incl. verifying a real MO-Sub-CA2-signed Josev tariff
  (`2026-07-22-tariff`).

---

## Background: what -2 and -20 required (why the phases were shaped this way)

Retained as the design rationale behind the plan above — all of this is now implemented;
it explains *why* the generator and codec look the way they do.

**ISO 15118-2** (one schema set: `V2G_CI_MsgDef` + MsgHeader/MsgBody/MsgDataTypes + XMLDSig):
- All ~36 messages sit inside one `V2G_Message` wrapper; the body is a **substitution group**
  over an abstract `BodyElement`.
- **Attributes** (AT events, e.g. `Id` for signatures), **xs:choice**, abstract types
  (`EntryType`/`IntervalType`), `maxOccurs="unbounded"`.
- Data types: `hexBinary` (SessionID), `base64Binary` (XMLDSig), **signed** integers (EXI
  encoding: sign bit + unsigned), `short`/`byte` for `PhysicalValueType`.
- **XMLDSig over EXI fragment grammars**: for Plug & Charge (AuthorizationReq,
  MeteringReceiptReq), the referenced body element is canonically encoded as an EXI
  *fragment*, hashed, and the `SignedInfo` itself EXI-encoded and signed. The hardest part
  of 15118, and unavoidable for a PnC-capable simulation.
- EXI options are fixed (bit-packed, non-strict, schema-informed, header `0x80`), but
  `valuePartitionCapacity` is unbounded → **string value tables** are a normative
  requirement on the decoder (even though cbV2G itself never emits hits).

**ISO 15118-20** (multiple schema sets: CommonMessages, AC, DC, WPT, ACDP + CommonTypes + XMLDSig):
- No more `V2G_Message` wrapper; every message is a global element with its own header
  (SessionID, TimeStamp, optional Signature).
- **One EXI grammar set per namespace** — the decoder selects the grammar via the V2GTP
  payload type. Architecturally: one generated codec assembly per schema set, plus a
  dispatcher.
- More messages, deeper nesting, `RationalNumberType`, stronger crypto suites
  (secp521r1/SHA-512, Ed448). Bidirectional charging (Scheduled/Dynamic mode) grows the
  state machine, not the codec.

## Reference libraries for automated testing

Three classes of oracle, with independent sources of error (a fourth class — further **live**
counterparties — is [below](#live-counterparties-beyond-josev)):

1. **[EVerest/libcbv2g](https://github.com/EVerest/libcbv2g)** + generator
   **[cbexigen](https://github.com/EVerest/cbexigen)** (C, Apache-2.0) — *primary diff
   oracle*. Covers DIN 70121, -2, and -20; runs in production in EVerest. Wrapped as a CLI
   harness under `tools/cbv2g-ref/` (pinned commit `03350be`): same input → byte diff, fast
   enough for every-commit CI. The XSDs ship with the repo — this also solved schema sourcing.
2. **[EXIficient](https://github.com/EXIficient/exificient)** (Java, generic W3C EXI
   processor) — *spec oracle*. A counter-check against cbV2G's own simplifications; where
   both independently produce the same bytes, confidence is high. Wired up under
   `tools/exificient-ref/` for the `SignedInfo` fragment cross-check (-2 and -20
   CommonMessages).
3. **[SwitchEV/iso15118 (Josev)](https://github.com/SwitchEV/iso15118)** (Python,
   Apache-2.0, -2 and -20) — *session-level oracle* for the Phase 5 end-to-end interop, used to the
   fullest: byte-exact record mode (`JosevCapturedFrames{,Dc,20}Tests`, in CI — our codec ≡ EXIficient
   on every captured frame) **plus complete live sessions in both directions** across -2 and -20, plain
   TCP and TLS, EIM and PnC, all four -20 energy modes, both control modes, pause/resume, renegotiation
   and signed tariffs (see `docs/interop-runs/`). ~12 Josev bugs/gaps documented en route (payload-type
   0x8001 framing, pydantic `Transforms`-required, empty -20 pause context, renegotiation drops, tariff
   verification TODO, …). The EVerest fork
   [ext-switchev-iso15118](https://github.com/EVerest/ext-switchev-iso15118) is the more
   actively maintained branch of this same codebase (Python, -2/-20/-8).

### Live counterparties beyond Josev

*(harnesses built 2026-08-01; **none of the three has been run against yet** — Josev remains the only
counterparty with recorded sessions under `docs/interop-runs/`)*

Josev was one live peer, and one live peer cannot show what two implementations of the *same* lineage
have agreed to be wrong about. These three extend that, and they do not extend it equally — the useful
question for each is **what a disagreement with it would prove**:

| Counterparty | Harness | EXI lineage | What a disagreement means |
|---|---|---|---|
| [tux-evse/iso15118-simulator-rs](https://github.com/tux-evse/iso15118-simulator-rs) (Rust, -2) | [`tools/interop-tux-evse/`](../tools/interop-tux-evse/README.md) | cbexigen — **ours** | never EXI, by construction: sequencing, framing or timing |
| [EDF-Lab/eVDriveFlow](https://github.com/EDF-Lab/eVDriveFlow) (Python, **-20 Ed. 1**) | [`tools/interop-evdriveflow/`](../tools/interop-evdriveflow/README.md) | **OpenEXI — independent** | either layer: the only counterparty that is an oracle for both |
| [EVerest](https://github.com/EVerest/everest-core) (C/C++/Python, DIN/-2/-20) | [`tools/interop-everest/`](../tools/interop-everest/README.md) | cbV2G — **ours** (car side: Josev) | station side: sequencing/timing. Car side: little — `PyEvJosev` is Josev in a wrapper |

What each is actually *for*, beyond the byte question:

- **tux-evse** — its side is a **replayer of packet captures**, not a state machine. A reverse run puts
  a real car's route (an Audi against an ABB charger, in the file they ship) in front of our SECC. Their
  scenario file is therefore a *declared flow*, and the harness reads it as one and diffs it against what
  crossed the wire.
- **eVDriveFlow** — **-20 + DC BPT + Dynamic control mode + mutual TLS 1.3**, the combination we have the
  least outside evidence for. `docs/pki-model.md` pins -20 to a mutual TLS 1.3 handshake and our own
  tests have been the only thing that ever checked it. Dynamic mode also drives schedule renegotiation
  beyond the points our corpus happens to record.
- **EVerest** — the implementation most likely to be on the other end of a real charger, so "works
  against EVerest" is closer to a market claim than a test result. `modules/EVSE/Evse15118D20` is where
  -20 lives now (`libiso15118` was archived 2026-02-26 and folded in); `IsoMux` answers -2 and -20 behind
  one endpoint. `config/config-sil-mcs.yaml` would be **the first live counterpart our MCS support has
  ever had** — see the MCS line in Phase 5.

**Shared machinery, so a fourth harness is mostly a README.** One vocabulary of environment variables
(`V2G_INTEROP_SECC`, `_LISTEN`, `_PROTOCOL`, `_MODE`, `_TLS`, `_DYNAMIC`, `_RECORD`, `_SCENARIO`), one
set of `[Explicit] [Category("Interop")]` fixtures — eight now, four counterparties × two directions —
and one recorder. Every run can leave the raw octets of both directions, a named frame log, a `flow.md`,
and a replayable `SessionTrace`; the bytes are written before the trace is attempted, because the run
that *fails* is the interesting one and it is exactly the run a strict corpus builder refuses.

`flow.md` is the part aimed at what these runs are for. It compares the message sequence in **both**
directions against a reference — a counterparty's scenario where one exists, otherwise one of our own
recorded traces — with consecutive repeats collapsed, so a poll loop does not bury the finding. For a
station-side counterparty the *station → EV* half is the one that carries the news.

Additional, bounded roles:
- **[OpenV2G](https://github.com/Martin-P/OpenV2G)** (C, LGPL) — historical DIN/-2
  reference, a third byte-level vote in disputed cases; no -20, frozen.
- **RISE-V2G** (Java, archived, -2 only) — PnC signature test data + a second full -2 stack.
- **[ChargePoint/wireshark-v2g](https://github.com/ChargePoint/wireshark-v2g)** (C, active) — a
  Wireshark dissector for DIN/-2/-20 built *on* libcbv2g. Useful for field-level inspection of live
  captures, but its real value is `extern/*.patch`: a **third-party bug list against our own primary
  oracle**, worth re-reading whenever the counterparts are pulled (see the tracking task).

#### Pin audit vs the ChargePoint patches (2026-07-25)

Both libcbv2g patches ChargePoint carries were checked against our pinned `03350be`; **neither
required a change on our side**, and the audit incidentally re-validated the pin:

- *`fix-iso20-loop-grammars`* — **does not apply to us.** At our pin the affected list decoders
  already use correct per-type loop-continuation states (`PriceRuleStackList` 122,
  `PriceLevelScheduleEntryList` 116, `PowerScheduleEntryList` 62, `EVPowerScheduleEntryList` 81,
  `EVPriceRuleStackList` 87) plus explicit *"LOOP breakout code for schema given maximum"* — not the
  patched-away `grammar_id = 3`. Their patch targets an older tree.
- *Pin freshness* — `iso20_CommonMessages_Decoder.c` is **byte-identical** between `03350be` and
  current `main` (md5 `ce76de568d17693b1116e86bd1e73da9`), so the pin is not stale for the file
  these bugs live in and no vector regeneration is warranted.
- *`fix-iso20-secp521-buffer-size`* (94 → 128 B) — **present at our pin, but schema-*correct* as-is.**
  `secp521_EncryptedPrivateKeyType` is `xs:base64Binary` with `<xs:length value="94"/>` — an *exact*
  length, so libcbv2g's 94-byte buffer is conformant. ChargePoint's widening is a real-world
  **leniency** patch for peers that emit non-conformant longer keys, not a spec fix; it is
  deliberately **not** adopted. Kept as an interop note for `CertificateInstallationRes` should a
  live counterpart ever send >94 bytes — at which point the answer is an explicit, documented
  tolerance decision rather than a silent buffer bump.

The general lesson worth keeping: a patch against the oracle is not automatically a bug in the
oracle — check it against the schema before treating it as one.

#### Schema sources (incl. amendments)

cbexigen fetches only the **base** editions, so amendments have to be watched separately:

- Base -20: `https://standards.iso.org/iso/15118/-20/ed-1/en/` — the eight files both we and cbexigen
  carry (`AC, ACDP, AppProtocol, CommonMessages, CommonTypes, DC, WPT, xmldsig`); our XSD set is at
  full parity with the codec oracle.
- **Amendments:** `https://standards.iso.org/iso/15118/-20/ed-1/en/Amd/` — currently `Amd/1/`
  containing `AMD1_xsdSchema.zip` (25 KB, free, no paywall) = `V2G_CI_AC_DER_IEC.xsd` +
  `V2G_CI_AC_DER_SAE.xsd`. Worth re-checking for `Amd/2+` whenever the counterparts are pulled.

### EVerest higher-layer stacks (post-monorepo, checked 2026-07-23)

EVerest consolidated its per-repo ISO 15118 modules into the **EVerest/EVerest** monorepo
(`modules/EVSE/…`). The EXI **codec** (libcbv2g/cbexigen) stays a standalone library consumed
as a dependency; the **session-level** state machines are now monorepo modules. This reshuffle
is why the old repo names are confusing — the map as it actually stands:

- **[EvseV2G](https://github.com/EVerest/EVerest/tree/main/modules/EVSE/EvseV2G)** (C,
  chargebyte, Apache-2.0) — DIN 70121 + **-2** SECC, built on libcbv2g. Full PnC + TLS, ships
  its own `tests/`. Independence value: a live C -2 counterpart, but it *shares our primary
  codec oracle* (libcbv2g), so it stresses session logic, not the codec.
- **[Evse15118D20](https://github.com/EVerest/EVerest/tree/main/modules/EVSE/Evse15118D20)**
  (C++) — the **-20** SECC. **This is what "libiso15118" became**: the standalone
  [EVerest/libiso15118](https://github.com/EVerest/libiso15118) repo is archived (read-only,
  2026-02-26), but its library lives on as `iso15118::iso15118`, linked by this monorepo module
  ("draft implementation of iso15118-20 for the EVSE side"). A *maintained, independent, non-Python*
  -20 stack — a genuine second -20 opinion alongside Josev. **Draft scope (its own feature table):**
  has DC/AC + BPT, MCS (megawatt), Scheduled + Dynamic, ExternalPayment, Pause/Resume (dynamic),
  TLS 1.2/1.3; **lacks** Plug & Charge (WIP), CertificateInstallation, Schedule Renegotiation,
  Smart Charging, WPT, ACDP, AC DER. → useful as a second opinion on the core -20 charge loop and
  dynamic mode, and the only maintained counterpart that has **MCS** (see the MCS entry under
  "Future ideas"); for PnC/cert-install/renegotiation/tariffs/WPT/ACDP, **Josev stays the only -20
  live oracle**.
- **[IsoMux](https://github.com/EVerest/EVerest/tree/main/modules/EVSE/IsoMux)** (C++) — a TCP
  **multiplexer** that sniffs the `SupportedAppProtocolReq` (via libcbv2g's `appHand` decoder) and
  routes the connection to a local `EvseV2G` (port 61341) or `Evse15118D20` (port 50000) instance;
  presents one unified `ISO15118_charger` upward. SAE J2847/2 BiDi is a pass-through setup flag to
  both backends, not a separate protocol. Mirrors our own Phase-0 sniff-and-dispatch design, and is
  itself a nice cross-reference for the SAP handshake message specifically.
- **VAS modules** —
  [Iso15118InternetVas](https://github.com/EVerest/EVerest/tree/main/modules/EVSE/Iso15118InternetVas)
  + [StaticISO15118VASProvider](https://github.com/EVerest/EVerest/tree/main/modules/EVSE/StaticISO15118VASProvider):
  maintained Value-Added-Service references (the -20 Internet Service VAS). Closest testable lead for
  the parked VDV 261 depot-VAS task — see the future-ideas section / task tracker.

> **Maintenance note:** these are moving targets. Periodically (see the "pull EVerest, re-run,
> reconcile" tracking task) `git pull` the monorepo, re-run our loopback + record-mode tests against
> the current modules, and reconcile drift — new -20 features to match, our bugs to fix, or fresh
> counterpart bugs to document.

**Test strategy:** reference encoders wrapped as dev tools, generated vectors checked in as
JSON with pinned commits (CI runs offline against the vectors; regeneration is a separate,
manually-triggered step). Plus internal property-based round-trips (CsCheck) and decoder
fuzzing (clean errors, not crashes) — which the reference oracles don't cover.

Sources: [EVerest/libcbv2g](https://github.com/EVerest/libcbv2g),
[EVerest/cbexigen](https://github.com/EVerest/cbexigen),
[EVerest monorepo modules/EVSE](https://github.com/EVerest/EVerest/tree/main/modules/EVSE)
(EvseV2G, Evse15118D20, IsoMux, Iso15118InternetVas),
[EVerest/libiso15118 (archived → Evse15118D20)](https://github.com/EVerest/libiso15118),
[SwitchEV/iso15118](https://github.com/SwitchEV/iso15118),
[EVerest/ext-switchev-iso15118](https://github.com/EVerest/ext-switchev-iso15118),
[chargebyte on cbexigen](https://chargebyte.com/artikel/bidirectional-charging-chargebyte-overcomes-exi-hurdle-with-release-of-own-open-source-software)
