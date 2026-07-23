# Post-quantum crypto experiments (Vanaheimr.V2G.Experiments.Pqc)

Date: **2026-07-23**. Status: **EXPERIMENT — wire-NON-conformant by design.** Both ISO 15118
editions pin classical suites (-2: ECDSA-P256/SHA-256; -20: ECDSA-secp521r1/SHA-512 or Ed448 —
elliptic-curve, *not* post-quantum), and no 15118 draft has committed to PQC yet. These experiments
answer two questions ahead of that standardization, with running code instead of speculation.
Nothing here is referenced by any production project; no external oracle exists for any of it
(all validation is loopback/CI self-consistency, flagged as such).

Motivation: **harvest-now-decrypt-later.** Contract provisioning ECDH-wraps a *long-lived* private
key, and vehicles live 15–20 years — the PnC PKI is a textbook PQ-migration case.

## Experiment 1 — post-quantum TLS key exchange (ML-KEM-1024)

`BcTlsOptions.ExperimentalNamedGroups` lets the BouncyCastle TLS 1.3 backend key-exchange via
**ML-KEM-1024** (FIPS 203). A complete ISO 15118-20 DC session — SAP, SessionSetup, the full DC
charge loop, SessionStop — runs over that channel unchanged
(`PqcTlsLoopbackTests.Iso20DcSession_OverMlKem1024KeyExchange_RunsToCompletion`). Both sides offer
*only* the ML-KEM group, so the completed handshake proves the PQC exchange was used; the negative
control (classical-only server vs ML-KEM-only client) fails the handshake as it must.

Honest limits: BC 2.6.2 exposes the **pure-ML-KEM draft codepoints** (0x0200–0x0202), not the
`X25519MLKEM768` *hybrid* actually deployed in browsers/OpenSSL — a pure-PQC exchange is
cryptographically stronger against a quantum adversary but is not what current internet migration
uses. Certificates stay classical P-521 in this experiment (see the chain numbers below for why
PQC certificates are their own problem).

## Experiment 2 — ML-DSA-87 message signatures through the real EXI codec

`MLDsaV2GSignature` signs the -20 `SignedInfo` with **ML-DSA-87** (FIPS 204) behind an explicitly
experimental URI (`urn:vanaheimr:v2g:experimental:xmldsig:ml-dsa-87` — no standardized XMLDSig URI
for ML-DSA existed at the time of writing). Reference digests stay SHA-512, so the whole
`V2GSignature` plumbing is reused; only the SignedInfo signing primitive changes — the same
seam Ed448 already used.

Key result: **the generated EXI codec carries the 4 627-byte signature without any change**
(base64Binary values are unbounded) — a full PnC `AuthorizationReq` with an ML-DSA-87 header
signature encodes, decodes byte-exactly, and verifies (`MLDsaSignatureTests`).

## The numbers — what PQC does to EXI's raison d'être

Exemplar: the -20 Plug & Charge `AuthorizationReq` (16-byte challenge + a real 3-certificate P-256
contract chain), as EXI (our codec) vs **compact JSON** (System.Text.Json over the same records,
byte arrays as base64). One measured run (`SizeReportTests`; cert sizes vary by a few bytes):

| Signature | Sig bytes | EXI bytes | JSON bytes | EXI saving | Sig share of EXI |
|---|--:|--:|--:|--:|--:|
| unsigned | 0 | 946 | 1 470 | 524 B (35.6 %) | 0.0 % |
| ECDSA-P521/SHA-512 | 132 | 1 282 | 2 073 | 791 B (38.2 %) | 10.3 % |
| ML-DSA-87 (PQC) | 4 627 | 5 774 | 8 066 | 2 292 B (28.4 %) | **80.1 %** |

What the table says:

1. **The message flips from payload-dominated to signature-dominated.** Classically the signature
   is ~10 % of the message; under ML-DSA-87 it is ~80 %. Everything EXI's schema-informed grammar
   compresses so artfully — the structure — becomes the *minority* of the bytes; the majority is
   incompressible signature randomness that no encoding can shrink.
2. **EXI's entire saving (2.3 KB) is smaller than the signature it now carries (4.6 KB).** The
   argument "we need EXI because V2G links are slow" loses its force when a single PQC signature
   costs twice what the whole encoding choice saves.
3. **The honest nuance:** EXI's *absolute* saving actually grows in the PQC row — from 524 B to
   2 292 B — but for an unflattering reason: JSON base64-inflates the 4.6-KB binary signature by
   ~33 %. That advantage belongs to "any binary-clean framing" (CBOR, raw fields), not to EXI's
   grammar machinery specifically. A trivial JSON+binary-attachment design would erase most of it.
4. **Certificates make it worse.** This table swaps only the signature. A full PQC migration also
   swaps the certificate chains: an ML-DSA-87 certificate carries a 2 592-byte public key plus a
   4 627-byte issuer signature — a 3-cert PnC chain goes from ~1.5 KB (P-256) to a projected
   **~23 KB**, dwarfing every encoding consideration entirely (and colliding with things like our
   8-KiB session buffers, Josev's pydantic limits, and V2GTP frame handling everywhere).

**Conclusion for the standardization debate:** in a post-quantum ISO 15118, EXI's size advantage
stops being an architectural argument. If message compactness is the goal, the crypto payload
budget — signatures, chains, KEM material — is where the bytes are; the encoding choice becomes a
rounding error, and a simpler self-describing format (JSON/CBOR) with binary-clean signature
fields would buy enormous tooling/debuggability wins at negligible wire cost. (ML-DSA-44 at
2 420 B, SLH-DSA at ~7.8–49 KB, or stateful hash-based schemes shift the constants, not the
conclusion.)

## Where this lives

- `Vanaheimr.V2G.Experiments.Pqc/` — `MLDsaV2GSignature`, `PqcSizeReport` (+ project README)
- `Vanaheimr.V2G.Experiments.Pqc.Tests/` — the five tests (ML-KEM E2E + negative control,
  ML-DSA roundtrip + tamper, size report)
- The single production-code touchpoint: `BcTlsOptions.ExperimentalNamedGroups` (null = exactly
  the previous behaviour), honoured by `BcV2GTlsClient`/`BcV2GTlsServer`.
