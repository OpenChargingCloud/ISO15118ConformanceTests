# Roadmap & status

Last updated: **2026-07-25**. Authoritative per-phase detail lives in
[`docs/prompts/`](prompts/README.md) (the phase prompts + their status table) and the
[`README.md`](../README.md); this file is the bird's-eye plan and the "why".

## Current status

**All phases (0–5) are complete.** The solution builds cleanly and **all 622 tests are green**
(`dotnet test -c Release`: 529 in `Vanaheimr.V2G.Exi.Tests`, 85 in `Vanaheimr.V2G.Simulation.Tests`,
8 in `Vanaheimr.V2G.Experiments.Pqc.Tests`) — offline, with no C toolchain, JRE, or network beyond
loopback; the live over-the-wire Josev tests stay `[Explicit]`/script-driven.

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

| Phase | Scope | Status |
|---|---|---|
| 0 | SupportedAppProtocol wire conformance vs cbV2G (real seed vectors, pinned commit) | ✅ **done** |
| 1 | EXI primitive layer (signed int, binary, boolean, string value tables) | ✅ **done** |
| 2 | Source generator lifted to the real ISO 15118-2 schema set | ✅ **done** |
| 3 | All 17 -2 message pairs + XMLDSig over EXI fragments (ECDSA-P256/SHA-256) | ✅ **done** |
| 4 | ISO 15118-20: five codec assemblies (CommonMessages/DC/AC/WPT/ACDP) + V2GTP dispatch + XMLDSig | ✅ **done** |
| 5 | EV↔EVSE simulation (SLAC, SDP, TCP/TLS incl. mutual, state machines, Josev interop, PnC/cert-install/pause/renegotiation/tariffs live) | ✅ **done** |

**Beyond the phases** — two additions outside the original roadmap, each honest about its own limits;
detail and what stays parked in [Completed extras](#completed-extras):

| Addition | Status |
|---|---|
| **AC DER** (-20 Amendment 1) — two AC grammar variants (`AC_DER_IEC`, `AC_DER_SAE`); cross-validated against EXIficient (decode direction). **No session wiring** (payload type / `ProtocolNamespace` live in the amendment text we don't have). [`docs/ac-der.md`](ac-der.md) | ✅ codec done |
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
| ✅ [Simulation](../Vanaheimr.V2G.Simulation/) (Phase 5) | Full in-repo stack over loopback: **SLAC** pairing (real UDP match) → **SDP** discovery seam → **TLS** (two backends: .NET SslStream + BouncyCastle -20-faithful P-521/Ed448 mutual TLS) → SAP → -2/-20 AC/DC sessions to SessionStop; a full-stack SLAC→SDP→TLS→session E2E; CLI with stage/backend flags. Live vs Josev: all four -20 energy modes + both control modes, PnC both directions in both protocols, cert-install, pause/resume, renegotiation, signed tariffs |
| ✅ Test infrastructure | Vector-driven (JSON), bit-exact diff on failure; property-based round-trips (CsCheck); reference oracles pinned under `tools/` |

The original "decisive weakness" (self-encoded seed vectors that only proved internal
consistency) is **closed**: `expectedHex` is now generated by EVerest's libcbv2g at a
pinned commit, so green proves wire conformance. The `SignedInfo` fragments (-2 and -20
CommonMessages) are additionally cross-validated against EXIficient, an independent
W3C-EXI processor.

## What remains

Everything the roadmap targeted is done — see [`phase5-report.md`](phase5-report.md) for the full
scorecard. What is left over is either a **structural non-goal** (no independent counterpart exists to
validate against) or a small cleanup:

**Remaining non-goals** (would need something that doesn't exist yet):
- ⬜ **WPT / ACDP session state machines** — codecs are byte-exact vs cbV2G, but no independent stack
  implements WPT/ACDP sessions (Josev has AC/DC only), so a live run would require building state
  machines on *both* sides with no oracle for the behaviour.
- ⬜ **-2 `CertificateInstallation`/`CertificateUpdate` live** — the messages are codec-tested; a live
  run would need Josev's -2 CERTIFICATE VAS wiring on both sides (its service path is unimplemented).
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
- ⬜ **MCS (Megawatt Charging System)** — **no codec work, no new XSD** (settled 2026-07-25; an
  earlier note here wrongly called it blocked on a missing byte oracle). Three findings resolve it:
  (a) the -20 **Amendment 1 schemas are public and free** —
  `standards.iso.org/iso/15118/-20/ed-1/en/Amd/1/AMD1_xsdSchema.zip` — and contain `V2G_CI_AC_DER_IEC`
  + `V2G_CI_AC_DER_SAE`, i.e. **AC DER, not MCS**; (b) EVerest implements MCS *inside the DC message
  set* (`ServiceCategory::MCS = 8` / `MCS_BPT = 9`, an `McsConnector` enum, `McsParameterList`,
  handled in `dc_charge_parameter_discovery.cpp`; there are no `mcs_*` states); (c) our schema already
  carries it generically — `serviceIDType` is a plain `xs:unsignedShort` (not an enum) and
  `ParameterType` is a `Name` attribute plus a value choice, so MCS service IDs and parameter sets
  need no schema change. Every byte MCS puts on the wire is therefore existing DC/CommonMessages
  structure already covered by the cbV2G oracle — the ground rule is satisfied. What remains is
  **state-machine and profile work** (offer service 8/9 in the -20 catalogue, MCS connector +
  parameter sets in `ServiceDetail`, DC charge loop with MCS limits), comparable in size to the
  existing DC/AC energy-mode hooks, cross-checkable live against `Evse15118D20` — the only maintained
  counterpart that has MCS. Physical/limits detail still needs the Amendment text. Tracked as task #83.
- 🔁 **Standing: track the EVerest counterparts** (task #82) — the counterpart stacks are moving
  targets and were reshuffled into the EVerest monorepo in early 2026 (see "EVerest higher-layer
  stacks" under Reference libraries). Periodically pull libcbv2g/cbexigen, Josev/ext-switchev and the
  monorepo modules, re-run our vector + loopback + live-interop suites against the current versions,
  and reconcile the drift — new counterpart features to match, our own bugs to fix, fresh counterpart
  bugs to document (~12 Josev findings so far). Also the natural moment to revisit the pinned
  `03350be` codec commit and to decide whether to stand up an EVerest node once, which would unlock
  `EvseV2G` / `Evse15118D20` / `IsoMux` as live counterparts.

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

Three classes of oracle, with independent sources of error:

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
