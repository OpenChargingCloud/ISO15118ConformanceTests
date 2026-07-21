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

Test state: **561 green** (`dotnet test -c Release`) — 516 in `Vanaheimr.V2G.Exi.Tests`, 45 in
`Vanaheimr.V2G.Simulation.Tests`; the 2 live over-the-wire Josev tests are `[Explicit]` and
excluded. Offline: no C toolchain, JRE, or network beyond loopback.

## 2. Definition-of-Done scorecard (honest)

| # | DoD item | Status | Notes |
|---|---|---|---|
| 1 | Four in-process E2E happy paths (-2 AC/DC, -20 AC/DC) | ✅ done | `Simulation.Tests/E2E/`, each asserts terminal phase + success code |
| 2 | SDP discovery (unit tests + used in E2E) | ◑ partial | Message layer + result mapping CI-tested and wired into the full-stack E2E; the **live UDP/IPv6 multicast exchange is not** CI-tested (a single host can't hear its own multicast). Documented gap. |
| 3 | TLS variant for -2 and -20 with test certs | ✅ done | Two backends — .NET `SslStream` (P-256) and **BouncyCastle** (TLS 1.3, secp521r1 **and** Ed448, mutual). Loopback tests on both. |
| 4 | Interop documented (-2 AC EIM both directions min.) | ◑ adjusted | Cross-validated in **record mode** (capture Josev's EXI, re-encode with our codec byte-for-byte) rather than live over-the-wire — same conformance signal, no L2 bridging. -2 AC, -2 DC, and -20 DC PnC all byte-exact; artifacts checked in. Live over-the-wire both-directions is deferred (item in §5). |
| 5 | Record mode → curated vector adoption | ◑ partial | Record mode works and its frames are baked into CI tests (`JosevCapturedFrames{,Dc,20}Tests`). Formal curation into the regular `Tests/Vectors/` files is **not yet done**. |
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

- **Live over-the-wire interop (both directions).** Cross-validation used record mode. A true
  live run (our EVCC ↔ Josev SECC and vice-versa) needs both stacks on one L2 network (SDP/IPv6),
  and for -20 the BouncyCastle TLS backend against Josev's TLS config. The `[Explicit]`
  `JosevInteropTests` hook + `tools/interop-josev/run-our-*.sh` wrappers are in place for it.
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
- **Record-mode vector curation.** Captured Josev frames are baked into CI tests but not yet promoted
  into the regular `Tests/Vectors/` files with a `source: "josev@<sha>"` provenance.
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
