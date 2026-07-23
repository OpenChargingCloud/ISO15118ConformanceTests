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

**Cross-validated between two independent FIPS 204 implementations** (`MLDsaCrossValidationTests`):
BouncyCastle 2.6.2 and .NET 10's native `System.Security.Cryptography.MLDsa`
(SYSLIB5006-experimental, OS-crypto-backed — supported on this dev machine) verify each other's
signatures over the real SignedInfo EXI fragment in **both directions**, with raw FIPS-204 key
interchange (2 592-byte public key, byte-identical re-export). The same
two-independent-implementations pattern the codec uses with cbV2G vs EXIficient — an *internal*
oracle for the PQC primitive, though still nothing 15118-external.

## The numbers — what PQC does to EXI's raison d'être

Exemplar: the -20 Plug & Charge `AuthorizationReq` (16-byte challenge + a real 3-certificate P-256
contract chain), encoded three ways: EXI (our codec), **CBOR** (System.Formats.Cbor, same structure
and field names, byte strings **raw** — the binary-clean strawman), and **compact JSON**
(System.Text.Json, byte arrays as base64). One measured run (`SizeReportTests`; cert sizes vary by
a few bytes):

| Signature | Sig bytes | EXI | CBOR | JSON | EXI vs JSON | EXI vs CBOR | Sig share of EXI |
|---|--:|--:|--:|--:|--:|--:|--:|
| unsigned | 0 | 947 | 1 119 | 1 470 | 523 B (35.6 %) | 172 B (15.4 %) | 0.0 % |
| ECDSA-P521/SHA-512 | 132 | 1 283 | 1 613 | 2 073 | 790 B (38.1 %) | 330 B (20.5 %) | 10.3 % |
| ML-DSA-87 (PQC) | 4 627 | 5 775 | 6 106 | 8 066 | 2 291 B (28.4 %) | **331 B (5.4 %)** | **80.1 %** |

What the table says:

1. **The message flips from payload-dominated to signature-dominated.** Classically the signature
   is ~10 % of the message; under ML-DSA-87 it is ~80 %. Everything EXI's schema-informed grammar
   compresses so artfully — the structure — becomes the *minority* of the bytes; the majority is
   incompressible signature randomness that no encoding can shrink.
2. **EXI's entire saving vs JSON (2.3 KB) is smaller than the signature it now carries (4.6 KB).**
   The argument "we need EXI because V2G links are slow" loses its force when a single PQC
   signature costs twice what the whole encoding choice saves.
3. **The CBOR column isolates the honest nuance.** EXI's saving vs *JSON* looks like it grows in
   the PQC row (523 B → 2 291 B) — but that is almost entirely JSON base64-inflating the 4.6-KB
   binary signature by ~33 %. Against CBOR, which keeps byte strings raw, EXI's advantage is
   **absolutely flat (~330 B, pure structural overhead) and collapses relatively: 20.5 % → 5.4 %**.
   Any binary-clean self-describing format gets within a rounding error of EXI in a PQC world —
   with schema-free tooling, greppable dumps, and no grammar generator.
4. **Certificates make it worse.** This table swaps only the signature. A full PQC migration also
   swaps the certificate chains: an ML-DSA-87 certificate carries a 2 592-byte public key plus a
   4 627-byte issuer signature — a 3-cert PnC chain goes from ~1.5 KB (P-256) to a projected
   **~23 KB**, dwarfing every encoding consideration entirely (and colliding with things like our
   8-KiB session buffers, Josev's pydantic limits, and V2GTP frame handling everywhere).

**Conclusion for the standardization debate:** in a post-quantum ISO 15118, EXI's size advantage
stops being an architectural argument. If message compactness is the goal, the crypto payload
budget — signatures, chains, KEM material — is where the bytes are; the encoding choice becomes a
measured **5.4 % vs CBOR**, and a simpler self-describing binary-clean format would buy enormous
tooling/debuggability wins at negligible wire cost. (ML-DSA-44 at 2 420 B, SLH-DSA at
~7.8–49 KB, or stateful hash-based schemes shift the constants, not the conclusion.)

## Where this lives

- `Vanaheimr.V2G.Experiments.Pqc/` — `MLDsaV2GSignature`, `PqcSizeReport` incl. the CBOR encoder
  (+ project README)
- `Vanaheimr.V2G.Experiments.Pqc.Tests/` — the eight tests (ML-KEM E2E + negative control,
  ML-DSA roundtrip + tamper, BC ↔ .NET 10 cross-validation ×3, size report)
- The single production-code touchpoint: `BcTlsOptions.ExperimentalNamedGroups` (null = exactly
  the previous behaviour), honoured by `BcV2GTlsClient`/`BcV2GTlsServer`.
