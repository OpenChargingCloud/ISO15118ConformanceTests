# Draft report to EVerest — `Evse15118D20` lets the EV decide whether the EV is authenticated

Status: **draft, not sent.** Measured on the wire 2026-08-10 against everest-core **2026.02.1**
(`b61bb12b8`) built from source: two `openssl s_client` handshakes against their stock configuration —
no EV, no client PKI, nothing of ours in the first two arms — and then a two-frame replay showing what
the unauthenticated connection is good for. Post it under your own name; see *Before sending* at the
bottom.

**Two issues, and they are numbered here so they can be filed separately.** §1 is the one that matters
and can stand alone. §2 is three small omissions in the same function, and it is kept apart from §1 on
purpose: §1 has an answer a maintainer might reasonably give (*"TLS 1.2 support is for ISO 15118-2 and
this station is dual-use"*), and if the two were one issue that answer would close both.

Evidence in this repository:
[`2026-08-10-everest-d20-client-auth`](../interop-runs/2026-08-10-everest-d20-client-auth/notes.md) —
the run notes, both `openssl` captures, three charger logs and their own session log for the
unauthenticated session.

Other reports go to everest-core:
[`everest-isomux.md`](everest-isomux.md) (four findings in the multiplexer — **§2 there shares
`[V2G20-2356]` with this run**, in a different module),
[`everest-d20-ac-namespace.md`](everest-d20-ac-namespace.md) and
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md) — **also libiso15118, so the
same reviewer** —
[`everest-loop-shutdown.md`](everest-loop-shutdown.md) — **reproduced again by the control arm here**,
and it is why every arm needed a fresh station —
[`everest-evsev2g-session-log-responses.md`](everest-evsev2g-session-log-responses.md),
[`everest-evse-security-ocsp-dropped.md`](everest-evse-security-ocsp-dropped.md),
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) and one to your fork of Josev's
certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)). The framing in
`everest-loop-shutdown.md` — what everest-core has been worth to this project, and why a report from us
is not a bug filed by a stranger — applies here unchanged and is not repeated.

---

**Version:** everest-core **2026.02.1** (`b61bb12b8`), native build, Debian 13, OpenSSL 3.5.6. Module
`Evse15118D20` alone (no `IsoMux`), library `lib/everest/iso15118`, your own test PKI,
`tls_negotiation_strategy: ENFORCE_TLS`, **`enforce_tls_1_3` left at its manifest default `false`**.

---

## §1 — Whether the EV has to authenticate is decided by the EV

**Title:** `libiso15118`'s TLS server enables client-certificate verification only when the
`ClientHello` offers TLS 1.3, so an EV that offers TLS 1.2 alone is never sent a `CertificateRequest`
and gets a complete ISO 15118-20 session anonymously — `[V2G20-2400]` puts that `CertificateRequest` on
the SECC unconditionally

### What we saw

Two arms. Same station, same config, same server certificate, fresh process each. The **only**
difference is the version the client offered:

| Arm | client | your log | result |
|---|---|---|---|
| **1** (control) | `openssl s_client -tls1_3` | `Client supports TLS1.3: Change verify mode to SSL_VERIFY_PEER and SSL_VERIFY_FAIL_IF_NO_PEER_CERT` | **refused** — `tls_process_client_certificate:peer did not return a certificate` |
| **2** | `openssl s_client -tls1_2` | *(no such line)* | **`Handshake complete!`** — `TLSv1.2`, `ECDHE-ECDSA-AES128-SHA256`, no `CertificateRequest` at all |

Arm 1 is the control and it is what makes arm 2 a defect rather than a configuration: your station
**does** demand a vehicle certificate, **does** have the roots loaded to check one, and does it — when
the EV offers 1.3. The EV chooses which of the two it gets.

Neither arm needs a client certificate, a PKI, or an EV. Two `openssl s_client` calls against your stock
config reproduce all of it, which is worth putting in the issue so it can be checked in a minute.

### What it costs

Same station, same anonymous TLS 1.2 handshake, then two frames replayed byte-for-byte out of our own
ISO 15118-20 DC session corpus — so there is no question what was offered:

```
→ supportedAppProtocolReq   one entry, urn:iso:std:iso:15118:-20:DC
← 80400040                  OK_SuccessfulNegotiation
→ SessionSetupReq
← SessionSetupRes
```

and your own station's own session log for that connection:

```yaml
info: "Transition (SupportedAppProtocol -> SessionSetup)"
info: "Transition (SessionSetup -> AuthorizationSetup)"
```

```
[INFO] iso15118_charge :: Handshake complete!
[INFO] iso15118_charge :: Received session setup with evccid: EVCC01
[INFO] iso15118_charge :: New session created with session_id: 0xDA, 0x70, 0xAC, …
```

An unauthenticated peer is at `AuthorizationSetup` on an ISO 15118-20 station. The replay stops there
because the station mints its own session id and later requests have to echo it — not because anything
refused.

That answer is separately `[V2G20-2356]`: `-20` was selected out of a `SupportedAppProtocolReq` that
arrived over TLS 1.2. Same requirement as [`everest-isomux.md`](everest-isomux.md) §2, but this is a
different module, with no multiplexer in front of it and its own TLS server, so a fix there does not
reach here.

### Where it comes from

`lib/everest/iso15118/src/iso15118/io/connection_ssl.cpp`. The context default is refuse-nothing, and
the `ClientHello` callback is the only thing that ever changes it:

```cpp
// :278, in init_ssl()
SSL_CTX_set_verify(ctx, SSL_VERIFY_NONE, nullptr);
SSL_CTX_set_client_hello_cb(ctx, &client_hello_cb, nullptr);

// :132-146
int client_hello_cb(SSL* ssl, int* /* alert */, void* /* object */) {
    if (SSL_client_hello_get0_ext(ssl, TLSEXT_TYPE_supported_versions, &data, &datalen)) {
        const auto tls_1_3_found = is_tls_1_3(data, datalen);      // :91 — scans the offered list
        if (tls_1_3_found) {
            logf_info("Client supports TLS1.3: Change verify mode to SSL_VERIFY_PEER and "
                      "SSL_VERIFY_FAIL_IF_NO_PEER_CERT");
            SSL_set_verify(ssl, SSL_VERIFY_PEER | SSL_VERIFY_FAIL_IF_NO_PEER_CERT, nullptr);
        }
    }
```

The comment at `:269` says what was meant — *"Loading root certificates to verify client (only for tls
1.3)"* — and `:234-235` is what leaves the other branch reachable: with `enforce_tls_1_3` false
(`config.hpp:34`, `connection_ssl.cpp:54`, `Evse15118D20/manifest.yaml:22`) the minimum version is TLS
1.2 and `:245` enables the ISO 15118-2 cipher suite for it.

Everything downstream then behaves correctly for a connection with no peer certificate:

| | |
|---|---|
| `:486` | `if (SSL_get_verify_mode(ssl_ptr) != SSL_VERIFY_NONE and peer)` — the whole post-handshake certificate block is skipped |
| `:499` | so `vehicle_cert_hash` is never filled |
| `d20/state/session_setup.cpp:99` | `not vehicle_cert_hash.has_value()` → **always a new session**, so pause/resume on such a connection silently cannot work |

### Why we think it is worth fixing

- **`[V2G20-2400]`** — the SECC shall request the EVCC's certificate via a `CertificateRequest`
  message. It carries no version qualifier and no public/private split. NOTE 23 beside it says what it
  is for: a session in which each side verifies the other.
- **`[V2G20-1264]`** — mutual authentication with TLS 1.3 shall be supported by every V2G entity.
- **`[V2G20-2356]`** — and the SECC shall not select `-20` on a connection at TLS 1.2 or below, which
  is the second half of what arm 2 produced. `[V2G20-2359]` explicitly permits *serving* TLS 1.2 for
  backwards compatibility, so the TLS 1.2 listener is not the defect — the missing `CertificateRequest`
  on it is, and so is answering `-20` there.

We cite requirement identifiers and paraphrase what they oblige rather than quoting; the rule is
[`docs/normative-basis.md`](../normative-basis.md). These are all `-20` identifiers and carry no
document caveat.

**And because of the shape of it.** A station that never authenticates its EVs is a deployment
decision someone can take knowingly. A station that authenticates the EVs which offer TLS 1.3 and waves
through the ones that do not is not a decision anybody took — and the EV picks which it is, in the first
flight, before anything of yours has run.

### Suggested direction

More than one shape is reasonable and which belongs in your tree is yours to choose:

1. **Verify unconditionally.** `SSL_VERIFY_PEER | SSL_VERIFY_FAIL_IF_NO_PEER_CERT` at `:278`, and let
   `client_hello_cb` go back to being a log line. This is the `-20` behaviour `[V2G20-2400]` asks for
   and needs the fewest words to explain.
2. **Or refuse `-20` on a connection that is not TLS 1.3.** If the TLS 1.2 path exists so the same
   process can serve ISO 15118-2, the version has to reach the SAP handler — which today it does not:
   `d20/state/supported_app_protocol.cpp` takes the request and the custom namespace and nothing else.
   That is `[V2G20-2356]` and it also answers the anonymous-session half.
3. **Or make it configurable and default it closed** — `enforce_tls_1_3` already exists; a second
   config value that gates client authentication independently would at least make the current
   behaviour something an operator chose.
4. **Whatever you choose, drop the version test from the callback.** Deriving a security property from
   what the peer offered is the part we would flag even if the standard were silent.

---

## §2 — The `-20` TLS profile is implemented for cipher suites and stops there

**Title:** `init_ssl()` sets exactly the Table 6 cipher suites in Table 6's order, and then leaves the
`certificate_authorities` extension, the signature-algorithm list and the named-group preference at
OpenSSL's defaults — so the `CertificateRequest` names no trust anchors (`[V2G20-2401]`), invites
ML-DSA- and RSA-signed vehicle certificates (`[V2G20-1667]`), and the handshake settles on a group that
is not in Table 7 (`[V2G20-2460]`)

Kept apart from §1 because it survives §1: fix the verify mode and all three of these are still true of
every handshake the station does.

### What we saw

All of it in the arm-1 capture — the one where a `CertificateRequest` **was** sent, so this is what your
station puts in it:

```
No client certificate CA names sent
Requested Signature Algorithms: id-ml-dsa-65:id-ml-dsa-87:id-ml-dsa-44:ECDSA+SHA256:ECDSA+SHA384:
    ECDSA+SHA512:ed25519:ed448:ecdsa_brainpoolP256r1_sha256:…:RSA+SHA256:RSA+SHA384:RSA+SHA512
Negotiated TLS1.3 group: X25519MLKEM768
```

Against the profile:

| | the standard | your handshake |
|---|---|---|
| cipher suites, Table 6 | `TLS_AES_256_GCM_SHA384`, `TLS_CHACHA20_POLY1305_SHA256`, in that order | **exactly that** — `:224` |
| `certificate_authorities` | `[V2G20-2401]`/`[V2G20-2402]`: the V2G and/or OEM roots the SECC holds | absent |
| signature algorithms, Table 8 | `[V2G20-1667]`: Table 8's two entries in Table 8's order | OpenSSL's default list, 19 entries |
| named groups, Table 7 | `[V2G20-2460]`: preference from Table 7 — `secp521r1`, then `x448` | `X25519MLKEM768` |

`[V2G20-2404]` is the only exemption for an empty authority list, and it is for a SECC that holds no
roots. This one holds two: it loaded them at `:270` and `:274` and logged no complaint.

### Where it comes from

One cause for all three: nothing configures them. In the whole of `lib/everest/iso15118` there is no
`SSL_CTX_set_client_CA_list`, no `SSL_CTX_add_client_CA`, no `set1_groups_list` and no
`set1_sigalgs_list`. The roots loaded at `:270`/`:274` go into the verify store, which is a different
thing from the list advertised in the `CertificateRequest`.

The extension was clearly on the author's mind from the receiving side — `:148-151` logs
*"Extension certificate_authorities found!"* with a `TODO`, and `SSL_CTX_set_cert_cb` at `:283` is
commented out above a `handle_certificate_cb` that reads `SSL_get0_peer_CA_list`. What is missing is the
sending side.

### Why we think it is worth fixing

**The `certificate_authorities` one is the one with a cost.** `[V2G20-2401]` exists so the EV can pick
which of its contract or vehicle certificates to present. Without the list the EV is guessing, and an EV
holding certificates under more than one root has no way to guess well — the same problem the
`trusted_ca_keys` extension solves for ISO 15118-2, where `EvseV2G` disables it
([`everest-isomux.md`](everest-isomux.md) §4). Your own `TODO` at `:150` calls it *multi root support*,
which is exactly right.

**The other two are conformance rather than interop**, and we say so: OpenSSL negotiated something
perfectly sound in security terms. `[V2G20-1667]` and `[V2G20-2460]` are still `shall`s, the fix is one
call each, and a test house will look at both.

### Suggested direction

1. `SSL_CTX_set_client_CA_list(ctx, …)` built from the same two files loaded at `:270`/`:274`.
   `[V2G20-2403]` specifies which RDNs belong in each DN, and `[V2G20-2404]` says an empty list is
   correct only when there are no roots.
2. `SSL_CTX_set1_groups_list(ctx, "secp521r1:X448")` and
   `SSL_CTX_set1_sigalgs_list(ctx, "ecdsa_secp521r1_sha512:ed448")`, beside the two `set_ciphersuites`
   calls that already carry Table 6 — and if you would rather keep the defaults reachable, gate them on
   `enforce_tls_1_3` as everything else in that function is.
3. **Ask us if the tables are worth quoting into a comment.** `:224` shows the profile was consulted
   once; a comment naming Table 6, 7 and 8 beside those four calls would keep the next person from
   having to rediscover which are deliberate.

---

## Not part of this

- **Your test PKI's key material.** The SECC leaf is `prime256v1` / `ecdsa-with-SHA256`, outside Tables
  6 to 8, but that comes from `create_certs.sh` rather than from this code and is already
  [`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md).
- **The dead accept loop** after arm 1's refused handshake is
  [`everest-loop-shutdown.md`](everest-loop-shutdown.md), unchanged at 2026.02.1. Reproduced here as a
  side effect, not re-filed — but it is why each arm needed a fresh process, which is worth a line in
  the issue so nobody wastes an afternoon on it.
- **OCSP stapling**, which `libiso15118` does not implement at all — no `status_request` handling
  anywhere in the tree. Mentioned in
  [`everest-evse-security-ocsp-dropped.md`](everest-evse-security-ocsp-dropped.md) as deserving its own
  issue; it is a missing feature rather than a wrong line and we have not written it up.
- We did not test whether an anonymous session can be driven past `AuthorizationSetup`, because the
  replay could not: the station mints its own session id. An EV rather than a replay would settle it.

---

## Before sending

- [x] **Reproduce it, with a control.** Two arms, fresh station each, one variable — the TLS version
      offered. Arm 1 shows the station demanding a certificate; arm 2 shows the same station asking for
      nothing.
- [x] **Carry it past the handshake.** `supportedAppProtocolReq(-20:DC)` answered
      `OK_SuccessfulNegotiation` and `SessionSetupReq` answered with a session id, on a connection with
      no client certificate — measured, and quoted from *your* session log rather than ours.
- [x] **Check every line reference against the tree.**
      `connection_ssl.cpp:54`, `:91`, `:132-146`, `:148-151`, `:223-224`, `:234-235`, `:245`, `:249`,
      `:269-278`, `:280`, `:283`, `:486`, `:499`; `config.hpp:34`;
      `Evse15118D20/manifest.yaml:22`; `d20/state/session_setup.cpp:99` — read from the built 2026.02.1
      source on 2026-08-10.
- [ ] **Lead with the one sentence.** *Your station asks for a vehicle certificate when the EV offers
      TLS 1.3, and asks for nothing when it does not — the EV chooses.* Everything else is support.
- [ ] **Say it needs no PKI and no EV.** Two `openssl s_client` calls. That is what will get it looked
      at today rather than next month.
- [ ] **Ask whether the TLS 1.2 path is deliberate** before calling §1 an oversight — a dual-protocol
      station wanting ISO 15118-2 compatibility is a real reason for the listener. The question is the
      missing `CertificateRequest` on it, not the listener.
- [ ] **File two issues**, §1 and §2, and say in each that the other exists. §1 has an answer available
      that would wrongly close §2.
- [ ] **Mention the `IsoMux` sibling for `[V2G20-2356]`** if both are open at once.
- [ ] **Post under your own name, in your own words.**
