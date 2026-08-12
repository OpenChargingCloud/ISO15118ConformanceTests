# Issue 1 of 3 — `Evse15118D20` asks for a vehicle certificate only when the EV offers TLS 1.3

> **Post this as-is under your own name, or edit freely.** It is one of three
> ([index](README.md)); each is a different direction through the same handshake and has a different
> fix. This one has the smallest fix in the set.

**Title:** `Evse15118D20` configures `lib/everest/tls`'s ISO 15118-2 client-auth behaviour, so an EV
that offers TLS 1.2 is never sent a `CertificateRequest` — and is then served ISO 15118-20 anyway

**Version:** everest-core `main` (`ebcd36d`) built from source, and `2026.02.1` (`b61bb12b8`) before
it. `Evse15118D20` alone, no `IsoMux`. `config-sil-dc-d20.yaml` as shipped, your own test PKI,
`enforce_tls_1_3` at its manifest default `false`. Debian 13, OpenSSL 3.5.6.

## What happens

Two `openssl s_client` handshakes against your stock configuration. **No client certificate in either**,
no EV, no PKI of ours. The only difference is the TLS version offered:

| | client offers | your log | result |
|---|---|---|---|
| **A** (control) | TLS 1.3 | `Client supports TLS1.3: Change verify mode to SSL_VERIFY_PEER and SSL_VERIFY_FAIL_IF_NO_PEER_CERT` | **refused** — `tlsv13 alert certificate required` |
| **B** | TLS 1.2 | *(no such line)* | **`New, TLSv1.2, Cipher is ECDHE-ECDSA-AES128-SHA256`** — handshake complete, nothing asked |

Run on both builds, same result. On `main` the *"Change verify mode"* lines were counted rather than
eyeballed — 0 → 1 in arm A, 1 → 1 in arm B — so arm B is a negative result and not an absence of
evidence.

**Arm A is the control and it is what makes arm B a defect rather than a configuration**: your station
does demand a vehicle certificate, does have the roots loaded to check one, and does exactly that — when
the EV offers 1.3. The EV chooses which of the two it gets, in its first flight, before anything of
yours has run.

## Why the library is not the problem

`lib/everest/tls` serves ISO 15118-2 (`EvseV2G`) and ISO 15118-20 (`Evse15118D20`) from one code path,
so it cannot demand a client certificate unconditionally. It says so where it happens:

```cpp
// lib/everest/tls/src/tls.cpp:1078-1083
// 15118-2 mandates TLS 1.2 and no client certificate; 15118-20 mandates TLS 1.3 and
// requires a client certificate. The dispatcher upgrades verify mode to require a peer
// certificate for TLS 1.3 connections in handle_tls_1_3_verify_upgrade so that TLS 1.2
// connections still honor cfg.verify_client below.
int mode = cfg.verify_client ? (SSL_VERIFY_PEER | SSL_VERIFY_FAIL_IF_NO_PEER_CERT) : SSL_VERIFY_NONE;
```

**That is correct.** The problem is one library up, where a `-20`-only module opts into the `-2`
answer anyway:

```cpp
// lib/everest/iso15118/src/iso15118/io/connection_ssl.cpp:89-90
out.verify_client = false; // verify_client_on_tls13 upgrades to require a peer cert once TLS 1.3 is negotiated
out.verify_client_on_tls13 = true;
```

`Evse15118D20` has no ISO 15118-2 peers to be lenient for.

**And the assumption behind the comment is never checked downstream.** *"15118-2 mandates TLS 1.2"* is
a statement about what a conformant `-2` peer does, not a test that this peer is one. The same station
that declined to authenticate the peer *because it looked like `-2`* then answers
`supportedAppProtocolReq(-20:DC)` with `OK_SuccessfulNegotiation` and mints a session id
(measured on `2026.02.1`; not re-run on `main`, and the code path that would have to change —
`d20/state/supported_app_protocol.cpp` is not given the negotiated TLS version — is unchanged).

## What the standard asks

- **`[V2G20-2400]`** — the SECC shall request the EVCC's certificate via a `CertificateRequest`. No
  version qualifier, no public/private split. NOTE 23 beside it says what it is for: a session where
  each side verifies the other.
- **`[V2G20-1264]`** — mutual authentication with TLS 1.3 shall be supported by every V2G entity.
- **`[V2G20-2356]`** — the SECC shall not select `-20` on a connection at TLS 1.2 or below.
  **`[V2G20-2359]`** explicitly permits *serving* TLS 1.2 for backwards compatibility, so the listener
  is not the defect — the missing `CertificateRequest` on it is, and so is answering `-20` there.

Requirement identifiers and a paraphrase of what they oblige; we do not reproduce the text. All `-20`
identifiers, no revision caveat.

## Suggested fix — one line, at the call site

```cpp
// connection_ssl.cpp:89 — a -20-only station has no -2 peers to be lenient for
out.verify_client = true;
```

`verify_client_on_tls13` can stay exactly as it is; it is right for `EvseV2G`, which is what it was
written for. **Do not change the version test in the library** — that would break the `-2` module.

**It costs no conformant interop.** `[V2G20-1237]` and `[V2G20-2356]` put a conformant `-20` EV on
TLS 1.3 anyway, so the connections this newly refuses are exactly the ones `[V2G20-2356]` says the
station should not have been serving `-20` on.

Two alternatives if you would rather not change the default:

1. **Make it configurable and default it closed.** `enforce_tls_1_3` already exists in the manifest; a
   second value gating client authentication would at least make the current behaviour something an
   operator chose rather than something an EV chose.
2. **Or close the other half instead** — refuse `-20` on a connection that is not TLS 1.3. That needs
   the negotiated version to reach `d20/state/supported_app_protocol.cpp`, which today it does not.

## The other two issues

- **Issue 2** — what the `CertificateRequest` *carries* when it is sent: no `certificate_authorities`,
  OpenSSL's default signature algorithms, a named group outside Table 7.
- **Issue 3** — which chain the server *presents*: the EV's `certificate_authorities` is never read.

Fixing this one leaves both of those standing.

---

### Before you post

- [ ] Re-read the three `main` line numbers on the day — `main` moves daily.
- [ ] Decide whether to include the *"what it costs"* paragraph, which is measured on `2026.02.1`
      only. If you keep it, keep the sentence saying so.
- [ ] Two `openssl s_client` calls reproduce the whole thing. Say that early; it is what gets an issue
      looked at today rather than next month.
