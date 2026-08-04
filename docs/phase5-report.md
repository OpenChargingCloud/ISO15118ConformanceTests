# Phase 5 closing report — EV↔EVSE simulation & interop

Date: **2026-07-21**, updated through **2026-07-25** (the follow-up-work section at the end tracks
everything after the original closing date — it has since closed the entire feature-gap list).
Scope: `docs/prompts/phase5.md` (EV↔EVSE session simulation over real TCP/TLS, SLAC/SDP front
stages, and interop against an independent stack). This report is the Definition-of-Done item 7:
codec/sequence discrepancies found, timing findings, and the honest list of known gaps.
Companion docs: [`roadmap.md`](roadmap.md), [`pki-model.md`](pki-model.md),
[`interop-runs/`](interop-runs/).

## 1. What Phase 5 delivered

A real-networking EVCC↔SECC simulation in `Vanaheimr.V2G.Simulation` (distinct from the older
in-process `ChargingSimulation` demo), composing the full connection front-end:

**SLAC pairing → SDP discovery → TLS → SAP handshake → -2/-20 AC/DC session → SessionStop**,

all loopback-testable in `dotnet test`, plus byte-exact cross-validation of both -2 and -20
against **Josev** (SwitchEV/iso15118), an independent Python stack that encodes with EXIficient
and shares no lineage with the cbV2G oracle our vectors come from.

Test state at the close of Phase 5 (2026-07-23) was **609 green**; **current: 629 green**
(`dotnet test -c Release`, 2026-07-25) — 534 in `WWCP_ISO15118_EXI_Tests`, 87 in
`Vanaheimr.V2G.Simulation.Tests`, 8 in `Vanaheimr.V2G.Experiments.Pqc.Tests`. The live
over-the-wire Josev tests are `[Explicit]`/script-driven and excluded. Offline: no C toolchain,
JRE, or network beyond loopback.

The growth since the closing date is **not** Phase 5 scope — it is the post-phase additions
tracked under "Completed extras" in [`roadmap.md`](roadmap.md) (AC DER codec variants, MCS, the PQC
experiments, and the EXIficient primitive/non-ASCII cross-checks). What *is* Phase 5 scope, and is
recorded at the end of §5, is one genuine mutual-TLS defect found afterwards.

## 2. Definition-of-Done scorecard (honest)

| # | DoD item | Status | Notes |
|---|---|---|---|
| 1 | Four in-process E2E happy paths (-2 AC/DC, -20 AC/DC) | ✅ done | `Simulation.Tests/E2E/`, each asserts terminal phase + success code |
| 2 | SDP discovery (unit tests + used in E2E) | ◑ partial | Message layer + result mapping CI-tested and wired into the full-stack E2E; the **live UDP/IPv6 multicast exchange is not** CI-tested (a single host can't hear its own multicast). Documented gap. |
| 3 | TLS variant for -2 and -20 with test certs | ✅ done | Two backends — .NET `SslStream` (P-256) and **BouncyCastle** (TLS 1.3, secp521r1 **and** Ed448, mutual). Loopback tests on both. |
| 4 | Interop documented (-2 AC EIM both directions min.) | ✅ done | **Record mode** byte-exact for -2 AC, -2 DC, -20 DC PnC. Plus a **complete live over-the-wire** -20 DC session: our EVCC ↔ Josev SECC (plain TCP) runs end to end through the full DC charge loop to SessionStop (`docs/interop-runs/2026-07-21-iso20-dc-tcp-live/`); it caught **seven** real framing/session/value bugs record mode can't (§3). The **reverse** direction (Josev EVCC → our SECC) also runs a **complete** DC session end to end to SessionStop (`docs/interop-runs/2026-07-21-iso20-dc-tcp-reverse/`), catching three more SECC bugs plus the DC poll-loop sequencing gap. **Live TLS** too, **both directions**: our EVCC → Josev SECC (TLS 1.2 unilateral + TLS 1.3 mutual) and Josev EVCC → our SECC (TLS 1.3 mutual), full DC session each (`docs/interop-runs/2026-07-21-iso20-dc-tls-{forward,reverse}/`). Plus **live Plug & Charge** over TLS (Josev signs, our SECC validates: challenge + reference digest verify; `docs/interop-runs/2026-07-21-iso20-dc-pnc-tls/`). |
| 5 | Record mode → curated vector adoption | ✅ done | Record mode works, and three -20 DC frames are curated into the vector suite as `Vectors/Iso15118_20.DC.josev.vectors.json` (`referenceEncoder = Josev/EXIficient @ d645255`), validated by decode → re-encode in `JosevCuratedVectorTests`. |
| 6 | CLI documented + architecture chapter | ✅ done | `README.md` — CLI usage (`evcc`/`secc`, tls/stage flags) + the "EV↔EVSE simulation (Phase 5)" chapter. |
| 7 | Closing report | ✅ this document | |

## 3. Codec / sequence discrepancies found

The headline value of interop against an independent stack is finding things the reference-oracle
(cbV2G) alone can't. Two findings:

- **xmldsig `Transforms` grammar (real generator bug — FIXED).** The -20 PnC session's signed
  `AuthorizationReq` carries a header `Signature`/`SignedInfo`/`Reference`/`Transforms` element
  (the `http://www.w3.org/TR/canonical-exi/` transform). Our generated decoder threw
  `invalid optional-run event code`. Root cause: the source generator's direct-`xs:choice` path
  dropped the choice's `minOccurs="0" maxOccurs="unbounded"` and emitted a **mandatory single
  choice** with no END-Element alternative, so an empty `Transform` (the only form ISO 15118 uses)
  could not decode; and `TransformsType`'s unbounded list left `ListMax=0`, so its encoder rejected
  every list. cbV2G's own vectors never emit `Transforms` inside a `Reference`, so this path was
  never validated until the Josev capture. **Fixed** by modelling an optional/repeatable direct
  choice as an EE-terminated optional run (matching cbexigen's `decode_iso20_TransformType`) and
  promoting a lone repeating child's bound to the plan level. The signed frame now round-trips
  byte-for-byte; all cbV2G vectors stay byte-exact. Full write-up:
  [`interop-runs/2026-07-21-iso20-dc-pnc-notls/`](interop-runs/2026-07-21-iso20-dc-pnc-notls/notes.md).

- **String value tables — watched, not triggered.** EXIficient *may* emit value-table hits where
  cbV2G is always miss-only; a hit our decoder mishandled would be a classic interop gap. Across all
  captured -2/-20 frames Josev emitted no hits our codec couldn't take: our decoder handles hits
  (`ExiStringTable`), our encoder is deliberately miss-only (byte-identical to cbV2G). On the SAP
  handshake specifically, **our codec ≡ cbV2G ≡ EXIficient** byte-for-byte.

No **sequencing** discrepancies were found: our -2 and -20 state machines drive the same message
order Josev does (SAP → SessionSetup → … → SessionStop, with -20's CommonMessages↔DC phase
interleave), and every captured request/response decodes and re-encodes identically.

The **live over-the-wire** run (our EVCC ↔ Josev SECC, -20 DC, plain TCP — see
`docs/interop-runs/2026-07-21-iso20-dc-tcp-live/`) exercised the layers record mode can't (the V2GTP header,
the SDP exchange, cross-stack session-state rules) and found **three more real bugs, all fixed** — each
masked in our loopback tests because our EVCC and SECC were lenient/consistent in the same wrong way:

- **V2GTP SAP payload type `0x8000` → `0x8001`.** The SupportedAppProtocol handshake shares the -2/EXI
  payload id `0x8001` (libcbv2g `V2GTP20_SAP_PAYLOAD_ID` / Josev), distinguished by session phase not payload
  type; our distinct `0x8000` was a wire-conformance bug. SAP now frames/decodes `0x8001` explicitly, and the
  payload-type dispatcher handles only post-SAP messages.
- **SAP `-20` ProtocolNamespace `…:CommonMessages` → mode-specific `…:DC`/`…:AC`** (Josev rejected the
  CommonMessages offer with `Failed_NoNegotiation`).
- **EVCC now adopts the SECC-assigned SessionID** from `SessionSetupRes` (ISO 15118-20 §7.9.2.4); it was
  sending the all-zero opener in every later request, which Josev strictly rejects.

Four further EVCC fixes then drive the session **to completion**: dynamic **service negotiation** (parse
`ServiceDiscovery`/`Detail` and select the peer's DC service/parameter set instead of hardcoded ids);
`MaximumSupportingPoints` `1` → the schema minimum `12`; a populated **`EVPowerProfile`** on
`PowerDelivery(Start)` referencing the SECC's schedule tuple; and `PowerToleranceAcceptance` set (schema-
optional but required by Josev). With all seven fixes the live session runs **end to end** — SDP → SAP →
SessionSetup → Auth → Service{Discovery,Detail,Selection} → DC_ChargeParameterDiscovery → ScheduleExchange →
DC_CableCheck → DC_PreCharge → PowerDelivery(Start) → **DC_ChargeLoop ×3** → PowerDelivery(Stop) →
DC_WeldingDetection → **SessionStop(Terminate)** (Josev: *"Session ended in SessionStop"*; `evcc`: *"✓ Session
complete, 18 exchanges"*). A full, live ISO 15118-20 DC (EIM) charge session between our independent stack
and Josev. Every one of the seven bugs was masked in loopback because our SECC is lenient exactly where Josev
validates — the whole point of interop against an independent, strictly-validating stack.

## 4. Timing findings

- The loopback E2E tests use an injected `TimeProvider` (elapsed-time checks: SECC sequence timeout,
  EVCC per-message timeout) and an `IAsyncDelay` seam (poll-loop backoff), so they run instantly and
  deterministically with no wall-clock waits — a `ManualTimeProvider`/immediate-delay double in the
  test project. Production uses `TimeProvider.System` / `Task.Delay`. No hardcoded `Task.Delay` on
  any tested path.
- Josev's own timeouts are real wall-clock. Record mode sidesteps them entirely (no live socket), so
  timing was not a factor in the cross-validation. For any future **live** over-the-wire interop, the
  per-message timeout must be kept generous (Josev + EXIficient/JRE startup adds seconds).
- SLAC/SDP loopback stages complete promptly; no timing sensitivity observed over loopback.

## 5. Known gaps (deliberate or deferred)

Nothing below blocks the happy-path simulation; each is honestly out of scope or deferred.

- **Live over-the-wire interop — both directions run a complete charge session end to end.** *Forward* (our
  EVCC ↔ Josev SECC, -20 DC, plain TCP) runs SDP through SessionStop after fixing seven bugs (§3;
  `docs/interop-runs/2026-07-21-iso20-dc-tcp-live/`). *Reverse* (Josev EVCC → our SECC) now also runs the
  **whole** session to SessionStop: SDP → SAP → service negotiation → ScheduleExchange → DC_CableCheck →
  DC_PreCharge (×4 polls) → PowerDelivery → DC_ChargeLoop (×10) → DC_WeldingDetection (×5 polls) →
  SessionStop, Josev's EVCC exiting code 0 and our SECC logging "✓ Session complete" — after fixing three
  more real SECC bugs (mode-aware ServiceID, the ControlMode parameter, a PriceLevelSchedule) + IPv6
  dual-stack listening, **and** the SECC **DC poll-loop sequencing**: `Secc20Base` self-loops
  CableCheck/PreCharge/WeldingDetection (answering each poll in place) and only advances on the next-phase
  message, via a `Secc20Dc.IsPollFor` classifier + a pre-switch advance-without-consume loop, covered by
  `Secc20DcTransitionTests` and validated in the second live reverse pass with no downstream content bugs
  (`docs/interop-runs/2026-07-21-iso20-dc-tcp-reverse/`). Both SECCs also now accept a `SessionStopReq` in
  **any** phase (answer `SessionStopRes(OK)` and end cleanly, instead of a sequence-guard error on an early
  abort) — `Secc20Base`/`Secc2`, covered by the transition tests.
- **Live TLS — both directions run to completion.** Our EVCC → Josev SECC over **TLS 1.2 unilateral** and
  **TLS 1.3 mutual** (`docs/interop-runs/2026-07-21-iso20-dc-tls-forward/`), and Josev EVCC → our SECC over
  **TLS 1.3 mutual** (`docs/interop-runs/2026-07-21-iso20-dc-tls-reverse/`) — each a complete -20 DC session to
  SessionStop. **Josev is P-256, not the strict -20 secp521r1 profile** (its `create_certs.sh` uses
  `prime256v1` for every role, `CertPath` hardcodes `iso15118_2/certs/`), so the Josev-facing TLS is the **.NET
  `SslStream`** backend; our secp521r1/Ed448 **BouncyCastle** backend — the -20-faithful TLS profile — can't be
  exercised by Josev and stays proven in loopback (`BcMutualTlsLoopbackTests`). Two real bugs found + fixed:
  (1) `SslStream` sent only the leaf certificate (client *and* server), so a root-only peer couldn't build the
  chain — now transmitted via `SslStreamCertificateContext` (`TlsOptions.Client/ServerCertificateChain`),
  locked in by a root-only-SECC loopback test; (2) `PowerDelivery(Start)` is a **poll phase** (a real EV
  repeats it with `EVProcessing=Ongoing`) — `PowerOn` now self-loops like the DC poll phases.
- **Live Plug & Charge over TLS — the flow runs.** Our SECC now offers **EIM + PnC** with a `GenChallenge`;
  a Josev EVCC with a contract cert selects PnC and signs its `AuthorizationReq`, and our SECC validates it
  (`Secc20Base.VerifyPnc`) — the session completes to SessionStop over mutual TLS 1.3
  (`docs/interop-runs/2026-07-21-iso20-dc-pnc-tls/`). The **GenChallenge echo and reference digest verify**;
  the digest match is the strong result — it proves our canonical-EXI encoding of the signed element
  (`PnC_AReqAuthorizationMode`, incl. the contract chain) is **byte-exact vs EXIficient over a live message**.
  Josev signs with a **P-256** contract cert / `ecdsa-sha256`. **Signature verification — root-caused and
  reproduced, not a codec bug:** the ECDSA signature over `SignedInfo` did not verify against our fragment, and
  the "our canonicalization is wrong" hypothesis is **refuted**. Our codec is byte-exact for the signed
  *element* (the reference digest matches Josev's `DigestValue` byte-for-byte). The divergence is the
  **`SignedInfo` grammar**: decompiling Josev's `EXICodec.jar` shows `to_exi(signed_info, Namespace.XML_DSIG)`
  maps to `BuiltInSchema.XSDCore` → `XMLDSIG_Core_Schema_Grammar`, a grammar built from
  **`xmldsig-core-schema.xsd` standalone**, whereas we (like cbV2G, our reference) encode `SignedInfo` as a
  fragment of the full `V2G_CI_CommonMessages` schema set. The EXI *Fragment* top-level element event-code width
  tracks the schema's global-element count, so Josev's form is **209 B** (one-bit-narrower code, whole stream
  shifted) vs our/cbV2G **210 B**, though both decode identically. Josev's own codec (in the `iso15118-secc`
  container) reproduces the 209 bytes exactly and Josev's captured signature verifies against them — checked in
  as `JosevPnCSignatureDiag.JosevStandaloneXmldsigSignedInfoHex` (runs in CI, no Java). **Now closed:** a
  verify-only interop path re-encodes `SignedInfo` under the standalone-xmldsig grammar
  (`WWCP_ISO15118_XMLDSig` — our own generator reproduces Josev's 209 bytes byte-for-byte —
  + `XmlDsigInteropVerify`), and `Secc20Base.VerifyPnc` falls back to it. **Verified live** on 2026-07-22
  (Josev EVCC → our SECC, mutual TLS 1.3): `challenge OK, digest OK, signature OK … grammar=xmldsig-standalone`,
  full DC loop to `SessionStop` (`docs/interop-runs/2026-07-22-iso20-dc-pnc-tls-verified/`). Our own signing
  stays cbV2G-byte-exact (we never sign that form).
- **Live SDP discovery without the shim — now closed.** `secc --sdp --interface <nic>` drives a real Josev
  EVCC end to end (`docs/interop-runs/2026-07-22-iso20-dc-sdp-noshim/`). The multicast interface binding was
  never actually broken — the WWCP `SECC_SDPServer` binds `[::]:15118`, joins `FF02::1`, and answers correctly.
  The earlier shim worked around a *policy* default: `SECC_SDPServerOptions.RejectNoTlsRequests` is `true`
  (TLS-deployment-oriented), so a **plaintext** SECC silently dropped a plaintext EVCC's `SDP_Request`. Fix is
  in the CLI (not the submodule): `Program.BuildSeccSdpOptions` sets `RejectNoTlsRequests = !noTls`, guarded by
  `SeccSdpOptionsTests`; also fixed a cosmetic `%scope%scope` doubling in the advertise log.
- **SDP live multicast in CI.** Only the SDP message layer + result mapping are CI-tested; the live
  UDP/IPv6 multicast exchange is not (single-host can't hear its own multicast). A two-host or
  loopback-unicast test mode would close this.
- **Windows Schannel + P-521.** Schannel cannot use P-521 certificates for TLS (verified). This is a
  property of one backend, not a project gap — the **BouncyCastle** backend runs the -20-faithful
  secp521r1/Ed448 profile. The `.NET` backend stays useful for -2 (P-256). Documented in
  [`pki-model.md`](pki-model.md).
- **Pause/Resume — closed** (was a declared non-goal, now implemented): `SessionStopReq(Pause)` + rejoin
  via the old session id (`OK_OldSessionJoined`), both protocols, loopback E2Es + live forward vs Josev
  (`2026-07-22-pause-resume`). The -2 flow round-trips fully; Josev's -20 side preserves an empty session
  context and degrades to a graceful new session — documented as its gap.
- **Renegotiation — closed** (the last item of the gap list): -2 [V2G2-841] SECC-triggered **and**
  EV-initiated, both live-complete against Josev in both directions; -20 ServiceRenegotiation
  [V2G20-1477] implemented with session re-entry at ServiceDiscovery — live to the point where Josev's
  own EVCC drops the link (three documented Josev gaps: empty -20 pause context, DC stop path hardcoding
  Terminate, and the stop notification beating its own ServiceDiscovery re-entry); the full cycle is
  CI-guarded (`2026-07-22-renegotiation`).
- **Smart charging / signed tariffs — closed** (was the last declared non-goal): -2 SalesTariff §7.9.2.5
  — the SECC offers a two-tuple SAScheduleList with both SalesTariffs digitally signed into ONE header
  signature, and validates the EV's `PowerDeliveryReq(Start)` (`FAILED_TariffSelectionInvalid` /
  `FAILED_ChargingProfileInvalid` [V2G2-761]); the EVCC verifies the tariff signature (dual-grammar),
  picks the cheapest tuple by average EPriceLevel, and shapes its ChargingProfile to that tuple's
  PMaxSchedule. -20: the Scheduled-mode ScheduleExchangeRes carries a rich **signed
  `AbsolutePriceSchedule`** (power-banded EUR/kWh PriceRuleStacks, ECDSA-P521/SHA-512) instead of the
  flat PriceLevelSchedule. Live (`2026-07-22-tariff`, three runs): a Josev EVCC consumed our signed
  two-tuple offer, **chose the cheap tuple** and sent a PMax-shaped profile our validation accepted;
  a Josev AC EVCC consumed the signed -20 AbsolutePriceSchedule; and — the surprise — **our EVCC
  live-verified a real MO-Sub-CA2-signed Josev SalesTariff** (`digests OK, ECDSA OK,
  grammar=xmldsig-standalone`), giving the -2 verify path a genuine external oracle. Honest residue:
  our -2 combined-grammar signing form and the -20 price-schedule signature have **no external
  verifier** (Josev's EVCC tariff check is a code TODO; nothing external touches -20 price-schedule
  signatures) — both are CI-guarded only. One more Josev gap found: its pydantic `Reference` model
  requires the schema-optional `Transforms` in -2 as well (empty-messaged `V2GMessageValidationError`
  in ChargeParameterDiscovery without it) — our tariff references include `Transforms=[EXI C14N]`.
- **ISO 15118-2 Plug & Charge session flow — closed** (was the last big codec-tested-only block): live in
  both directions over TLS (`2026-07-22-iso2-pnc-tls`) — PaymentDetails, the signed AuthorizationReq and
  the signed MeteringReceiptReq all verify, ours at Josev and Josev's at ours (dual-grammar; Josev's -2
  signatures use the same standalone-xmldsig form as -20). Three live conformance findings fixed:
  mandatory SAScheduleList [V2G2-905], receipt-once (Josev loops on receipt-every-cycle), -2 SAP version
  2.0. What remains for -2 PnC: `CertificateInstallation`/`CertificateUpdate` live (codec-tested only;
  Josev's -2 cert-install service path would need its CERTIFICATE VAS wiring on both sides).
- **Live Plug & Charge session flow (-20) — closed.** The signed-Authorization half runs in **both directions**:
  Josev signs → our SECC verifies (`2026-07-22-iso20-dc-pnc-tls-verified`), and our EVCC signs → Josev's
  SECC verifies (`2026-07-22-iso20-dc-pnc-forward-signed`: Josev logs `=> Match: True` +
  `Signature verified successfully`; `evcc --contract-cert`, Josev-form signing via `XmlDsigInteropSign`).
  Contract **provisioning** (`CertificateInstallation`) is now also live to the maximum an independent stack
  allows (`2026-07-22-iso20-certinstall-sdp`): our SECC verifies Josev's real signed req and issues a
  signed res Josev fully validates before hitting its own `NotImplementedError` (Josev implements
  cert-install on neither side); the full roundtrip incl. working-key unwrap runs in-repo. Remaining honest
  gap: the provisioning *crypto octets* (ECDH/KDF/AES-GCM wrap in `ContractProvisioning`) have **no external
  oracle** — schema-valid and round-trip-tested, but self-consistent only, unlike every wire message.
- **WPT / ACDP interop — not runnable.** Their codecs are byte-exact vs cbV2G (record mode), but a live run
  is impossible: Josev — the only independent -20 stack available — implements **no WPT/ACDP session state
  machines** (only AC/DC; confirmed 2026-07-22, `iso15118_20_states.py` has AC/DC states only and ships only
  `evcc_config_{ac,dc}[_bpt].json`), and our own WPT/ACDP projects are codec-only by the same token. A live
  run would need full session state machines built on **both** sides. Two WPT grammar shapes also remain
  self-consistency-only (see `README.md`). **DC, AC, and their bidirectional (BPT/V2G) variants, by contrast,
  all run live** against Josev — full **-20 AC** (plain + TLS), **DC_BPT**, and **AC_BPT** sessions completed to
  SessionStop on 2026-07-22 (`docs/interop-runs/2026-07-22-iso20-{ac-eim,ac-tls,dc-bpt,ac-bpt}-sdp/`). BPT
  support: the SECC advertises both the unidirectional and BPT energy-transfer services and replies with the
  matching charge-and-discharge mode/control-mode variant (guarded by
  `Secc20DcTransitionTests.DcBptSession_*` / `Secc20AcBptTests`). **Both control modes** also run live:
  the SECC offers Scheduled *and* Dynamic parameter sets ([V2G20-2656]) and answers ScheduleExchange and
  the charge loop strictly in kind for all four control-mode variants — before this fix a Dynamic-mode EV
  got Scheduled res types, a wire-type mismatch that never fired because all earlier runs were Scheduled.
  Live Dynamic sessions (DC, DC_BPT, AC_BPT via `secc --dynamic`) completed to SessionStop on 2026-07-22
  (`docs/interop-runs/2026-07-22-iso20-dynamic-sdp/`; guarded by `Secc20DynamicModeTests`).
- **`TransformType` present-content fidelity.** The generator fix is byte-exact vs cbexigen for the
  empty `Transform` (the only real case); for *present* content (an XPath or wildcard child, which no
  ISO 15118 message carries) it models sequence rather than choice-reduced semantics — untested,
  documented in the code.
- **Hermod weight.** SLAC pulls the heavy `Hermod`/`Styx` chain into the core Simulation library (a
  deliberate Option-A tradeoff); a later pass should slim Hermod or split SLAC into its own project.

### Closed after the closing date

- ✅ **Mutual-TLS client-certificate context** (2026-07-25, commit `76bc251`). The mutual-TLS tests
  drifted from green to consistently failing with
  `CryptographicException: An unknown chain building error occurred` out of
  `SslStreamCertificateContext.Create`. Two independent causes, both real:
  1. **A production defect.** `TcpV2GClient` always wrapped the client certificate in an
     `SslStreamCertificateContext`, even with no chain to send. `Create` builds a chain over the leaf
     against the **platform** trust store — there is no custom-trust hook — so it fails for any
     certificate whose issuer the machine does not know. That is not merely a test artefact: a real EV
     whose OEM root is not installed locally hits exactly this. The context is now used only when the
     caller actually has intermediates to transmit; otherwise the leaf goes out via
     `ClientCertificates` and path building is left to the peer, which is what the -2/-20 trust model
     expects anyway.
  2. **A test-PKI defect.** Every run minted a fresh root with the *identical* subject
     `CN=V2G Root CA, …`. Windows' certificate cache indexes by name, so a later run's chain build
     picked up an earlier run's root — matching name, non-matching key — and reported
     `NotSignatureValid`. Hence the drift: green while the cache was empty, red once it filled.
     `V2GHierarchy.Build`'s `CommonNameSuffix` now makes every hierarchy uniquely named.

  Method worth remembering: the answer only appeared after dumping `X509Chain.ChainStatus` with
  `throwOnException: false` — against the PKI variant the tests actually use. Two plausible-sounding
  hypotheses (a throwing validation callback; AIA/OCSP fetching, "fixed" with `offline: true`) were
  both wrong and were reverted. Verified with eight consecutive full runs; the flake never reproduced
  under `--filter`.

## 6. Security caveats

- All certificates and keys used are **self-signed / dev test material generated at test-time or into
  a `--pki-dir`**; none are checked in, and none are production-ready. The CLI's `--tls-backend bc`
  path generates a throwaway strict-20 dev PKI. Do not present any of this as production PKI.
- The `--tls` / interop dev paths accept any server cert (EVCC side) for testing convenience; that is
  a dev-only relaxation, not the -20 mutual-TLS trust model (which `pki-model.md` describes).

## 7. Bottom line

Phase 5's in-repo stack is complete and green, and interop against an independent stack is byte-exact
for -2 (AC/DC) and -20 DC Plug & Charge — including a real signed message, which is what forced out
(and fixed) the one genuine codec bug of the phase. The remaining items (§5) are external-stack or
wrap-up work, not correctness gaps in the codec or the simulation.
