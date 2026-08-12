# 2026-08-10 — the EV decides whether an `Evse15118D20` station authenticates it

Their `-20` station switches on client authentication **from the `supported_versions` list in the
`ClientHello`**. Offer TLS 1.3 and it demands a vehicle certificate and refuses without one. Offer
TLS 1.2 only and it asks for nothing — and then serves ISO 15118-20 on that connection: our own recorded
`supportedAppProtocolReq(-20:DC)` gets `OK_SuccessfulNegotiation`, `SessionSetupReq` gets a session id,
and their log reads `Transition (SessionSetup -> AuthorizationSetup)`. No certificate anywhere.

`[V2G20-2400]` puts the `CertificateRequest` on the SECC unconditionally. This one sends it only when
the client asked for the version that would have made it awkward to skip.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), source build in WSL2 Debian 13, OpenSSL 3.5.6 |
| Their module | `Evse15118D20` alone (no `IsoMux`), `tls_negotiation_strategy: ENFORCE_TLS`, **`enforce_tls_1_3` left at its manifest default `false`** |
| Ours | `openssl s_client` for the handshake arms; for the consequence, two frames replayed byte-for-byte out of our own `-20` DC session vector |
| Outcome | **A client that offers only TLS 1.2 is never asked for a certificate, and gets a full `-20` session anyway** |
| Artifacts | [`their-charger.tls13-nocert.log`](their-charger.tls13-nocert.log) · [`openssl.tls13-nocert.log`](openssl.tls13-nocert.log) · [`their-charger.tls12-nocert.log`](their-charger.tls12-nocert.log) · [`openssl.tls12-nocert.log`](openssl.tls12-nocert.log) · [`their-charger.consequence.log`](their-charger.consequence.log) · [`replay.consequence.hex`](replay.consequence.hex) · [`their-session-log.consequence.yaml`](their-session-log.consequence.yaml) · [`config-d20-tls12-ours.yaml`](config-d20-tls12-ours.yaml) |
| Filed | [`everest-d20-client-auth.md`](../../reports/everest-d20-client-auth.md) |

## Two arms at the handshake, no PKI on our side at all

Fresh station each — their `-20` station serves one session per SDP request, and after a refused
handshake the accept loop is dead anyway ([`everest-loop-shutdown.md`](../../reports/everest-loop-shutdown.md),
reproduced again below). Same config, same station, same certificate on their side; the *only*
difference is which TLS version the client offered.

| Arm | `openssl s_client` | Their log | Result |
|---|---|---|---|
| **1** (control) | `-tls1_3` | `Client supports TLS1.3: Change verify mode to SSL_VERIFY_PEER and SSL_VERIFY_FAIL_IF_NO_PEER_CERT` | **refused** — `tls_process_client_certificate:peer did not return a certificate` |
| **2** | `-tls1_2` | *(no such line)* | **`Handshake complete!`** — `TLSv1.2`, `ECDHE-ECDSA-AES128-SHA256`, no `CertificateRequest` |

Arm 1 is what makes arm 2 a finding rather than a configuration: the station **does** know how to
demand a vehicle certificate, has the roots loaded to check one, and does it — when the EV offers 1.3.

Neither arm needed a client certificate, a client PKI, or an EV. Two `openssl s_client` invocations
against their stock configuration reproduce the whole thing.

## What an anonymous connection can do

Same station, same TLS 1.2 handshake with no client certificate, then two frames replayed out of
[`Session.iso20-dc-eim.trace.json`](../../../ISO15118ConformanceTests.Simulation/Vectors/Session.iso20-dc-eim.trace.json)
— our own encoder's bytes, so what was offered is not in doubt:

```
→ 01fe8001 00000025 8000f3ab…d222 18010000040040   supportedAppProtocolReq, one entry, -20:DC
← 01fe8001 00000004 80400040                       OK_SuccessfulNegotiation
→ 01fe8002 00000019 808c04000000000000000008…     SessionSetupReq
← 01fe8002 00000023 8090046d38567069692acb0a…     SessionSetupRes
```

> **What `replay.consequence.hex` contains — read this before reusing it.** The two frames in that file
> are the **station's responses** above (`80400040` and the `SessionSetupRes`), not the two requests we
> sent. The name reads as *the replay* and it is *the consequence*. Feeding it back to a station as
> input gets `Expected SupportedAppProtocol`, correctly, and teaches nothing — which cost a wrong
> conclusion on [2026-08-12](../2026-08-12-everest-main-client-auth/notes.md). The **request** frames
> were not kept as bytes; they survive only as the elided hexdumps above.

and their own session log, written by their own station
([`their-session-log.consequence.yaml`](their-session-log.consequence.yaml)):

```yaml
info: "Transition (SupportedAppProtocol -> SessionSetup)"
…
info: "Transition (SessionSetup -> AuthorizationSetup)"
```

with, in the charger log:

```
[INFO] iso15118_charge :: Handshake complete!
[INFO] iso15118_charge :: Received session setup with evccid: EVCC01
[INFO] iso15118_charge :: New session created with session_id: 0xDA, 0x70, 0xAC, 0xE0, 0xD2, 0xD2, 0x55, 0x96
```

So an unauthenticated peer is at `AuthorizationSetup` on an ISO 15118-20 station. The replay stops
there because the station mints its own session id and every later request has to echo it — not
because anything refused.

That answer is also `[V2G20-2356]` on its own: the SECC selected `-20` out of a
`SupportedAppProtocolReq` that arrived over TLS 1.2. Same requirement as
[`everest-isomux.md`](../../reports/everest-isomux.md) §2, a different module, no multiplexer in front
of it, and its own TLS server rather than the mux's.

## Where it comes from

`lib/everest/iso15118/src/iso15118/io/connection_ssl.cpp`. The default is refuse-nothing, and the
`ClientHello` callback is the only thing that ever changes it:

```cpp
// :278, in init_ssl()
SSL_CTX_set_verify(ctx, SSL_VERIFY_NONE, nullptr);
SSL_CTX_set_client_hello_cb(ctx, &client_hello_cb, nullptr);

// :132-146
int client_hello_cb(SSL* ssl, int* /* alert */, void* /* object */) {
    if (SSL_client_hello_get0_ext(ssl, TLSEXT_TYPE_supported_versions, &data, &datalen)) {
        const auto tls_1_3_found = is_tls_1_3(data, datalen);      // :91, scans the offered list
        if (tls_1_3_found) {
            logf_info("Client supports TLS1.3: Change verify mode to SSL_VERIFY_PEER and "
                      "SSL_VERIFY_FAIL_IF_NO_PEER_CERT");
            int mode = SSL_VERIFY_PEER | SSL_VERIFY_FAIL_IF_NO_PEER_CERT;
            SSL_set_verify(ssl, mode, nullptr);
        }
    }
    …
}
```

`:269` says what the intent was — *"Loading root certificates to verify client (only for tls 1.3)"* —
and `:234-235` is what makes the other branch reachable: `enforce_tls_1_3` is `false` by default
(`config.hpp:34`, `connection_ssl.cpp:54`, `Evse15118D20/manifest.yaml:22`), so the minimum protocol
version is TLS 1.2 and the `-2` cipher suite is enabled for it at `:245`.

Everything downstream is consistent with an unauthenticated connection, correctly:

| | |
|---|---|
| `:486` | `if (SSL_get_verify_mode(ssl_ptr) != SSL_VERIFY_NONE and peer)` — the post-handshake certificate block is skipped whole |
| `:499` | so `vehicle_cert_hash` is never filled |
| `d20/state/session_setup.cpp:99` | `not vehicle_cert_hash.has_value()` → **always a new session**. Pause/resume cannot work on such a connection at all, silently |

## And the `CertificateRequest` names no trust anchors

Visible in the same capture, in the arm where a `CertificateRequest` *was* sent
([`openssl.tls13-nocert.log:73`](openssl.tls13-nocert.log)):

```
No client certificate CA names sent
Requested Signature Algorithms: id-ml-dsa-65:id-ml-dsa-87:id-ml-dsa-44:ECDSA+SHA256:…:RSA+SHA512
Negotiated TLS1.3 group: X25519MLKEM768
```

Three separate things, all from the same cause — nothing in `init_ssl` configures them:

- **`certificate_authorities` is absent.** `SSL_CTX_set_client_CA_list` is never called anywhere in
  `lib/everest/iso15118` (grep: no `set_client_CA_list`, no `add_client_CA`). `[V2G20-2401]` (public
  SECC) and `[V2G20-2402]` (private, not CPM4PE) both require the list; `[V2G20-2404]` permits an empty
  one only when the SECC holds no roots, and this one holds two — it loaded them at `:270` and `:274`
  without complaint. The `TODO` at `:150` and the commented-out `SSL_CTX_set_cert_cb` at `:283` show
  the extension was on the author's mind from the *receiving* side.
- **The signature-algorithm list is OpenSSL's default**, not Table 8. `[V2G20-1667]` — the SECC shall
  include them in Table 8's order. The list above offers ML-DSA, RSA and brainpool to a vehicle
  certificate; Table 8 has two entries, `ecdsa_secp521r1_sha512` and `ed448`. No `set1_sigalgs_list`
  call exists in the tree.
- **The named group is OpenSSL's default too.** `[V2G20-2460]` — the SECC's preference shall be
  Table 7's, which is `secp521r1` then `x448`. `X25519MLKEM768` is in neither. No `set1_groups_list`
  call exists in the tree.

Worth saying plainly, because it shapes the report: **Table 6 *is* implemented** — `:224` sets exactly
the two Table 6 suites in Table 6's order. Whoever wrote that line had the profile open. Tables 7 and 8
were not carried across.

## The requirements

- **`[V2G20-2400]`** — the SECC shall request the EVCC's certificate via `CertificateRequest`.
  Unconditional: no TLS-version qualifier, no public/private split. NOTE 23 beside it says what it is
  for — a mutually authenticated session in which each side verifies the other.
- **`[V2G20-1264]`** — mutual authentication with TLS 1.3 shall be supported by every V2G entity.
- **`[V2G20-2356]`** — the SECC shall not select `-20` where the connection is TLS 1.2 or below;
  `[V2G20-2359]` permits *serving* TLS 1.2 for backwards compatibility, which is why the finding is the
  missing `CertificateRequest` and the `-20` answer, not the TLS 1.2 listener.
- **`[V2G20-2401]` / `[V2G20-2402]`**, with **`[V2G20-2404]`** as the only exemption, and
  **`[V2G20-2403]`** for the DN contents.
- **`[V2G20-1667]`** (signature algorithms in Table 8 order), **`[V2G20-2460]`** (named-group preference
  from Table 7), against **`[V2G20-2458]`/`[V2G20-1856]`** — the cipher-suite pair they *did*
  implement.

All `-20` identifiers; no document caveat. Recorded in [`normative-basis.md`](../../normative-basis.md).

## Also reproduced, already filed

Arm 1 ends with

```
[ERRO] iso15118_charge :: Shutdown loop() because of: Failed to SSL_accept(): 1: …
                          peer did not return a certificate
```

— one refused handshake takes the station's accept loop down for the rest of the process's life. That
is [`everest-loop-shutdown.md`](../../reports/everest-loop-shutdown.md), unchanged at 2026.02.1, and it
is *why* every arm here gets a fresh station. Not re-filed.

Their SECC leaf is `prime256v1` / `ecdsa-with-SHA256`, outside the `-20` profile's Tables 6 to 8. That
is their test PKI rather than this code, and it is already
[`josev-iso20-pki-curve.md`](../../reports/josev-iso20-pki-curve.md).

## How it was run

```bash
grep -v enforce_tls_1_3 config-d20-tls-ours.yaml > config-d20-tls12-ours.yaml   # manifest default = false
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-d20-tls12-ours.yaml &   # fresh per arm
SECURITY=00 bash sdp-probe.sh eth0                                             # → [fe80::…%eth0]:50000
openssl s_client -tls1_3 -connect "$EP" -showcerts </dev/null                   # arm 1
openssl s_client -tls1_2 -connect "$EP" -showcerts </dev/null                   # arm 2
{ printf "$SAP_BYTES"; sleep 2; printf "$SESSIONSETUP_BYTES"; sleep 3; } \
  | openssl s_client -quiet -tls1_2 -connect "$EP" | xxd                        # consequence
```

Three rig notes, all of which cost time before:

- **The broker is not part of the manager.** `mosquitto -d -p 1883` first, or every module dies at
  `Cannot connect to MQTT broker at localhost:1883` and the SDP probe simply gets no answer — which
  looks exactly like a station that is up and ignoring you.
- **`SECURITY=00`** in `sdp-probe.sh` is the TLS endpoint; the default `10` is plain TCP.
- **Write the driving script to a file and run `bash /mnt/c/…/script.sh`.** `wsl -- bash -lc '…$VAR…'`
  loses `$f`, `~`, and globs to the Windows shell before WSL ever sees them; every one of those failures
  this session looked like an empty file rather than an error.
