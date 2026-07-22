# Interop run — ISO 15118-20 DC **Plug & Charge, FORWARD**: our EVCC **signs**, Josev SECC **verifies** ✅

- **Date:** 2026-07-22
- **Our side:** `evcc --connect [<sdp>%eth0]:<port> --protocol 20 --mode dc --tls-backend dotnet
  --client-cert oem.p12 --contract-cert contract.p12` — mutual TLS 1.3, **signed** PnC AuthorizationReq.
- **Josev:** SECC, docker host-mode, `SECC_ENFORCE_TLS=True`, `ENABLE_TLS_1_3=True`,
  `AUTH_MODES` default (`EIM`+`PNC`), `LOG_LEVEL=DEBUG`.
- **Outcome:** ✅ Josev's own verifier accepts our signature — its SECC log shows
  `Verifying digest for element with ID 'id1' … => Match: True` and
  **`Signature verified successfully`** — and the full DC PnC session runs to `SessionStop`
  (our side: `18 exchanges … auth: pnc-signed`, `✓ Session complete in 3377 ms`, exit 0).

## Why this is the strongest signal for the EVCC-side signing bytes

Josev's `verify_signature` (shared/security.py) **re-encodes** what it received with its own EXIficient
codec and compares/verifies against that:

1. **Digest:** it re-encodes the decoded `PnC_AReqAuthorizationMode` under its CommonMessages fragment
   grammar and SHA-256-compares with our `DigestValue` → `Match: True` proves our
   `EncodeFragment_PnC_AReqAuthorizationMode` octets are byte-identical to EXIficient's — now confirmed in
   the **signing** direction too (the reverse PnC run confirmed the verify direction).
2. **Signature:** it re-encodes the decoded `SignedInfo` via `to_exi(signed_info, Namespace.XML_DSIG)` —
   the **standalone xmldsig grammar** (209-byte form) — and ECDSA-SHA256-verifies our raw `r‖s` against it.
   `Signature verified successfully` proves our `XmlDsigCodec.EncodeFragment_SignedInfo` octets are
   byte-identical to Josev's grammar output for a message **we** authored.

## What was added on our side

- `XmlDsigInteropSign` (Simulation): signs in Josev's exact `create_signature` form — Reference
  `URI="#id1"` + `Transforms`=[EXI C14N] + SHA-256 digest; SignedInfo EXI C14N + `ecdsa-sha256`,
  encoded over the **standalone xmldsig** grammar, ECDSA-P256 raw `r‖s` (64 B).
- `Evcc20Base`: `Pnc` (`PncEvccOptions`: contract leaf DER + MO sub-CAs + ECDSA key) switches
  authorization from EIM to a signed PnC `AuthorizationReq` (challenge echo + contract chain), signed
  **once** and reused across Authorization polls; `AuthorizationMode` reports `pnc-signed`.
- CLI: `evcc --contract-cert <pfx> [--contract-cert-pass <pw>]`.
- Our own SECC accepts the same form via its standalone-xmldsig verify fallback — CI:
  `Iso20LoopbackTests.DcPncSession_SignedAuthorization_VerifiesAtSecc` (full loopback TCP E2E:
  EVCC signs → SECC verifies, `SignatureGrammar == "xmldsig-standalone"`).

## Contract credentials

`contract.p12` = Josev's shipped MO PKI (venv `iso15118_2` PKI, pw `12345`): contract leaf
`CN=UKSWI123456791A, O=Switch, C=UK, DC=MO` (**P-256**) + key + MO Sub-CA2/Sub-CA1. Josev's SECC verifies
digest + signature against the leaf only (no chain/trust validation in its `verify_signature` call), and a
GenChallenge mismatch would only be a `WARN_CHALLENGE_INVALID` — ours echoed correctly.

Script: [`tools/interop-josev/live-evcc-pnc-tls.sh`](../../../tools/interop-josev/live-evcc-pnc-tls.sh).
Logs: [`our-evcc-pnc.log`](our-evcc-pnc.log), [`josev-secc-pnc.log`](josev-secc-pnc.log) (DEBUG — contains
the full decoded AuthorizationReq incl. our SignedInfo and the verification lines).

With this run, -20 Plug &amp; Charge is live-validated in **both directions**: Josev signs → we verify
(2026-07-22 `dc-pnc-tls-verified`), and we sign → Josev verifies (this run).
