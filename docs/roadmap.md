# Roadmap & status

Last updated: **2026-08-03**. Authoritative per-phase detail lives in
[`docs/prompts/`](prompts/README.md) (the phase prompts + their status table) and the
[`README.md`](../README.md); this file is the bird's-eye plan and the "why".

## Current status

**All phases (0–5) are complete.** The solution builds cleanly and **all 1140 tests are green**
(`dotnet test -c Release`, measured 2026-08-03: 911 in `WWCP_ISO15118_EXI_Tests`, 221 in
`Vanaheimr.V2G.Simulation.Tests`, 8 in `Vanaheimr.V2G.Experiments.Pqc.Tests`) — offline, with no C
toolchain, JRE, or network beyond loopback. The live over-the-wire interop tests stay
`[Explicit] [Category("Interop")]` and script-driven: eight of them now, four counterparties × two
directions, and they are *not* in that count.

ISO 15118-2 and -20 are **feature-complete at session level** and validated live against the independent
Josev stack in every direction Josev supports — the feature-gap list is **empty**:

- **Both protocols, all four -20 energy modes** (DC, AC, DC_BPT, AC_BPT), plain TCP **and** TLS
  (1.2 unilateral / 1.3 mutual), EIM **and** Plug & Charge, Scheduled **and** Dynamic control mode —
  *the last of those was one-directional until 2026-08-03*: our SECC has answered Dynamic EVs since
  2026-07-22, our EVCC could not be one. Now both, in C#
  ([the Dynamic EVCC](#-the-dynamic-evcc-2026-08-03)).
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
the first one was a reason to expect more from the second.

**There were more, and they came from two different peers.** eVDriveFlow, run on 2026-08-01, found in
its first thirteen messages that neither EVCC read a response code at all — a station could answer
`FAILED` to everything and the car would charge through it. EVerest, run on 2026-08-02, found that no
poll loop had a deadline — its station answered 1 170 authorization polls with `Ongoing`, correctly,
and our car would have kept going for ever. Both are fixed, in both protocols and all three languages;
see [Response-code handling](#-response-code-handling-2026-08-01) and
[The ongoing-poll deadline](#-the-ongoing-poll-deadline-2026-08-02).

**And then the first one paid for itself.** Later on 2026-08-02, with EVerest's charger authorized over
MQTT, our EVCC reached `CableCheck` and their station answered **`FAILED`** — the response code that
until the day before would have been read by nobody. It ended the session in one line. Two live peers:
one to find the hole, another to walk into it.

**Then the whole charge.** Driving EVerest's simulated car over MQTT as well — plug in, and CP to
state C when the station asks for its cable check — our EVCC ran a **complete ISO 15118-2 DC session
against `EvseV2G`**: 36 exchanges through PreCharge, the CurrentDemand loop, WeldingDetection and
SessionStop, every response `OK`, and the flow report's verdict on the route was *"matches the declared
flow exactly"*. The first complete charging session this project has run against a station somebody
else wrote ([`2026-08-02-everest-iso2-dc-full-charge`](interop-runs/2026-08-02-everest-iso2-dc-full-charge/notes.md)).

**And it counted for more than planned.** Every note on this counterparty said its `EvseV2G` sits on
cbV2G — the encoder our corpus is generated from — so that byte agreement with it would be agreement
with ourselves. True of `everest-core` today; **false of the image all three runs used.**
`manager:main` is everest-core **2023.10.0** and links `libopenv2g.so.1`, with no libcbv2g in it
anywhere. The codec on the other end was OpenV2G, which shares no lineage with cbexigen — so those
sessions were independent-codec results in both directions after all. A tag is not a version; the runs
are pinned by digest now, and the lesson is cheaper to learn here than in a conformance claim.

**And then -20.** On 2026-08-03, against `Evse15118D20` on **everest-core 2025.10**, our EVCC ran a
**complete ISO 15118-20 DC session**: 113 exchanges through AuthorizationSetup, ServiceDetail,
ServiceSelection, ScheduleExchange, DC_CableCheck, DC_PreCharge, the DC_ChargeLoop and
DC_WeldingDetection to SessionStop, every response `OK`, route identical to our own recorded -20
session. That sequence had never been answered by anything but our own SECC
([`2026-08-03-everest-iso20-dc-full-charge`](interop-runs/2026-08-03-everest-iso20-dc-full-charge/notes.md)).

**And in Dynamic mode.** The same station, the same day, with the EV stating energy needs and limits
instead of picking a schedule — and their power supply showing the difference: 400 V/120 A at the
setpoint our car named in Scheduled, 500 V/125 A at the operating point their station chose from our
envelope in Dynamic
([`2026-08-03-everest-iso20-dc-dynamic`](interop-runs/2026-08-03-everest-iso20-dc-dynamic/notes.md)).

**And over mutual TLS 1.3.** The same station again, this time with `ENFORCE_TLS` and
`enforce_tls_1_3`: a complete -20 DC charge on the profile's own cipher suites, our EVCC validating
their SECC chain against a supplied trust anchor and their SECC validating ours.
[`docs/pki-model.md`](pki-model.md) has pinned -20 to a mutual TLS 1.3 handshake since it was written,
and **our own tests were the only thing that had ever checked it**
([`2026-08-03-everest-iso20-dc-tls13`](interop-runs/2026-08-03-everest-iso20-dc-tls13/notes.md)).

**And through their multiplexer.** `IsoMux` in front of both backends, one TCP port, one process: an
ISO 15118-2 charge and an ISO 15118-20 charge minutes apart, routed on the namespace our car offered in
`SupportedAppProtocolReq`. The closest thing to a real charger this project has met, since a station in
the field does not know which protocol is about to arrive
([`2026-08-03-everest-isomux-dc`](interop-runs/2026-08-03-everest-isomux-dc/notes.md)). What it could not
show is a multiplexer *choosing*: our EVCC offered one protocol per session, so the case of a car that
offers both with priorities had never been exercised. Closed later the same day in loopback and in the
corpus — see [the multi-protocol offer](#what-remains) — with the live IsoMux rerun still open.

**And AC found a third one in our own EVCC.** Running their AC configuration took seven messages to be
told `FAILED_WrongEnergyTransferMode`: our -2 EVCC **hard-coded** the energy transfer mode it asked for
(`AC_three_phase_core`) and never read the `SupportedEnergyTransferMode` list the station had sent five
messages earlier. EVerest's AC SIL is single-phase and was right to refuse. Fixed the same day, with the
offer named in the refusal when nothing matches; ISO 15118-2 AC then completes, run twice. **-20 AC gets
nine phases and stops at a contactor their SIL expects its own EV module to close**
([`2026-08-03-everest-ac`](interop-runs/2026-08-03-everest-ac/notes.md)).

That is the third defect of the same shape in three days — **a value taken from our own assumption where
the protocol supplies one** — after the unread response code and the unbounded poll. All three were
invisible to every oracle here for the same reason: our own SECC supplies exactly what our own EVCC
assumes.

All four counterparties have now been run — see
[Live counterparties beyond Josev](#live-counterparties-beyond-josev) for what each proved and what it
could not.

| Phase | Scope | Status |
|---|---|---|
| 0 | SupportedAppProtocol wire conformance vs cbV2G (real seed vectors, pinned commit) | ✅ **done** |
| 1 | EXI primitive layer (signed int, binary, boolean, string value tables) | ✅ **done** |
| 2 | Source generator lifted to the real ISO 15118-2 schema set | ✅ **done** |
| 3 | All 17 -2 message pairs + XMLDSig over EXI fragments (ECDSA-P256/SHA-256) | ✅ **done** |
| 4 | ISO 15118-20: five codec assemblies (CommonMessages/DC/AC/WPT/ACDP) + V2GTP dispatch + XMLDSig | ✅ **done** |
| 5 | EV↔EVSE simulation (SLAC, SDP, TCP/TLS incl. mutual, state machines, Josev interop, PnC/cert-install/pause/renegotiation/tariffs live) | ✅ **done** |

**Beyond the phases** — three additions outside the original roadmap, each honest about its own limits;
detail and what stays parked in [Completed extras](#completed-extras), which also records the two
*corrections* that came from outside the plan entirely — the
[response-code handling](#-response-code-handling-2026-08-01) and the
[ongoing-poll deadline](#-the-ongoing-poll-deadline-2026-08-02), each found by a different live peer:

| Addition | Status |
|---|---|
| **AC DER** (-20 Amendment 1) — two AC grammar variants (`AC_DER_IEC`, `AC_DER_SAE`); cross-validated against EXIficient (decode direction). **No session wiring** (payload type / `ProtocolNamespace` live in the amendment text we don't have). [`docs/ac-der.md`](ac-der.md) | ✅ codec done |
| **MCS** (Megawatt Charging System) — `Secc20Mcs`/`Evcc20Mcs`: the DC message set under service ids 8/9 with a megawatt envelope; no codec work needed. **Ids taken from EVerest, not validated against a live MCS counterpart.** | ✅ done |
| **PQC experiments** — ML-KEM-1024 TLS session + ML-DSA-87 signatures, BC ↔ .NET cross-validated, with the EXI/CBOR/JSON size verdict. **Wire-non-conformant by design**, never a production default. [`docs/experiments/pqc.md`](experiments/pqc.md) | ✅ experiment done |

What exists today, at a glance:

| Component | State |
|---|---|
| ✅ [BitReader/BitWriter](../WWCP_ISO15118_EXI/Exi/BitReader.cs) | Bit-packed streams, MSB-first |
| ✅ [ExiPrimitives](../WWCP_ISO15118_EXI/Exi/ExiPrimitives.cs) + `ExiStringTable` | Unsigned/signed integer, binary, boolean, n-bit, string values incl. local+global value tables (decode-side; encode is miss-only, matching cbV2G) |
| ✅ [V2GTP](../WWCP_ISO15118_EXI/V2GTP/V2GTP.cs) + [Dispatch](../WWCP_ISO15118_EXI_Dispatch/V2GTPDispatcher.cs) | 8-byte transport header; payload-type → codec dispatcher over all seven sets |
| ✅ [SourceGenerator](../WWCP_ISO15118_EXI_SourceGenerator/ExiCodecGenerator.cs) | `IIncrementalGenerator`: XSD set → grammar plan → C# document + fragment codecs; fail-loud on unknown constructs; emits block-scoped namespaces |
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
validate against), a small cleanup, or a next step that one of the new counterparties has opened up:

**⬜ Get further with the three new counterparties.** All four have now been run. One of them completes
a charge; the other two stop somewhere worth naming:

- **eVDriveFlow** — their EV ends the session after `AuthorizationSetupRes`, even when offered exactly
  the one authorization service it configures. Root cause inside their state machine, not identified.
  The Dynamic-mode run sits behind that wall.
- **tux-evse** — their responder matches the *incoming request* field by field against the capture, so
  with a shipped scenario it answers the recorded Audi and no other car. Getting past SessionSetup
  means relaxing every field an EV chooses for itself.
- **EVerest** — **complete charges in both protocols**, and the only counterparty that gets that far.
  ISO 15118-2 against `EvseV2G` (36 exchanges, 2026-08-02) and ISO 15118-20 against `Evse15118D20`
  (113 exchanges, 2026-08-03), each with the route matching our own recorded session message for
  message. Both walls fell to the same idea: their module graph is addressable over MQTT, so the
  session can be authorized and the simulated car plugged in without patching a line of EVerest.
  Dynamic control mode, mutual TLS 1.3, their `IsoMux` and AC followed the same day. What is left is a
  list rather than a blocker — **AC BPT** (their `supported_iso_ac_bpt: true`, and our -20 AC BPT arm
  has never met a station) and `config-sil-dc-isomux-tls.yaml`. **-2 over TLS 1.2 is done**, as the
  carrier for the PnC run below.
  **Plug &amp; Charge, 2026-08-03:** our signed -2 `AuthorizationReq` **verified at their station** —
  the second independent stack ever to check one of our contract signatures, after Josev — using
  their own MO credential so no key was generated. Their station also taught us a station-side rule
  nothing here had had to satisfy: **-2 PnC is refused without TLS**, correctly. It stops short of a
  complete charge for a reason that is theirs: the SIL has no backend to authorize an eMAID
  (`NO_CONNECTOR_AVAILABLE`), where a deployment would have OCPP. And **-20 PnC cannot be run against
  them at all** — `auth_services.push_back(…PnC)` is commented out in `Evse15118D20` with
  *"Currently Plug&amp;Charge is not supported and ignored"*, so that item moves off this list and onto
  theirs ([`2026-08-03-everest-pnc`](interop-runs/2026-08-03-everest-pnc/notes.md)). One real wall remains: **their -20 AC expects their own EV module to
  close the contactor**, so getting past `PowerDelivery(Start)` there means driving their EV-side
  hardware simulation and not only the car's CP line. **MCS stays parked:** `config-sil-mcs.yaml` is
  not in 2025.10 either.
  One thing is worth reporting to them, and it is not any single symptom: **an error anywhere on their
  accept path ends the whole event loop while leaving the sockets bound**, so the station keeps
  accepting connections and answers nothing. Three triggers found so far — a unicast SDP request, TLS
  key logging, a refused handshake.
  One thing is worth reporting to them, and it is not any single symptom: **an error anywhere on their
  accept path ends the whole event loop while leaving the sockets bound**, so the station keeps
  accepting connections and answers nothing. Three triggers found so far — a unicast SDP request, TLS
  key logging, a refused handshake.

What each run *did* produce is in
[Live counterparties beyond Josev](#live-counterparties-beyond-josev).

**✅ Offer both protocols in one SupportedAppProtocol handshake (2026-08-03).** Our EVCC announced
exactly the one protocol it was constructed for, because the state machine was chosen before the
handshake ran. Now the offer can carry both — -20 at priority 1, -2 at priority 2 — and the state
machine is chosen **after** the handshake, from the entry the station's answered SchemaID names. In
all three languages, held by two new corpus traces (`iso2-ac-eim-sapboth`, whose station picks the
priority-2 entry, and `iso20-dc-eim-sapboth`, whose station picks priority 1 — the two answers differ
in exactly one byte, and the ports must run the machine it names) plus loopback tests either way
round, including a both-capable station following the EV's ranking.

**And it went live the same day**, against the multiplexer it was built for: two complete -20 DC
charges through EVerest's `IsoMux` from a two-entry offer, and the second one — ranking **-2** above
-20 — got routed to -20 anyway. Their selection loop never reads `Priority`: it walks the offer in
array order and returns on the first namespace starting with `-20`. Our EVCC followed their answered
SchemaID (2) into the -20 machine rather than its own ranking, which is exactly what the feature is
for; a car that had chosen its machine first would have spoken -2 at a -20 backend
([`2026-08-03-everest-isomux-both`](interop-runs/2026-08-03-everest-isomux-both/notes.md)). Their
`SupportedAppProtocolRes` for that offer is byte-identical to the one our own SECC records in the
`iso2-ac-eim-sapboth` trace.

En route it uncovered the same family's fifth member, on the station side this time: **our SECC
answered SchemaID 1 as a literal** rather than echoing the id of the entry it accepted —
indistinguishable from correct for as long as every EVCC it met assigned SchemaID 1, which ours did.
Fixed; the -2-only station accepting a two-entry offer's second entry is the recording that shows it.

Wired through: `evcc`/`secc --protocol both` in the CLI (the SECC half is a mini-IsoMux: accept both,
follow the EV's priority), and `V2G_INTEROP_PROTOCOL=both` in the interop harness — the EVerest
`IsoMux` run can now exercise the one case it could not. That live run is still open, and it is a
run, not a feature. The app-side config contract (`bridge`/`SessionConfig`) stays single-protocol on
purpose: how a phone expresses "offer both" is a B1 product decision, not a stack one.

**✅ Port parity: Dynamic, the sweep fixes and the energy-transfer-mode selection are in the Kotlin
and Swift EVCCs (2026-08-03, later the same day).** C# gained Dynamic in the morning; the ports
stayed Scheduled-only, and nothing failed, because no trace-corpus entry was a Dynamic session — the
same blind spot as everywhere else on this page, one layer along: the ports are validated against a
corpus that cannot contain the thing they are missing. Closed from both ends:

- **Two Dynamic sessions are in the corpus** (`iso20-dc-eim-dynamic`, `iso20-ac-eim-dynamic`) and
  replay byte-exactly in both ports — with the negative that replaying them *without* the flag
  diverges at ServiceSelection, so the traces cannot pass a flag that is read nowhere. The bridge
  event corpus was regenerated over them; all four back ends agree on the new events unchanged.
- **The four sweep fixes and the -2 mode selection are ported**, pinned by scripted-station tests
  (`Evcc2EnergyTransferModeTest`, `Evcc20OfferTest`, `SapHandshakeTest`, and their Swift twins): a
  deaf station whose canned offer differs from ours, because a foreign offer is the one thing the
  corpus structurally cannot contain.
- **En route, the parity pass found the ongoing-poll deadline was itself only half-ported**: Swift's
  -2 cable-check guard was constructed and never ticked, and the -2 ChargeParameterDiscovery and
  -20 DC_CableCheck poll loops were unguarded in both ports. All wired now, in exactly the places
  C# guards — the compiler's unused-variable warning was what surfaced it, three days late.

**✅ Run every session twice, in every harness** — adopted 2026-08-02, and it immediately paid for
itself twice over. `EvseV2G` in EVerest's `:main` demo image segfaults on the *second* V2G session in a
process and, because their manager kills every module when one dies, takes the whole charger with it.
Five reproductions; the fifth followed a complete, successful charge, so it is the second session as
such and not a first one that ended badly. **None of the eight interop fixtures could have found it,
because every run before this was exactly one session long.**

Then the same discipline, applied one step further, withdrew the finding: before reporting it, the
same procedure was run against everest-core **2025.10**, where two consecutive sessions both complete
and nothing crashes. The defect belongs to a 2023 image. Ten minutes of checking, and the difference
between a useful report and an embarrassing one — see
[an image tag is not a version](#-an-image-tag-is-not-a-version-2026-08-03).

The blocker was expected to be a machine — veth pairs, podman, an `everest-core` build, link-local
SDP — and it was not. After the SAP handshake an ISO 15118 session is plain TCP, so a `socat` relay in
front of each station removed the topology problem entirely, and all three ran from a Mac against
containers. Every remaining wall is inside the counterparty, and named above. The *confirm on first
contact* lists in the harness READMEs have served their purpose: they were questions, and the runs
answered them.

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
  `03350be` codec commit. **Standing up an EVerest node is done** — `EvseV2G` has been run twice
  (2026-08-02) from `ghcr.io/everest/everest-demo/manager:main`, which needs no `everest-core` build,
  so this task is now a matter of repeating an existing recipe
  ([`tools/interop-everest/`](../tools/interop-everest/README.md)). `Evse15118D20` and `IsoMux` are
  configuration changes away.

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

#### ✅ Response-code handling (2026-08-01)

Not an addition so much as a hole closed, and it belongs here because nothing in the roadmap predicted
it: **neither EVCC ever looked at a `ResponseCode`.** `Expect<T>` checked the message set and the type;
the -2 side recorded `SessionSetupCode` for the caller and acted on nothing. A station could answer
`FAILED` to every message of a session and our car would drive it to completion — which is exactly what
happened when eVDriveFlow's SECC answered `DC_CableCheckRes` with `FAILED` and we went on through
PreCharge, PowerDelivery and into the charge loop.

`RefuseOnFailure` now sits in the one place every response passes through, in **-2 and -20** and in
**C#, Kotlin and Swift**. `OK*` and `WARNING*` continue — `WARNING` is explicitly the code for
"something is off and the session goes on", and aborting on it would turn an expiring certificate into
a refused charge — and `FAILED*` ends the session with the message and the code in the error. It aborts
rather than sending SessionStop: a FAILED response is the station saying it is done.

Two shapes, because the two schemas differ. -20 has a common `V2GResponseType` base per message set, so
the check is typed. -2 has none — every `*ResType` declares its own code — so it is read by property
name, and `Evcc2FailureHandlingTests.EveryResponseTypeIsCheckable` enumerates the generated assembly to
prove every response carries one. A hand-written switch would have been *fail-open*: the one forgotten,
or the one added later, silently unchecked.

**Why no oracle here could have found it.** Every recorded response in the trace corpus was produced by
our own SECC, and our own SECC never says FAILED. The corpus is silent on this by construction, and so
is every replay built on it. It took a station that says it — which is the §1.3 argument in one
paragraph, arrived at the hard way. Full account:
[`docs/interop-runs/2026-08-01-edf-iso20-dc-notls/`](interop-runs/2026-08-01-edf-iso20-dc-notls/notes.md).

**Exercised in the field the next day, by a different peer.** On 2026-08-02 EVerest's `EvseV2G`
answered `CableCheckRes` with `FAILED`, and the session ended on that line instead of continuing into
PreCharge. Two unrelated stacks, two protocols, and both refuse at the **cable check** — it is the first
message where a station has to consult hardware, and a peer arriving over TCP has none. So it is the
natural first `FAILED` of any bench run, and the one a fixture built from our own SECC will never
contain: [`2026-08-02-everest-iso2-dc-mqtt-auth/`](interop-runs/2026-08-02-everest-iso2-dc-mqtt-auth/notes.md).

#### ✅ The ongoing-poll deadline (2026-08-02)

The sibling of the entry above, found by a different peer, and the same shape of hole: **no poll loop
in either EVCC had a deadline.** `while (… != Finished)`, no counter, no limit. EVerest's `EvseV2G`
answered 1 170 `AuthorizationReq` with `OK` and `EVSEProcessing = Ongoing` — correctly, because nothing
had authorized the session — and our car would have polled until somebody unplugged it.

What makes this one instructive is *where* the gap was: between two timeouts that each looked like it
covered the case. The per-message timeout fires when a response is **late**, and all 1 170 were fast.
The cancellation token ends the whole session, which is a stop-everything rather than a phase deadline.
Missing was the one in the middle, which ISO 15118 specifies as the EVCC's *ongoing* timeout.

`OngoingGuard` is that deadline — 60 s by default, checked once per poll in the authorization,
cable-check and charge-parameter phases of **-2 and -20**, in **C#, Kotlin and Swift**. One deliberate
difference is documented at all three sites: C# reads the session's injected `TimeProvider`, the two
ports have no clock on their `Evcc2` and use a monotonic one.

**Same reason no oracle here could have found it** as the response codes: our own SECC answers
`Finished` within a poll or two, so no recorded session contains a station that keeps saying `Ongoing`.
Writing the test made that concrete — the station had to answer the poll *outside* `Secc2`/`Secc20Dc`,
because their sequence guards reject a second `AuthorizationReq`. A station that never moves on is not
a thing either of our state machines can be. Full account:
[`docs/interop-runs/2026-08-02-everest-iso2-dc-notls/`](interop-runs/2026-08-02-everest-iso2-dc-notls/notes.md).

The rerun against the same station later that day, this time authorized, is the other half of the
evidence: seven authorization polls and 35 cable-check polls, both far inside the limit, and the guard
armed through all of them. A deadline that never fires against a station that finishes is exactly what
it should look like.

#### ✅ The sweep for the fourth (2026-08-03)

Three findings of one shape in three days made the fourth worth *looking* for rather than paying another
counterparty to trip over. Every place either EVCC puts a value into a request, checked against "did the
station already tell us this?" — and the answer written down either way, because a sweep that records
only its hits cannot be repeated. Full account, including the eleven places that were already correct:
[`docs/assumed-values-sweep.md`](assumed-values-sweep.md).

**Four found, all fixed:**

- **-2 selected `ServiceID: 1` as a literal** while `ServiceDiscoveryRes` carried the station's own
  ChargeService id. Ours is 1 and every counterparty's has been 1, which is why it survived.
- **-20 fell back to `offered[0]`** when no preferred energy-transfer service matched — for a DC car at
  an AC-only station that selects the AC service and then sends DC messages against it. Narrowed to
  *fall back within the message set you speak*, which keeps the case the fallback actually exists for:
  MCS ids ride the DC message set, so a megawatt truck at an ordinary DC charger still takes service 2.
  A first attempt removed the fallback outright and broke `Secc20McsTests`; the test was right.
- **-20 assumed EIM was on offer** after checking PnC. Now refused at AuthorizationSetup, one message
  before the station would have said FAILED, naming what was offered instead.
- **The SupportedAppProtocol response's `SchemaID` was read by nobody** — which of the offered protocols
  the station accepted. Latent while our offer is one entry, a silent protocol mismatch on the day it is
  not, and that is the day nobody would re-read this function.

**Two of the four could not have been found any other way.** The service id and the SchemaID produce no
failure against a station that behaves like ours, so neither a live run nor a test built from our own
parts would have shown them. That is the case for repeating this after the next protocol feature lands
instead of waiting to be embarrassed by a peer.

**No fifth**, and that is stated rather than left open: everything else is either the vehicle's own
description, a schema constant, or already read from the peer.

#### ✅ Reading the energy transfer mode instead of assuming it (2026-08-03)

The third hole of the same shape in three days, and the cheapest to describe: `Evcc2` **hard-coded** the
energy transfer mode it requested — `AC_three_phase_core` for AC, `DC_extended` for DC — while
`ServiceDiscoveryRes` had carried the station's `SupportedEnergyTransferMode` list five messages earlier.
EVerest's AC SIL configuration is single-phase, advertises `AC_single_phase_core`, and answered our
three-phase request with `FAILED_WrongEnergyTransferMode`. It was right.

`SelectEnergyTransferMode` now picks from what was offered, best-first inside our own power mode, and a
station offering nothing in that mode is refused with the offer named — *"the station offers no AC
energy transfer mode (offered: DC_extended)"*, which is the difference between "it refused" and "it is a
DC charger". `Evcc2EnergyTransferModeTests` pins all three cases: single-phase taken when it is all
there is, three-phase still preferred when both are offered, and the named refusal.

**Why no oracle here could have found it,** for the third time and for the same reason: our own SECC
advertises exactly the one mode our own EVCC names. A constant and a list agree until they don't, and
nothing made only of our own parts can tell them apart. Grouped with
[response-code handling](#-response-code-handling-2026-08-01) and
[the ongoing-poll deadline](#-the-ongoing-poll-deadline-2026-08-02), the three are one family: **a value
taken from our own assumption where the protocol supplies one.** The sweep that followed is
[its own entry](#-the-sweep-for-the-fourth-2026-08-03).

Full account: [`2026-08-03-everest-ac`](interop-runs/2026-08-03-everest-ac/notes.md).

#### ✅ The Dynamic EVCC (2026-08-03)

A capability that was claimed and half-present. Our SECC has answered Dynamic-mode EVs since
2026-07-22 and every recorded Dynamic session had **Josev's** car on the other side, so the mode was
live-validated in exactly one direction while this page said "Scheduled **and** Dynamic" without
qualification. `Evcc20Base` mentioned the word twice, both times as `null`.

It surfaced when the previous run's next step — "Dynamic against `Evse15118D20`, one environment
variable" — turned out to set a flag on the *station*. The variable was real, it just pointed the other
way.

`PreferDynamicControlMode` now exists on the EVCC too, and it had to reach four places, because the mode
is a property of the session and not of a message: the parameter set selected out of `ServiceDetailRes`
(ControlMode = 2), `ScheduleExchangeReq`'s control-mode arm with its **mandatory** energy triple, the
`EVPowerProfile` in `PowerDelivery(Start)` — empty in Dynamic, since there is no schedule tuple to point
at — and the charge-loop arm, DC and AC. Answering in kind is [V2G20-1600]; asking in kind is the same
rule from the other end. A flag reaching three of the four would still complete a session against a
lenient station and be wrong on the wire, so `Evcc20DynamicModeTests` asserts each on the messages the
station received, plus the negative (the default stays Scheduled end to end) and the refusal (a station
offering only Scheduled is named as such, not silently fallen back from).

Two things came with it: the tariff check now also reads
`Dynamic_SEResControlMode.AbsolutePriceSchedule`, which Dynamic mode carries instead of hanging one off
each tuple — otherwise that path would have been silently dead in this mode; and `Secc20Ac` lost a
`sealed` that `Secc20Dc` never had.

**Kotlin and Swift stay Scheduled-only**, unlike the two corrections before it, which went to all three
languages the same day. Nothing fails there — no corpus entry is a Dynamic session — so what is missing
is the capability, not a test. Named here rather than left to be found:
[port Dynamic to the two ports](#what-remains).

Live the same day, against `Evse15118D20`:
[`2026-08-03-everest-iso20-dc-dynamic/`](interop-runs/2026-08-03-everest-iso20-dc-dynamic/notes.md).

#### ✅ An image tag is not a version (2026-08-03)

Not a code change — a **methodology fix**, and it belongs here because it silently decided what three
interop runs meant. Every note on EVerest said its `EvseV2G` sits on cbV2G, the encoder our corpus is
generated from, so byte agreement with it would be agreement with ourselves. That is true of
`everest-core` HEAD. The image was `ghcr.io/everest/everest-demo/manager:main`, which is **everest-core
2023.10.0, built 2023-12-05**, links `libopenv2g.so.1`, and carries no libcbv2g at all. The tag had not
been rebuilt in three years.

Three consequences, in both directions:

- **In our favour:** those runs *were* independent-codec results. OpenV2G shares no lineage with
  chargebyte's cbexigen, so a complete DC charge against that image is 36 of our messages decoded and
  acted on by a foreign encoder, and 36 of theirs read by ours.
- **Against us:** the same image has no `Evse15118D20`, no `IsoMux` and no `config-sil-mcs.yaml` —
  targets this project had been carrying as "next", read from HEAD and never checked against the
  artifact. The -20 charger did not exist yet when that image was built.
- **A finding withdrawn:** `EvseV2G`'s second-session segfault, reproduced five times and written up as
  something to report, does not reproduce on 2025.10. Checking that took ten minutes and is the
  difference between a useful report and an embarrassing one.

**Every run write-up now records the image digest**, and "which build is this actually" is a question
the harness READMEs ask before the first byte, with the `ldd`/`release.json` commands to answer it.
Full account:
[`docs/interop-runs/2026-08-03-everest-iso20-dc-full-charge/`](interop-runs/2026-08-03-everest-iso20-dc-full-charge/notes.md).

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

*(harnesses built 2026-08-01; **all three have been run**, 2026-08-01 and 2026-08-02 — see
`docs/interop-runs/2026-08-01-edf-*`, `2026-08-01-tux-*` and `2026-08-02-everest-*`. Between them they
found two defects in our own EVCCs, both since fixed, one crash in a counterparty's charger, and none
in our codecs — which is the split the EXI-lineage column below predicts. One of them, EVerest, has
since been taken through a **complete DC charge**.)*

Josev was one live peer, and one live peer cannot show what two implementations of the *same* lineage
have agreed to be wrong about. These three extend that, and they do not extend it equally — the useful
question for each is **what a disagreement with it would prove**:

| Counterparty | Harness | EXI lineage | What a disagreement means |
|---|---|---|---|
| [tux-evse/iso15118-simulator-rs](https://github.com/tux-evse/iso15118-simulator-rs) (Rust, -2) | [`tools/interop-tux-evse/`](../tools/interop-tux-evse/README.md) | cbexigen — **ours** | never EXI, by construction: sequencing, framing or timing |
| [EDF-Lab/eVDriveFlow](https://github.com/EDF-Lab/eVDriveFlow) (Python, **-20 Ed. 1**) | [`tools/interop-evdriveflow/`](../tools/interop-evdriveflow/README.md) | **OpenEXI — independent** | either layer: the only counterparty that is an oracle for both |
| [EVerest](https://github.com/EVerest/everest-core) (C/C++/Python, DIN/-2/-20) | [`tools/interop-everest/`](../tools/interop-everest/README.md) | cbV2G — **ours** — at HEAD; **OpenV2G — independent** in the `:main` demo image we ran (car side: Josev) | as run: either layer. At HEAD: station-side sequencing/timing only. Car side little either way — `PyEvJosev` is Josev in a wrapper |

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
  *Since 2026-08-02 it is also the only counterparty we have completed a charge against.* Both walls
  fell to the same idea — their whole module graph is addressable over MQTT
  (`everest/<module_id>/<impl_id>/var` and `/cmd`, plus a bare-string external interface for the car
  simulator). [`mqtt-authorize.sh`](../tools/interop-everest/mqtt-authorize.sh) publishes their own
  token on their own topic, triggered by the HLC;
  [`sil-car.sh`](../tools/interop-everest/sil-car.sh) plugs their simulated car in and moves its CP
  line to state C when the station starts its cable check. Neither patches a line of EVerest.

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
