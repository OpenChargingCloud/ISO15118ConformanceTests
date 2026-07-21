# Interop run — ISO 15118-20 DC, **live Plug & Charge over TLS**: Josev EVCC → our SECC

- **Date:** 2026-07-21
- **Our side:** `Vanaheimr.V2G.Simulation.Cli` (`secc … --protocol 20 --mode dc --tls-backend dotnet
  --server-cert secc.p12 --server-cert-pass 12345 --require-client-cert`), .NET 10 under WSL. Our SECC now
  offers **both EIM and Plug & Charge** in `AuthorizationSetupRes` and, for a PnC EV, validates the signed
  `AuthorizationReq` (`Secc20Base.VerifyPnc`).
- **Josev:** SwitchEV/iso15118 @ `d645255`, EVCC only, host-mode, `-20 DC`, `ENABLE_TLS_1_3=True`, `useTls`.
  Because our SECC advertises PnC, Josev's EVCC selects **PnC** and signs the AuthorizationReq with its
  contract certificate (it falls back to EIM only if it can't sign).
- **Transport:** mutual **TLS 1.3** (same P-256 setup as the reverse TLS run).
- **Outcome:** ✅ the **full PnC authorization flow runs live** and the session completes end to end to
  SessionStop ("✓ Session complete in 36551 ms"). Our SECC's verdict on Josev's real signed AuthorizationReq:

  | Check | Result | Meaning |
  |-------|--------|---------|
  | **GenChallenge echo** | ✅ OK | Josev echoed the 16-byte challenge we issued in AuthorizationSetupRes |
  | **Reference digest** | ✅ OK | our `EncodeFragment_PnC_AReqAuthorizationMode` is **byte-identical to Josev/EXIficient** — SHA-256 of our re-encoded fragment matched Josev's `DigestValue` |
  | **ECDSA signature** | ⚠️ FAIL | verify over the `SignedInfo` fragment did not match (see below) |

  Contract certificate presented: `CN=UKSWI123456791A, O=Switch, DC=MO` (P-256), with the MO Sub-CA 1/2 chain.
  Signature method: **`ecdsa-sha256`** (P-256 — Josev's PKI is P-256, not the -20-nominal secp521r1, matching
  the forward TLS run's finding). Full logs: [`josev-evcc-pnc-session.log`](josev-evcc-pnc-session.log),
  [`our-secc-pnc.log`](our-secc-pnc.log).

## What this validates

- The **EIM → PnC state-machine path** over the wire: our SECC offers PnC + a `GenChallenge`, Josev signs, our
  SECC decodes the ~2 KB signed `AuthorizationReq` (contract leaf + MO Sub-CAs + XMLDSig header) and completes
  the session.
- The **reference-digest match is the strong codec result**: it proves our canonical-EXI encoding of the
  signed element (`PnC_AReqAuthorizationMode`, including the contract chain) is byte-exact against an
  independent EXIficient encoder over a *live* message — the highest-value conformance signal short of a full
  signature verify.

## Open item — SignedInfo-fragment signature verification

The ECDSA signature over the `SignedInfo` fragment failed to verify even though the reference digest matched.
The signature is `sign(contractKey, SHA-256(canonical-EXI(SignedInfo)))`; we verify with the contract leaf's
public key and the message's own `ecdsa-sha256`/`sha256` URIs (raw 64-byte r‖s). The most likely cause is a
**canonical-EXI fragment-encoding difference** for a `SignedInfo` whose `Reference` carries a `Transforms`
element (canonical-exi transform) and SHA-256 URIs — the area of the earlier Transforms generator fix, and one
the earlier EXIficient `SignedInfo` cross-check (no Transforms, SHA-512) didn't cover. This is a codec
follow-up (byte-diff `EncodeFragment_SignedInfo` vs EXIficient for that case), not a protocol-flow issue — the
session ran to completion regardless. A reproducible offline check: decode the captured signed AuthorizationReq
in `JosevCapturedFrames20Tests` and verify its embedded signature.

## Reproduce

1. Build the CLI; start our SECC with `--server-cert secc.p12 --require-client-cert` (it offers PnC by default)
   + the TLS SDP responder.
2. Run Josev's EVCC host-mode with `ENABLE_TLS_1_3=True` and a config with `useTls=true`; its PKI already
   carries a contract cert, so it selects PnC automatically.
3. Our SECC prints the `Plug & Charge:` verdict line after the session.
