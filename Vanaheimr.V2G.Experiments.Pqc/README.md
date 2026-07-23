# Vanaheimr.V2G.Experiments.Pqc — post-quantum crypto EXPERIMENTS

**Everything in this project is deliberately wire-NON-conformant.** Both ISO 15118 editions pin
classical crypto suites (-2: ECDSA-P256/SHA-256; -20: ECDSA-secp521r1/SHA-512 or Ed448 — Ed448 is
elliptic-curve, *not* post-quantum), and no 15118 draft has committed to PQC yet. This project
exists to answer, *ahead of that standardization*, two practical questions with running code:

1. **Can the transport go post-quantum today?** — `BcTlsOptions.ExperimentalNamedGroups` lets the
   BouncyCastle TLS 1.3 backend key-exchange via **ML-KEM-1024** (FIPS 203; BC 2.6.2 exposes the
   pure-ML-KEM draft codepoints, not yet the `X25519MLKEM768` hybrid browsers use). The 15118
   session layers run over it unchanged — proven by a full loopback -20 DC session in the tests.
2. **What does a PQC signature do to 15118 message sizes?** — `MLDsaV2GSignature` signs the -20
   `SignedInfo` with **ML-DSA-87** (FIPS 204) behind an explicitly experimental URI, and the
   generated EXI codec carries the 4 627-byte signature without modification (byte-arrays are
   unbounded). Cross-validated against **.NET 10's native `MLDsa`** (a second, independent
   FIPS 204 implementation — both directions, raw key interchange). `PqcSizeReport` puts numbers
   on the consequence, with EXI vs **CBOR** (binary-clean) vs **JSON** (base64) columns: once
   signatures dominate the message, EXI's advantage over CBOR collapses to ~5 % structural
   overhead — see `docs/experiments/pqc.md`.

No 15118-external oracle exists for any of this (nothing independent signs or verifies ML-DSA
15118 messages, nothing negotiates ML-KEM V2G TLS) — the BC ↔ .NET cross-validation is an
*internal* oracle for the primitive; everything else is loopback/CI self-consistency, flagged as
such, same honesty rule as everywhere else in this repo.
