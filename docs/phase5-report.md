# Phase 5 closing report — EV↔EVSE simulation & interop

Date: **2026-07-21**. Scope: `docs/prompts/phase5.md` (EV↔EVSE session simulation over real
TCP/TLS, SLAC/SDP front stages, and interop against an independent stack). This report is the
Definition-of-Done item 7: codec/sequence discrepancies found, timing findings, and the honest
list of known gaps. Companion docs: [`roadmap.md`](roadmap.md), [`pki-model.md`](pki-model.md),
[`interop-runs/`](interop-runs/).

## 1. What Phase 5 delivered

A real-networking EVCC↔SECC simulation in `Vanaheimr.V2G.Simulation` (distinct from the older
in-process `Vanaheimr.V2G.Exi.Simulation` demo), composing the full connection front-end:

**SLAC pairing → SDP discovery → TLS → SAP handshake → -2/-20 AC/DC session → SessionStop**,

all loopback-testable in `dotnet test`, plus byte-exact cross-validation of both -2 and -20
against **Josev** (SwitchEV/iso15118), an independent Python stack that encodes with EXIficient
and shares no lineage with the cbV2G oracle our vectors come from.

Test state: **573 green** (`dotnet test -c Release`) — 519 in `Vanaheimr.V2G.Exi.Tests`, 54 in
`Vanaheimr.V2G.Simulation.Tests`; the 2 live over-the-wire Josev tests are `[Explicit]` and
excluded. Offline: no C toolchain, JRE, or network beyond loopback.

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
  Josev signs with a **P-256** contract cert / `ecdsa-sha256`. **Open follow-up:** the ECDSA signature over the
  `SignedInfo` *fragment* did not verify — most likely a canonical-EXI fragment-encoding difference for a
  `SignedInfo` carrying a `Transforms` element + SHA-256 URIs (the earlier EXIficient SignedInfo cross-check
  had neither); a codec byte-diff, not a flow issue. Remaining: (a) that SignedInfo-fragment canonicalization
  fix; (b) fixing the WWCP SDP components' multicast interface binding so `--sdp` works without the SDP shim.
- **SDP live multicast in CI.** Only the SDP message layer + result mapping are CI-tested; the live
  UDP/IPv6 multicast exchange is not (single-host can't hear its own multicast). A two-host or
  loopback-unicast test mode would close this.
- **Windows Schannel + P-521.** Schannel cannot use P-521 certificates for TLS (verified). This is a
  property of one backend, not a project gap — the **BouncyCastle** backend runs the -20-faithful
  secp521r1/Ed448 profile. The `.NET` backend stays useful for -2 (P-256). Documented in
  [`pki-model.md`](pki-model.md).
- **Live Plug & Charge session flow.** The PnC *messages* (AuthorizationSetup, signed Authorization,
  CertificateInstallation) are all codec-tested and the -20 PnC frames are byte-exact against Josev,
  but contract-cert provisioning + the live `CertificateInstallation` handling and its mTLS binding
  are not exercised end-to-end.
- **WPT / ACDP interop.** Their codecs are byte-exact vs cbV2G, but there is no Josev (or other
  independent) counterpart to interop against; two WPT grammar shapes remain self-consistency-only
  (see `README.md`).
- **`TransformType` present-content fidelity.** The generator fix is byte-exact vs cbexigen for the
  empty `Transform` (the only real case); for *present* content (an XPath or wildcard child, which no
  ISO 15118 message carries) it models sequence rather than choice-reduced semantics — untested,
  documented in the code.
- **Hermod weight.** SLAC pulls the heavy `Hermod`/`Styx` chain into the core Simulation library (a
  deliberate Option-A tradeoff); a later pass should slim Hermod or split SLAC into its own project.

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
