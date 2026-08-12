# Draft report to EVerest — a `-20`-only station inherits ISO 15118-2's client-auth leniency, so the EV decides whether the EV is authenticated

Status: **draft, not sent.** Measured on the wire 2026-08-10 against everest-core **2026.02.1**
(`b61bb12b8`) built from source: two `openssl s_client` handshakes against their stock configuration —
no EV, no client PKI, nothing of ours in the first two arms — and then a two-frame replay showing what
the unauthenticated connection is good for. Post it under your own name; see *Before sending* at the
bottom.

> **⚠ Re-argued 2026-08-11 against `main`, and §1 got smaller and sharper.** Checked against
> everest-core `main` (`ebcd36d`)
> ([audit notes](../interop-runs/2026-08-11-reports-upstream-audit/notes.md)):
>
> - **The mechanism moved one library down and is otherwise identical.** The `supported_versions` scan
>   and the upgrade to `SSL_VERIFY_PEER | SSL_VERIFY_FAIL_IF_NO_PEER_CERT` now live in
>   `Server::handle_tls_1_3_verify_upgrade` (`lib/everest/tls/src/tls.cpp:997-1019`), same log line,
>   behind a flag the caller sets. **Every `connection_ssl.cpp:NNN` citation in §1 below locates the
>   2026.02.1 code and nothing on `main`** — the anchors have been updated in *Where it comes from*.
> - **§1's open question — "is the TLS 1.2 path deliberate?" — is answered, and the answer moves the
>   defect.** It is deliberate, it is documented, and **it is correct where it is written**:
>   `lib/everest/tls` serves ISO 15118-2 (`EvseV2G`) and ISO 15118-20 (`Evse15118D20`) from the same
>   code, and a dual-protocol library must not demand a client certificate on a `-2` connection. What
>   is wrong is that a `-20`-**only** module opts into that leniency. **The fix is one line at the call
>   site, not a change to the library** — which is a far easier thing to agree to than what this report
>   asked for before.
> - **§2 stands unchanged, at the new location.** `lib/everest/tls/src/tls.cpp` has no client-CA-list
>   call of any kind, no `SSL_CTX_set1_sigalgs*` and no `set1_groups`/`set1_curves` on the server path,
>   so `[V2G20-2401]`, `[V2G20-1667]` and `[V2G20-2460]` are as absent there as they were.
> - The standalone `EVerest/libiso15118` still has the whole thing in `connection_ssl.cpp`, which is
>   the only reason the old citations still resolve anywhere — **that repository is not maintained**.
>
> **All three sections are measured on `main`**, all on 2026-08-12:
> §1's [two handshake arms](../interop-runs/2026-08-12-everest-main-client-auth/notes.md),
> §2's [CertificateRequest, byte for byte](../interop-runs/2026-08-12-everest-main-tls-profile/notes.md),
> §3's [three chain-selection arms](../interop-runs/2026-08-12-everest-main-chain-selection/notes.md).
> **One exception, stated where it belongs**: §1's *What it costs* — the two-frame replay showing an
> anonymous peer reaching `AuthorizationSetup` — still rests on the `2026.02.1` measurement and was not
> re-run, because the request frames were never kept as bytes.

> **The three postable issues are in [`everest-d20-client-auth/`](everest-d20-client-auth/README.md).**
> This file is the *account* — how each finding was reached, on which build, what was ruled out and what
> we got wrong on the way. The three files in that directory are what goes to the maintainers: shorter,
> self-contained, each naming the other two. Suggested order and the reason for it are in its README.

**Three issues, and they are numbered here so they can be filed separately.** §1 is the one that matters
and can stand alone. §2 is three small omissions in the same function, and it is kept apart from §1 on
purpose: §1 has an answer a maintainer might reasonably give (*"TLS 1.2 support is for ISO 15118-2 and
this station is dual-use"*), and if the two were one issue that answer would close both.

Evidence in this repository:
[`2026-08-10-everest-d20-client-auth`](../interop-runs/2026-08-10-everest-d20-client-auth/notes.md) —
the run notes, both `openssl` captures, three charger logs and their own session log for the
unauthenticated session.

Other reports go to everest-core:
[`everest-d20-trust-anchor.md`](everest-d20-trust-anchor.md) — **the same function**, and the natural
sequel: this report is about *whether* the station asks for a certificate and what it names in the
`CertificateRequest`, that one about *which root it checks the answer against* (it loads the MO root,
so it refuses vehicle certificates and accepts contract ones) —
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
difference is the version the client offered. **Run on `2026.02.1` (2026-08-10) and again on `main`
(2026-08-12) with the same result** — the table below is the release; the `main` re-run is in its
[own notes](../interop-runs/2026-08-12-everest-main-client-auth/notes.md) and differs only in that the
*"Change verify mode"* lines were **counted** (0 → 1 in arm 1, 1 → 1 in arm 2), which is what turns
arm 2's silence into a negative result rather than an absence of evidence:

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

**Measured on `2026.02.1`, not re-run on `main`** — the rest of §1 was, and the code path this depends
on is unchanged, but we are not going to blur those. Same station, same anonymous TLS 1.2 handshake,
then two frames replayed byte-for-byte out of our own ISO 15118-20 DC session corpus — so there is no
question what was offered:

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

Two files on `main`, and they are not equally at fault.

**The library is right.** `lib/everest/tls` terminates TLS for `EvseV2G` (ISO 15118-2) *and* for
`Evse15118D20` (ISO 15118-20), so it cannot demand a client certificate unconditionally — a `-2`
connection must not get a `CertificateRequest`. It expresses that as a flag the caller sets, with the
reason written out:

```cpp
// lib/everest/tls/src/tls.cpp:1076-1083
m_verify_client_on_tls13 = cfg.verify_client_on_tls13;

// 15118-2 mandates TLS 1.2 and no client certificate; 15118-20 mandates TLS 1.3 and
// requires a client certificate. The dispatcher upgrades verify mode to require a peer
// certificate for TLS 1.3 connections in handle_tls_1_3_verify_upgrade so that TLS 1.2
// connections still honor cfg.verify_client below.
int mode = cfg.verify_client ? (SSL_VERIFY_PEER | SSL_VERIFY_FAIL_IF_NO_PEER_CERT) : SSL_VERIFY_NONE;
```

and the upgrade itself:

```cpp
// lib/everest/tls/src/tls.cpp:997-1019
int Server::handle_tls_1_3_verify_upgrade(SSL* ssl, int* /*alert*/) {
    if (not m_verify_client_on_tls13) { return SSL_CLIENT_HELLO_SUCCESS; }
    …
    if (SSL_client_hello_get0_ext(ssl, TLSEXT_TYPE_supported_versions, &data, &datalen) == 1) {
        if (openssl::is_tls_1_3(data, datalen)) {
            log_info("Client supports TLS1.3: Change verify mode to SSL_VERIFY_PEER and "
                     "SSL_VERIFY_FAIL_IF_NO_PEER_CERT");
            SSL_set_verify(ssl, SSL_VERIFY_PEER | SSL_VERIFY_FAIL_IF_NO_PEER_CERT, nullptr);
        }
    }
```

**The call site is where it goes wrong.** `Evse15118D20` speaks ISO 15118-20 and nothing else. It has
no `-2` peers to be lenient for — and it opts into the leniency anyway:

```cpp
// lib/everest/iso15118/src/iso15118/io/connection_ssl.cpp:89-90
out.verify_client = false; // verify_client_on_tls13 upgrades to require a peer cert once TLS 1.3 is negotiated
out.verify_client_on_tls13 = true;
```

`verify_client = false` is the ISO 15118-2 answer, configured into a module that never serves ISO
15118-2. The comment beside it is accurate about the mechanism and silent about the mismatch.

**And the assumption behind the comment is never checked.** *"15118-2 mandates TLS 1.2"* is a statement
about what a conformant `-2` peer does — it is not a test that the peer on this socket is one. Nothing
downstream re-examines it: `d20/state/supported_app_protocol.cpp` is handed the request and the custom
namespace and not the negotiated TLS version, so the same station that declined to authenticate the
peer *because it looked like `-2`* then answers `supportedAppProtocolReq(-20:DC)` with
`OK_SuccessfulNegotiation` — which is what *What it costs* above measured. The inference is made in one
place and contradicted in another, and neither knows about the other.

Everything downstream then behaves correctly for a connection with no peer certificate, which is why
nothing complains (2026.02.1 line numbers; the structure is unchanged):

| | |
|---|---|
| `connection_ssl.cpp:486` | `if (SSL_get_verify_mode(ssl_ptr) != SSL_VERIFY_NONE and peer)` — the whole post-handshake certificate block is skipped |
| `connection_ssl.cpp:499` | so `vehicle_cert_hash` is never filled |
| `d20/state/session_setup.cpp:99` | `not vehicle_cert_hash.has_value()` → **always a new session**, so `[V2G20-2677]` pause/resume on such a connection silently cannot work |

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

**The change we would actually make is one line**, and it is in your module rather than your library:

```cpp
// connection_ssl.cpp:89 — a -20-only station has no -2 peers to be lenient for
out.verify_client = true;
```

`verify_client_on_tls13` can stay exactly as it is; it is right for `EvseV2G`, which is what it was
written for.

**It costs no conformant interop**, which is the part worth putting in the issue: `[V2G20-1237]` and
`[V2G20-2356]` mean a conformant `-20` EV is on TLS 1.3 anyway, so the connections this newly refuses
are the ones `[V2G20-2356]` says the station should not have been serving `-20` on. The only peers
affected are the ones the standard already excludes.

Two alternatives, if you would rather not change the default:

1. **Make it configurable and default it closed.** `enforce_tls_1_3` already exists in the manifest; a
   second value gating client authentication would at least make the current behaviour something an
   operator chose rather than something an EV chose.
2. **Or close the other half instead** — refuse `-20` on a connection that is not TLS 1.3. That needs
   the negotiated version to reach `d20/state/supported_app_protocol.cpp`, which today it does not.
   That is `[V2G20-2356]` and it answers the anonymous-session half rather than the missing
   `CertificateRequest`.

**What we would not suggest any more:** changing the version test in the library. On the previous draft
of this report that was suggestion 1, and it was wrong — it would break `EvseV2G`, which needs exactly
the behaviour the flag provides.

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
station puts in it. **Measured on `2026.02.1` and again on `main` (`ebcd36d`) on 2026-08-12**, the
second time with `-msg`, which turns the first item from a summary line into a parse
([run notes](../interop-runs/2026-08-12-everest-main-tls-profile/notes.md)):

```
<<< TLS 1.3, Handshake [length 003e], CertificateRequest
    0d 00 00 3a 00 00 37 00 0d 00 2a 00 28 09 05 09
    06 09 04 04 03 05 03 06 03 08 07 08 08 08 1a 08
    1b 08 1c 08 09 08 0a 08 0b 08 04 08 05 08 06 04
    01 05 01 06 01 00 1b 00 05 04 00 01 00 03
```

| bytes | |
|---|---|
| `0d 00 00 3a` | CertificateRequest, body 58 |
| `00` | `certificate_request_context`: empty |
| `00 37` | extensions, 55 bytes |
| `00 0d 00 2a` … | ext **13** `signature_algorithms`, inner list 40 bytes = **20 algorithms** |
| `00 1b 00 05` … | ext **27** `compress_certificate` (RFC 8879) |

1 + 2 + 55 = 58, 46 + 9 = 55 — the message is fully accounted for. **Two extensions, and neither is
number 47**, `certificate_authorities`. The same transcript also carries openssl's summary:

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
| signature algorithms, Table 8 | `[V2G20-1667]`: Table 8's two entries in Table 8's order | OpenSSL's default list — **19 entries in 2026-08, 20 in 2026-08-12**, see below |
| named groups, Table 7 | `[V2G20-2460]`: preference from Table 7 — `secp521r1`, then `x448` | `X25519MLKEM768` |

`[V2G20-2404]` is the only exemption for an empty authority list, and it is for a SECC that holds no
roots. This one holds two: it loaded them at `:270` and `:274` and logged no complaint.

**The list is not one anybody chose, and there is now evidence for that rather than an assertion.**
Between the two runs the count went from **19 entries to 20** — nothing in your code changed, the
linked OpenSSL's defaults did. A conformance property that moves when a dependency is upgraded is not
being met on purpose, and `[V2G20-1667]` asks for exactly two entries in a fixed order.

### Where it comes from

One cause for all three: nothing configures them, and on `main` the place where nothing configures them
has moved. In the whole of `lib/everest/tls/src/tls.cpp` there is **no client-CA-list call of any
kind** — no `SSL_CTX_set_client_CA_list`, no `SSL_CTX_add_client_CA`, no `SSL_CTX_set0_CA_list` — and no
`set1_groups`/`set1_curves` and no `set1_sigalgs` on the server path. `configure_verify_locations`
(`tls.cpp:667-695`) calls `SSL_CTX_load_verify_locations` for the V2G and MO roots, which fills the
**verify store**; that is a different thing from the list advertised in the `CertificateRequest`, and it
is the only thing the roots are used for.

### Two things that look like this and are not — please read before replying

Both are on `main` and both would make a reasonable person say *"but we do handle
`certificate_authorities`"*:

- **`ChainConfig`'s doc comment** (`iso15118/config.hpp:26-27`) says multiple chains support *"TLS 1.3
  multi-chain selection driven by the peer's `certificate_authorities` extension (RFC 8446 §4.2.4)"*.
  That is the server **reading** the extension the EV sent, to pick which chain to present.
  `[V2G20-2401]` is about the server **sending** it, in its own `CertificateRequest`, so the EV can pick
  its contract or vehicle certificate. Opposite directions, same extension name.
- **`m_server_trusted_ca_keys.init_ssl(ctx)`** (`tls.cpp:1087`) initialises `trusted_ca_keys` —
  **RFC 6066**, a different extension from RFC 8446's `certificate_authorities`, and the one
  `[V2G2-651]` is about in the ISO 15118-2 world. Its absence in `IsoMux` is
  [a separate finding](everest-isomux.md) §4. It does not populate a `CertificateRequest` either.

So the receiving side was on somebody's mind twice. The sending side is what is missing.

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

## §3 — The station never reads the EV's `certificate_authorities`, so it cannot pick a chain the EV can verify

**Title:** `lib/everest/tls` selects the server chain as `cfg.chains[0]` and never looks at the
`certificate_authorities` extension the EV is obliged to send, so a `-20` station holding more than one
root cannot satisfy `[V2G20-1007]`/`[V2G20-2379]` — and `Evse15118D20` configures exactly one chain, so
today it cannot even try

Added 2026-08-12 from a source reading, and **measured the same day: 3 arms with a control**
([run notes](../interop-runs/2026-08-12-everest-main-chain-selection/notes.md)). It is not latent.

### What we saw

Your station, `main` (`ebcd36d`), `config-sil-dc-d20.yaml` as shipped, **two V2G roots installed with a
valid SECC chain under each**. One variable: what the client puts in `certificate_authorities`.

| arm | the EV asks for | the station serves |
|---|---|---|
| **A** | root **A** — and you hold a valid chain under it | `SECCCert-B` ← `CPOSubCA-B` ← **`V2GRootCA-B`** |
| **B** | root **B** | the same chain B |
| **C** (control) | *no extension at all* | the same chain B |

Byte-identical in all three, and **arm A is the finding on its own**: the EV named a root, you hold a
chain under exactly that root, and you sent the other one. `Verify return code: 20` in every arm is our
client refusing what it got.

**The right answer was available** — this is not an impossibility argument:

```
$ openssl verify -CAfile ca/v2g/V2G_ROOT_CA.pem       -untrusted client/cso/CPO_CERT_CHAIN.pem client/cso/SECC_LEAF.pem
client/cso/SECC_LEAF.pem: OK
```

and your own log names the one that went to the TLS layer instead:

```
evse_security:E :: Requesting leaf certificate info: V2G
evse_security:E :: Found valid leaf: [".../client/cso/CPO_CERT_CHAIN_B.pem"]
```

**One leaf, chosen before any `ClientHello` exists.** `get_leaf_certificate_info` runs while the TLS
server is being built, in response to the SDP request; the EV's list arrives a flight later with
nowhere to go.

### The obligation

`-20` runs the whole certificate-authority conversation over RFC 8446's `certificate_authorities`, in
both directions, and this section is about the direction §2 is *not* about:

- **`[V2G20-1006]`** — an EVCC not in CPM4PE **shall** list every V2G and PE private root it holds in a
  `certificate_authorities` extension in its `ClientHello`. Unconditional, so the data is always there.
- **`[V2G20-1007]`** — a public SECC **shall** send a chain up to a root **the EV named**.
- **`[V2G20-2379]`** — when the EV's list is non-empty, the SECC **shall** use those DistinguishedNames
  to choose a chain originating from one of them. **`[V2G20-2378]`** allows free choice only when the
  list is empty. **`[V2G20-2382]`–`[V2G20-2384]`** say the same for a private SECC not in CPM4PE.

Requirement identifiers and paraphrase only; the rule is
[`docs/normative-basis.md`](../normative-basis.md). All `-20`, no document caveat.

### What the code does

Nothing reads it. On `main`, `lib/everest/tls/src/tls.cpp` contains no `SSL_get0_peer_CA_list`, no
`TLSEXT_TYPE_certificate_authorities`, no DistinguishedName handling of any kind. The server chain is
fixed at init:

```cpp
// tls.cpp:1052-1054
// use the first server chain
const ssl_ctx_params params{true, cfg.ciphersuites, cfg.cipher_list, true, cfg.enforce_tls_1_3};
result = configure_ssl_ctx(ctx, cfg.chains[0], params);
```

The one selection mechanism that does exist — `ServerTrustedCaKeys` — is driven by **`trusted_ca_keys`**,
RFC 6066, which is the ISO 15118-**2** extension and plays no part in `-20`
([why](../normative-basis.md#-20-does-not-use-trusted_ca_keys--it-uses-certificate_authorities-in-both-directions)).
So the `-20` station inherited a selector for a protocol it does not speak and has none for the one it
does.

And `Evse15118D20` gives it one chain to choose from anyway
(`ISO15118_chargerImpl.cpp:276-281`, a single `chains.push_back`).

### Why this is worth raising

**Because you have already written down that it should work this way.** `ChainConfig`'s own doc comment
on `main` says multiple chains exist to support *TLS 1.3 multi-chain selection driven by the peer's
`certificate_authorities` extension, RFC 8446 §4.2.4* (`iso15118/config.hpp:26-27`). The vector is
there, the intent is recorded — the selection step is what is missing between them.

**And because of where it bites.** With one root, a station that ignores the extension is
indistinguishable from one that honours it: there is nothing to choose. It bites when an operator holds
two — mid-rotation, or serving two roots — which is the configuration nobody tests, the one
`[V2G20-1006]`'s multi-root wording is written for, and the one the arms above set up. An EV handed a
chain that does not trace to a root it holds cannot validate it, and ours did not.

### Suggested direction

1. **Read the list.** In the certificate callback, `SSL_get0_peer_CA_list()` gives the
   DistinguishedNames; match against each configured chain's root issuer and install the first that
   matches, falling back to `chains[0]` when the list is empty — which is `[V2G20-2378]` exactly.
2. **Then give the module more than one chain to offer**, or the selection has nothing to work with.
   That is a `libevse-security` question rather than a TLS one, and it is the same question `IsoMux`
   §4 runs into from the `-2` side: `get_leaf_certificate_info` returns the newest single chain, not
   all valid ones.

(1) without (2) is still worth having — it makes the station correct for the empty-list case by
construction and puts the mechanism where the next chain can use it.

### Filing

**Its own issue, separate from §1 and §2.** §1 is *whether* the EV is asked to authenticate, §2 is
*what the `CertificateRequest` carries*, §3 is *which chain the server presents*. Three directions,
three fixes — and all three are now measured, so none of them has to lean on the others.

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
  anywhere in the tree. Same TLS server, same function, and still its own issue:
  [`everest-d20-ocsp-absent.md`](everest-d20-ocsp-absent.md), measured the same day.
- We did not test whether an anonymous session can be driven past `AuthorizationSetup`, because the
  replay could not: the station mints its own session id. An EV rather than a replay would settle it.

---

## Before sending

- [x] **Reproduce it, with a control.** Two arms, fresh station each, one variable — the TLS version
      offered. Arm 1 shows the station demanding a certificate; arm 2 shows the same station asking for
      nothing. **Done twice: `2026.02.1` on 2026-08-10 and `main` on 2026-08-12**, same result, with the
      *"Change verify mode"* lines counted the second time.
- [ ] **Carry it past the handshake — and say which build.** This half is `2026.02.1` only; the
      `main` re-run covered the handshake arms and not the replay, because the request frames were never
      kept as bytes (see the [run notes](../interop-runs/2026-08-12-everest-main-client-auth/notes.md)).
      Either regenerate them from our EVCC and re-run, or state plainly in the issue that the
      consequence was observed on the release. Do not let it ride on the `main` arms.
      <br>What was measured on `2026.02.1`: `supportedAppProtocolReq(-20:DC)` answered
      `OK_SuccessfulNegotiation` and `SessionSetupReq` answered with a session id, on a connection with
      no client certificate — measured, and quoted from *your* session log rather than ours.
- [x] **Re-measure §2 on `main` — done 2026-08-12, and it got sharper.** `-msg` shows the
      `CertificateRequest` carries exactly two extensions, `signature_algorithms` and
      `compress_certificate`, and **not** number 47 — so `[V2G20-2401]` is a byte count rather than an
      inference from an openssl summary line
      ([run notes](../interop-runs/2026-08-12-everest-main-tls-profile/notes.md)).
- [x] **Check every line reference against the tree.**
      `connection_ssl.cpp:54`, `:91`, `:132-146`, `:148-151`, `:223-224`, `:234-235`, `:245`, `:249`,
      `:269-278`, `:280`, `:283`, `:486`, `:499`; `config.hpp:34`;
      `Evse15118D20/manifest.yaml:22`; `d20/state/session_setup.cpp:99` — read from the built 2026.02.1
      source on 2026-08-10, **re-verified 2026-08-11** in the sweep over all 189 `file:line` citations
      in this directory. **They are 2026.02.1 line numbers.** The `main` anchors —
      `tls.cpp:667-695`, `:997-1019`, `:1076-1083`, `:1087`, `connection_ssl.cpp:89-90`,
      `config.hpp:26-27` — were read on 2026-08-11 and are the ones to re-read on the day you post;
      `main` moves daily. This report cites two revisions and labels which is which. The old anchors
      still resolve against the standalone `EVerest/libiso15118`, which is unmaintained and no help.
- [x] **Ask whether the TLS 1.2 path is deliberate — answered 2026-08-11, and it moved the defect.**
      It is deliberate, documented, and **correct in the library**: `lib/everest/tls` serves `-2` and
      `-20` from one code path and must not demand a client certificate on a `-2` connection. The defect
      is that a `-20`-only module opts into that. **Do not open §1 against `lib/everest/tls`** — the
      dual-protocol argument is not a reply to anticipate there, it is simply right, and the issue
      would be closed on it. Open it against `Evse15118D20`'s call site.
- [ ] **Lead with the one sentence, and it is a different sentence now.** *Your `-20`-only station
      configures the TLS layer's ISO 15118-2 client-auth behaviour, so an EV that offers TLS 1.2 gets a
      `-20` session without ever being asked for a certificate.* Everything else is support.
- [ ] **Say the fix is one line at the call site.** `verify_client = true` in `connection_ssl.cpp`, and
      it costs no conformant interop because `[V2G20-1237]`/`[V2G20-2356]` put a conformant `-20` EV on
      TLS 1.3 anyway. A one-line diff that breaks nothing conformant is a very different conversation
      from what this report asked for in its first draft.
- [ ] **Get ahead of the two `certificate_authorities` look-alikes in §2.** The `ChainConfig` comment
      (peer's extension, chain selection) and `trusted_ca_keys` (RFC 6066) both carry the words and
      neither is `[V2G20-2401]`. §2 names both; keep that in the issue or the first reply will be
      *"we do handle that"*.
- [ ] **Say it needs no PKI and no EV.** Two `openssl s_client` calls. That is what will get it looked
      at today rather than next month.
- [x] **Measure §3 rather than reasoning about it — done 2026-08-12, 3 arms with a control.** Two roots
      installed, a valid chain under each; the EV asks for root A and gets root B, and the served chain
      is byte-identical whether the EV asks for A, for B, or sends no extension at all
      ([run notes](../interop-runs/2026-08-12-everest-main-chain-selection/notes.md)).
- [x] **File three issues, §1, §2 and §3 — split and drafted 2026-08-12** into
      [`everest-d20-client-auth/`](everest-d20-client-auth/README.md), each self-contained and naming
      the other two. The reason they stay apart is sharper than this checklist used to give: §1 has an
      *answer* available (*"the TLS 1.2 path is there for ISO 15118-2"*) that closes its framing without
      touching §2 or §3, and a one-line *fix* that leaves both standing. §2 and §3 have no such answer.
      <br>Suggested posting order is **1, 3, 2** — smallest fix first, because a maintainer who has just
      merged a one-liner reads the next one. By severity it is 3, 1, 2, and the README says both.
- [ ] **§3: lead with their own comment, not with the clause.** `ChainConfig`'s doc comment already
      says multi-chain selection is driven by the peer's `certificate_authorities` — the report is
      asking why the step between the vector and the selection is missing, not proposing a new idea.
- [ ] **Mention the `IsoMux` sibling for `[V2G20-2356]`** if both are open at once.
- [ ] **Post under your own name, in your own words.**
