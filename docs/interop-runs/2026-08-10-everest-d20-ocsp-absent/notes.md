# 2026-08-10 — `Evse15118D20` never staples an OCSP response, and has nowhere to put one

Asked their ISO 15118-20 station for a stapled OCSP response the way `[V2G20-2372]` says every `-20` EV
asks — `status_request` in the `ClientHello` — and got **`OCSP response: no response sent`** on TLS 1.2
and on TLS 1.3. Their log says nothing at all, because `libiso15118` contains no OCSP handling: the
grep for `status_request`, `tlsext_status`, `OCSP` and `ocsp` across that whole tree returns nothing.

This is **not** [the OCSP-dropped filing](../../reports/everest-evse-security-ocsp-dropped.md) seen
again. That one is a lost struct member on the `EvseV2G` path, where the machinery exists and the data
never arrives. Here the machinery does not exist, and there are three independent places it is missing —
so a fix to the conversion does nothing for this module, and a fix here does nothing without the
conversion.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), source build in WSL2 Debian 13, OpenSSL 3.5.6 |
| Their module | `Evse15118D20` / `libiso15118`, `tls_negotiation_strategy: ENFORCE_TLS`, stock `enforce_tls_1_3` default. Control: `IsoMux` + `EvseV2G` from `config-mux-tls-ours.yaml` |
| Ours | `openssl s_client -status`. No EV, no session, no client PKI |
| Outcome | **No staple on either TLS version — and the control proves the request reached them** |
| Artifacts | [`openssl.status-tls12.log`](openssl.status-tls12.log) · [`openssl.status-tls13.log`](openssl.status-tls13.log) · [`their-charger.status-tls12.log`](their-charger.status-tls12.log) · [`their-charger.status-tls13.log`](their-charger.status-tls13.log) · [`openssl.mux-control.log`](openssl.mux-control.log) · [`their-charger.mux-control.log`](their-charger.mux-control.log) |
| Filed | [`everest-d20-ocsp-absent.md`](../../reports/everest-d20-ocsp-absent.md) |

## What was asked, and what came back

```
openssl s_client -status -tls1_2 -connect [fe80::…%eth0]:50000   → OCSP response: no response sent
openssl s_client -status -tls1_3 -connect [fe80::…%eth0]:50000   → OCSP response: no response sent
```

Both handshakes reached the point of carrying the certificate chain: `TLSv1.2` /
`ECDHE-ECDSA-AES128-SHA256` and `TLSv1.3` / `TLS_AES_256_GCM_SHA384`. The TLS 1.3 arm then failed for an
unrelated reason — no client certificate, which is
[the client-auth filing](../../reports/everest-d20-client-auth.md) — but that happens *after* the
server's `Certificate` message, so the absent status extension is measured either way.

Their charger log across both arms contains no line about OCSP, `status_request` or the extension at
all. There is nothing to log.

## The control, which is the point of this run

The first thing anyone will suspect is that our client never sent the extension. So the **same client,
the same `-status` flag**, against a module in the same repository whose TLS layer does implement it —
`IsoMux`, from `config-mux-tls-ours.yaml`:

```
[INFO] iso_mux:IsoMux :: Incoming TLS connection
[ERRO] iso_mux:IsoMux :: OcspCache::lookup: not in cache: d8817041a94bb65646ea392c812fcb4978ae4cf6
```

`OcspCache::lookup` is reached **only from the `status_request` extension handlers** —
`ServerStatusRequest::set_ocsp_response` and `ServerStatusRequestV2` at
`lib/everest/tls/extensions/status_request.cpp:168` and `:250`, with the miss logged at `:117`. It ran,
with the hash of their own SECC leaf. So the extension was on the wire, and the client asked correctly.

`IsoMux` then answers `no response sent` too — for the *other* reason, the empty cache that
[`everest-evse-security-ocsp-dropped.md`](../../reports/everest-evse-security-ocsp-dropped.md) is about.
Two modules, the same silence, two unrelated causes, and only one of them has a handler that speaks up.

## Where it comes from — three places, each sufficient on its own

1. **It is never requested.** `modules/EVSE/Evse15118D20/charger/ISO15118_chargerImpl.cpp:181-182`:

   ```cpp
   const auto certificate_response = mod->r_security->call_get_leaf_certificate_info(
       types::evse_security::LeafCertificateType::V2G, types::evse_security::EncodingFormat::PEM, false);
                                                                                     // include_ocsp ↑
   ```

   The third argument is `include_ocsp` (`interfaces/evse_security.yaml:153`). `EvseV2G` passes `true`
   here and uses `get_all_valid_certificates_info`; this module passes `false`.

2. **There is nowhere to carry it.** `lib/everest/iso15118/include/iso15118/config.hpp:22-36` —
   `SSLConfig` has eleven members: chain, key, password, two roots, three flags, a path, a backend and a
   config string. No OCSP member, so `ISO15118_chargerImpl.cpp:207-223` could not pass one if it had it.

3. **Nothing would send it.** `connection_ssl.cpp:220-300`, `init_ssl()`, never calls
   `SSL_CTX_set_tlsext_status_cb`. Grep across `lib/everest/iso15118` for `status_request`,
   `tlsext_status`, `OCSP`, `ocsp`: **no matches**.

And a fourth, outside this module: even with (1) set to `true`, `to_everest(CertificateInfo)` drops the
`ocsp` member (`lib/everest/conversions/evse_security/src/conversions.cpp:429`), so the reply would
arrive empty — that is the earlier filing. **Neither fix alone produces a staple.** Worth saying in the
issue, because a maintainer who fixes the conversion has every reason to think this one closed with it.

For contrast, `lib/everest/tls` — the library `EvseV2G` and `IsoMux` use — has `status_request.cpp`,
`status_request.hpp`, an `OcspCache`, `status_request_v2` support and unit tests for all of it. The
capability is in the same repository; the `-20` stack is a separate TLS implementation that did not get
it.

## The requirements, and the exemption that might apply

- **`[V2G20-2372]`** — the EVCC **shall** include `status_request` in its `ClientHello`, with
  **`[V2G20-2373]`** a zero-length `responder_id_list`. So the question is always asked; there is no
  configuration in which a `-20` station is not asked.
- **`[V2G20-2388]`** — the **public** SECC shall include an OCSP response for each certificate in the
  chain it sent. Unqualified for a public station.
- **`[V2G20-2391]`** — a **private** SECC **that supports PnC** shall do the same.
- **`[V2G20-2398]`** — and a private SECC **not** supporting PnC may ignore the extension entirely.
  **This is the exemption to ask about before calling it a defect**: `Evse15118D20` warns
  *"Currently Plug&Charge is not supported and ignored"* (`ISO15118_chargerImpl.cpp:714`), so a private,
  non-PnC deployment of it is exactly the case `[V2G20-2398]` permits. A public charging station is not,
  and everest-core's stated purpose is public charging infrastructure.
- **`[V2G20-1021]`** — a response may be reused for at most a week, which is what makes stapling
  practical at all.

**And `-20` is softer than `-2` here, which the report has to say.** `[V2G2-873]` makes a `-2` EV
*close the connection* when it asked and nothing came back; the `-20` equivalent, **`[V2G20-2411]`**,
says the EVCC **may** contact the OCSP responder itself. So a `-20` EV is not obliged to abandon the
session. But **`[V2G20-1240]`** still makes the revocation check itself a `shall`, performed via an OCSP
response — and an EV on the end of a charging cable is the case stapling exists for. The honest severity
is *"the EV must now do something it may not be able to do"*, not *"the handshake fails"*.

Recorded in [`normative-basis.md`](../../normative-basis.md).

## How it was run

```bash
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-d20-tls12-ours.yaml &   # fresh per arm
SECURITY=00 bash sdp-probe.sh eth0
openssl s_client -status -tls1_2 -connect "$EP" </dev/null
openssl s_client -status -tls1_3 -connect "$EP" </dev/null
# control — the mux prints its port at boot, no SDP needed
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-mux-tls-ours.yaml &
openssl s_client -status -tls1_2 -connect "[fe80::…%2]:64110" </dev/null
```

One rig note beyond the ones already in
[`…-d20-client-auth`](../2026-08-10-everest-d20-client-auth/notes.md): `IsoMux` **logs its listening
port at boot** and needs no SDP probe, unlike `Evse15118D20`, which creates the TCP server only when an
SDP request arrives. The mux's port came straight out of its own log line.
