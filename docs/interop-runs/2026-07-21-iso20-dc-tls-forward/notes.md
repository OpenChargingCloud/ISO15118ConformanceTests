# Interop run — ISO 15118-20 DC, **live over-the-wire TLS**: our EVCC → Josev SECC

- **Date:** 2026-07-21
- **Our side:** `Vanaheimr.V2G.Simulation.Cli` (`evcc --connect … --protocol 20 --mode dc --tls-backend dotnet`),
  run under WSL's .NET 10, the **.NET `SslStream`** backend.
- **Josev:** SwitchEV/iso15118 @ `d645255`, SECC only, host-mode container on WSL2 `eth0`, `-20 DC`,
  `SECC_ENFORCE_TLS=True`.
- **Discovery:** an SDP request with **security = TLS** (`0x00`) to `ff02::1%eth0:15118`; Josev's SECC starts
  its TLS server on a fresh dynamic port and answers with `[fe80::…%eth0]:port, Security: TLS`.
- **Outcome:** ✅ a **complete** -20 DC charge session (18 exchanges, SDP → SAP → … → SessionStop) runs over
  TLS in **both** modes Josev offers:
  - **Stage 1 — TLS 1.2, unilateral** (`ENABLE_TLS_1_3=False`): server-auth only, cipher
    `ECDHE-ECDSA-AES128-SHA256`. Our SECC log tail: *"✓ Session complete in 7290 ms"*; Josev: *"Session ended
    in SessionStop"*. Josev SECC log: [`josev-secc-tls12-session.log`](josev-secc-tls12-session.log).
  - **Stage 2 — TLS 1.3, mutual** (`ENABLE_TLS_1_3=True`): Josev requires + validates our client cert against
    its OEM root; cipher `TLS_AES_256_GCM_SHA384`. *"✓ Session complete in 8271 ms"*; Josev: *"Session ended in
    SessionStop"*. Josev SECC log: [`josev-secc-tls13-mutual-session.log`](josev-secc-tls13-mutual-session.log).

## Key finding — Josev's TLS is **P-256**, not the strict -20 secp521r1 profile

Josev's `create_certs.sh` generates **`prime256v1` (P-256)** certificates for *every* role, including -20
(line 133 is literally `EC_CURVE=prime256v1  # TODO Check correct version for ISO 15118-20`), and its
`get_ssl_context` pins `set_ecdh_curve("prime256v1")`. `CertPath.__get__` also hardcodes `iso15118_2/certs/`
regardless of protocol, so a -20 session reuses the same P-256 PKI. Consequences:

- The Josev-facing TLS is our **.NET `SslStream`** backend (native P-256 / TLS 1.2+1.3 / mutual TLS). Our
  **secp521r1 + Ed448 BouncyCastle** backend — the -20-faithful TLS profile — **cannot be exercised by Josev**
  (Josev would reject its curves/sig-schemes); it stays validated in our own loopback E2E (`BcMutualTlsLoopbackTests`).
- So "live -20 TLS against Josev" = **P-256 mutual TLS 1.3**, faithful to the -20 profile *except the curve*.

## The one real bug this run caught — client-chain transmission

The Stage 2 handshake first failed (`connection closed before a full 8-byte header arrived`; Josev logged no
handshake completion). Root cause: **`SslStream` sends only the leaf** when a client cert is set via
`SslClientAuthenticationOptions.ClientCertificates`, so a peer that trusts only the **root** (Josev loads just
the OEM root) can't build leaf → Sub-CA 2 → Sub-CA 1 → root. Confirmed with `openssl s_client -cert_chain`
(handshake completed once the intermediates were sent). Fix: present the client cert via
`SslStreamCertificateContext.Create(leaf, intermediates)` so the chain is transmitted — new
`TlsOptions.ClientCertificateChain`, wired in `TcpV2GClient` and the CLI (`--client-cert <pfx>` now loads the
whole PKCS#12 collection and splits leaf from intermediates). Locked in by
`MutualTlsLoopbackTests.Iso20DcSession_OverMutualTls_ClientSendsIntermediateChainToRootOnlySecc` (a root-only
SECC that validates purely from the wire-supplied chain).

## Reproduce

1. Build the CLI (`dotnet build -c Release`).
2. Bring up Josev's SECC in host mode with `SECC_ENFORCE_TLS=True`; `ENABLE_TLS_1_3=False` for Stage 1,
   `True` for Stage 2 (a throwaway compose override sets the two env vars).
3. For Stage 2, build a client PKCS#12 from Josev's OEM leaf + key + OEM Sub-CAs (pw `12345`, from the venv
   PKI `iso15118_2/certs`) and pass it as `--client-cert oem.p12 --client-cert-pass 12345`.
4. SDP-discover with **security = TLS**, then run `evcc --connect [<addr>%eth0]:<port> --protocol 20 --mode dc
   --tls-backend dotnet [--client-cert …]`.

## Remaining

- Live -20 **Plug & Charge** over TLS (contract certs) rather than EIM.
- The reverse TLS direction (Josev EVCC → our SECC over TLS) — our SECC would present Josev's CPO chain and
  validate Josev's OEM client cert; symmetric to this run.
