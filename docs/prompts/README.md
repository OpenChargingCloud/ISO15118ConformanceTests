# Phase prompts for the ISO 15118 EXI implementation

Ready-to-run, self-contained prompts for autonomous agent runs (Opus/Claude Code).
Each prompt is independently readable, checks its own preconditions, and defines
a Definition of Done. The phases build strictly on one another — work through
them in order. Overall plan and current status: [../roadmap.md](../roadmap.md).

| Phase | File | Content | Status |
|---|---|---|---|
| 0 | [phase0.md](phase0.md) | Replace SAP seed vectors with cbV2G reference output (wire conformance) | **done @2026-07-03** |
| 1 | [phase1.md](phase1.md) | Complete the EXI primitive layer (string value tables, signed integer, binary, boolean) | **done @2026-07-03** (value tables: miss-only encode + lenient decode, cbV2G doesn't use tables) |
| 2 | [phase2.md](phase2.md) | Lift the source generator to the real ISO 15118-2 schema set (import/choice/extension/substitutionGroup/attribute) | **done @2026-07-05** (whole set generates + compiles; SessionSetup/ServiceDiscovery byte-exact against cbV2G) |
| 3 | [phase3.md](phase3.md) | Complete ISO 15118-2 (all 17 message pairs) + XMLDSig over EXI fragments | **done @2026-07-11** (Part A: **all 17 pairs byte-exact** against cbV2G, incl. CertificateInstallation/Update; Part B: SignedInfo/Signature subtree modelled, signed message byte-exact, ECDSA-P256 sign/verify, `SignedInfo` fragment externally decoded against EXIficient — see `tools/exificient-ref/README.md`) |
| 4 | [phase4.md](phase4.md) | ISO 15118-20: multi-schema codecs (CommonMessages/DC/AC/WPT/ACDP) + V2GTP dispatch | **done @2026-07-11** (originally scoped to CommonMessages/DC/AC, WPT+ACDP added afterward — all five sets generate + compile, all target messages byte-exact against cbV2G incl. decode/roundtrip, `RationalNumber` helper, V2GTP payload-type dispatcher for all five sets with error paths, XMLDSig for CommonMessages/DC/AC — 6/6, 2/2, 2/2 fragment elements byte-exact, `V2GSignature` secp521r1/SHA-512 per set, plus Ed448 (RFC 8032) via `BouncyCastle.Cryptography`
since .NET has no built-in Ed448; per cbV2G source, WPT/ACDP have no signable elements at all. WPT surfaced two new EXI grammar constructs (generator extended, one of them with no working cbV2G reference — its own generated encoder fails outright at the schema minimum there), ACDP a document-index quirk for shared types (generator fix). CommonMessages' `SignedInfo` fragment externally decoded against EXIficient, see `tools/exificient-ref/README.md`; Josev interop stays in Phase 5) |
| 5 | [phase5.md](phase5.md) | EV↔EVSE simulation: SDP, TCP/TLS, state machines, interop against Josev | **done @2026-07-23**. In-repo stack (@2026-07-21): `Vanaheimr.V2G.Simulation` (+`.Cli`/`.Tests`), `V2GTPStream` framing, SAP, `Evcc2`/`Secc2` + `{Evcc,Secc}20{Base,Dc,Ac}`, **SLAC** pairing, **SDP** seam, **mutual TLS 1.3** (two backends: .NET `SslStream` P-256 + BouncyCastle -20-faithful secp521r1/Ed448), Vehicle cert, composed SLAC→SDP→TLS→session E2E, CLI. **Josev interop far beyond the original scope** (@2026-07-21…23): record mode byte-exact (-2 AC/DC + -20 DC PnC, 30/30 frames) **plus complete live sessions in both directions** — plain TCP + TLS, EIM + Plug & Charge (both protocols, both directions, dual-grammar signatures), all four -20 energy modes (DC/AC/DC_BPT/AC_BPT), both control modes, -20 CertificateInstallation, Pause/Resume, Renegotiation, and signed tariffs (-2 SalesTariff §7.9.2.5 incl. live verification of a real MO-Sub-CA2-signed Josev tariff; -20 AbsolutePriceSchedule). ~15 own conformance bugs fixed, ~12 Josev gaps documented. Closing report: `docs/phase5-report.md`; runs: `docs/interop-runs/` |

**Beyond the phases** — work that landed outside this plan has no prompt here; it is
documented on its own:

- **AC DER** (-20 Amendment 1): two AC grammar variants, cross-validated against EXIficient,
  no session wiring — [`../ac-der.md`](../ac-der.md).
- **MCS** (Megawatt Charging System): `{Evcc,Secc}20Mcs` — the DC message set under service
  ids 8/9 with a megawatt envelope, no codec work; ids from EVerest, no live counterpart.
- **PQC experiments** (wire-non-conformant by design) — [`../experiments/pqc.md`](../experiments/pqc.md).

All three are summarised under "Completed extras" in [`../roadmap.md`](../roadmap.md).

After finishing a phase: update the status column here (e.g. "done @<commit/date>")
and check whether the context sections of the following prompts still match the
actual repo state — they describe the expected state after the previous phase.
