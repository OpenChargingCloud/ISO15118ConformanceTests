# Draft report to EVerest — a failed TLS handshake ends `Evse15118D20`'s V2G loop

Status: **draft, not sent.** Reproduced 2026-08-05 on everest-core 2026.02.1 built from source, three
times, one attempt each — including **against their own shipped `config-sil-dc-d20.yaml`, unmodified**.
Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository: [`trigger3-tls-accept-shutdown.log`](../interop-runs/2026-08-05-everest-2026021-matrix/trigger3-tls-accept-shutdown.log)
(the three reproductions plus the contrast case, filtered from the station logs), and the run notes
[`2026-08-05-everest-2026021-matrix`](../interop-runs/2026-08-05-everest-2026021-matrix/notes.md).
The original 2025.10.0 sightings are in
[`2026-08-03-everest-iso20-dc-tls13`](../interop-runs/2026-08-03-everest-iso20-dc-tls13/notes.md) and
[`2026-08-03-everest-iso20-dc-full-charge`](../interop-runs/2026-08-03-everest-iso20-dc-full-charge/notes.md).

Four other reports for the same project, all unrelated to this one, are in
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md),
[`everest-isomux-iso20-over-tls12.md`](everest-isomux-iso20-over-tls12.md),
[`everest-isomux-sap-priority.md`](everest-isomux-sap-priority.md) and
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md). **File them separately.**

## Before the defect — what everest-core has been worth to us

Not politeness, and worth a paragraph because it is the reason this report exists at all rather than a
bug filed by a stranger.

**everest-core has found more defects in this project than every other counterparty combined**, across
twenty-four runs. Almost all of them share one shape: a value we had taken from our own assumption where
the protocol supplies one — an unbounded "Ongoing" poll, an energy transfer mode we hard-coded instead
of reading from `ServiceDiscoveryRes`, a preference list we treated as a filter rather than a ranking,
an EV-side power envelope written as literals. Their SIL is single-phase, their station enforces the
service/parameter coupling, their `EvseManager` logs what it actually received — so every one of those
surfaced as a refusal or a wrong number rather than as a green test.

None of that is reachable against a loopback peer: our own station answers in kind, advertises exactly
what our EV asks for, and enforces nothing. It took a real charger to find them, and this is the one.

That is also the honest framing for what follows: we ran everest-core hard enough to hit this, and we
hit it the same way a car in the field would.

---

**Title:** `Evse15118D20`: a TLS handshake that fails in `SSL_accept()` shuts down
`TbdController::loop()` — the charger then answers nothing while its process stays healthy

**Version:** everest-core **2026.02.1** (`b61bb12b8`), libiso15118 **v0.9.1** as vendored in-tree at
`lib/everest/iso15118`, OpenSSL 3.5.6, Debian 13. Also present in 2025.10.0.

## Summary

One TCP connection that fails TLS client-certificate verification permanently disables the ISO 15118-20
charger. `ConnectionSSL::handle_data()` runs as a poll handler; when `SSL_accept()` fails it raises,
the exception surfaces at `poll_manager.poll()` inside `TbdController::loop()`, and that handler is
`break`:

```cpp
// src/iso15118/tbd_controller.cpp:51-56
try {
    poll_manager.poll(poll_timeout_ms);
} catch (const std::runtime_error& e) {
    logf_error("Shutdown loop() because of: %s", e.what());
    break;
}
```

`TbdController` is not destroyed, so **the SDP socket stays bound and is never read again**. Since
`Evse15118D20` creates its TCP server only in response to an SDP request, and no SDP request is ever
processed again, the charger can no longer be reached at all — by any EV, over TLS or plaintext. The
module process and the manager both stay alive, so nothing supervising them notices.

Measured after one failed handshake:

```
--- SDP after: NO ANSWER                     (multicast, security=00 — TLS)
--- SDP after: NO ANSWER                     (multicast, security=10 — plain TCP)
UNCONN 960 0 *%eth0:15118 users:(("iso15118_charge",pid=21969,fd=10))
module alive: 1; manager alive: 1
```

`Recv-Q 960` is the SDP requests queueing up unread in the kernel buffer: the socket is bound, the
reader is gone.

## Why we think it is worth fixing rather than configuring around

**It is reachable from your own default configuration, by an EV that does nothing unusual.**
`tls_negotiation_strategy` defaults to `ACCEPT_CLIENT_OFFER`, and the module switches to
`SSL_VERIFY_PEER | SSL_VERIFY_FAIL_IF_NO_PEER_CERT` as soon as the client offers TLS 1.3. So any EV
that speaks TLS 1.3 and does not present a certificate your `EvseSecurity` accepts — a car with an
expired or not-yet-installed vehicle certificate, a car from another PKI, a probe, a scan — takes the
charger down for every subsequent car until someone restarts the module.

It is also **not** a case of the loop dying on a genuinely unrecoverable condition: the same class of
error on an already-established session is handled exactly the way we would expect, one level up.

## The asymmetry, in your own code

Both OpenSSL call sites are written the same way — `WANT_READ`/`WANT_WRITE` return, anything else
raises:

```cpp
// src/iso15118/io/connection_ssl.cpp:414-420   (read path, on an established session)
const auto ssl_error = SSL_get_error(ssl_ptr, ssl_read_result);
if ((ssl_error == SSL_ERROR_WANT_READ) or (ssl_error == SSL_ERROR_WANT_WRITE)) {
    return {true, 0};
}
log_and_raise_openssl_error("Failed to SSL_read_ex(): " + std::to_string(ssl_error));
```

```cpp
// src/iso15118/io/connection_ssl.cpp:475-480   (accept path, during the handshake)
const auto ssl_error = SSL_get_error(ssl_ptr, ssl_handshake_result);
if ((ssl_error == SSL_ERROR_WANT_READ) or (ssl_error == SSL_ERROR_WANT_WRITE)) {
    return;
}
log_and_raise_openssl_error("Failed to SSL_accept(): " + std::to_string(ssl_error));
```

What differs is **where the throw lands**, and the two log lines below show it directly. The read
path's failure reaches the per-session `catch`, which does the right thing:

```cpp
// src/iso15118/tbd_controller.cpp:60-68
try {
    const auto next_session_event = session->poll();
    …
} catch (const std::runtime_error& e) {
    logf_error("Shutting down session because of: %s", e.what());
    logf_info("Restarting session ...");
    session->close();
}
```

The accept path's failure is raised inside a poll handler — `handle_data`, registered at
`connection_ssl.cpp:459` — so it surfaces at `poll_manager.poll()` and hits the `break` above.

(All five line references above were re-checked against the 2026.02.1 tree on 2026-08-07 and still
point where they say.)

Observed, same build, minutes apart:

| what happened | their log | loop afterwards |
|---|---|---|
| valid mutual handshake, client disconnects mid-session | `Shutting down session because of: Failed to SSL_read_ex(): 6` → `Closing TLS connection` → `TLS connection closed gracefully` | **alive** — SDP answers, port 50000 served again |
| handshake fails during `SSL_accept` | `Shutdown loop() because of: Failed to SSL_accept(): 1: …` | **gone** — SDP silent, socket still bound |

## A second contrast, from a module of your own

Added 2026-08-06. `IsoMux` was given the same treatment — a TLS 1.3 hello it refuses (it serves the -2
profile, so it answers `alert 70`), and then a second refused handshake from `openssl` — and **it kept
accepting**: the TLS 1.2 probe afterwards completed normally, and two full ISO 15118-20 sessions ran to
`SessionStop` in the same process ([run notes](../interop-runs/2026-08-06-everest-isomux-tls/notes.md)).

It is a different TLS termination (`lib/everest/tls`), so this is not a fix to copy across. What it
gives is a second data point in the same tree: **the project's other TLS-terminating server survives
what this one dies on**, and nothing about the deployment makes surviving it impractical.

Worth saying because of where it leaves the exposure. Behind `IsoMux` this defect is unreachable — not
because it is handled, but because the mux never lets a TLS 1.3 hello through to the backend at all,
which is [its own separate report](everest-isomux-iso20-over-tls12.md). Where the
defect bites is `Evse15118D20` **addressed directly**, which is exactly what your shipped
`config-sil-dc-d20.yaml` does, and what the reproduction below uses.

## Reproduction — one command, no ISO 15118 stack needed

No car, no charging session, no MQTT interaction. **Their shipped configuration, unmodified.**

1. Start `config/config-sil-dc-d20.yaml` as it ships. (The module needs a V2G certificate present or it
   aborts at startup; we copied `tests/ocpp_tests/test_sets/everest-aux/certs/` into
   `etc/everest/certs/`, which is your own test PKI.)
2. One multicast SDP request, so the module opens its TCP server, and note the port it answers with
   (50000 here):
   ```bash
   printf '\x01\xfe\x90\x00\x00\x00\x00\x02\x00\x00' \
     | socat -T3 - 'UDP6-DATAGRAM:[ff02::1%eth0]:15118,bind=:15119' | od -An -tx1
   ```
3. Connect with TLS 1.3 and **no client certificate**:
   ```bash
   openssl s_client -connect '[<charger-ll-addr>%eth0]:50000' -tls1_3 </dev/null
   ```
   The client sees `tlsv13 alert certificate required`; the station logs, ~3 ms later:
   ```
   Client supports TLS1.3: Change verify mode to SSL_VERIFY_PEER and SSL_VERIFY_FAIL_IF_NO_PEER_CERT
   [ERRO] Shutdown loop() because of: Failed to SSL_accept(): 1: …:tls_process_client_certificate:
          peer did not return a certificate:../ssl/statem/statem_srvr.c:3751:
   ```
4. Repeat step 2. **No answer**, for either security byte, for as long as the process runs.

Reproduced three times, one attempt each: their stock config as above; and with
`ENFORCE_TLS` + `enforce_tls_1_3`, once with no client certificate and once with an untrusted
self-signed one (`…:tls_process_client_certificate:certificate verify failed:…:3764:`). Both messages
are the same defect; only the OpenSSL verification failure differs. We first hit this on 2025.10.0
while driving a third-party EVCC against `Evse15118D20`, from a client-certificate chain your OpenSSL
could not build.

## Suggested direction

We would happily send a PR for either, if you agree with the shape:

1. **Scope accept-path failures to the connection.** A handshake that fails verification is a
   per-client event; closing that connection and continuing to serve is what the session path already
   does thirty lines away.
2. **If ending `loop()` is ever right, make it visible.** Closing the listening sockets on the way out
   would at least let a supervisor see a dead module, instead of a process that is up and answers
   nothing. As it stands the failure mode is silent by construction.

## Also seen, secondary

- **`enable_tls_key_logging: true`** took the loop down the same way on 2025.10.0
  (`Could not set interface name:eth0 (reason: Protocol not available)`, raised from the
  `TlsKeyLoggingServer` constructor in the accept path). That run was under `qemu-x86_64`, where
  `SO_BINDTODEVICE` may simply not be implemented — so treat it as another instance of the pattern,
  not as an independent bug report. Not retried on 2026.02.1.
- **A unicast SDP request** was a third trigger on 2025.10.0
  (`Read on sdp server socket failed (reason: Resource temporarily unavailable)` — `EAGAIN` on a
  non-blocking socket treated as fatal). On **2026.02.1 this no longer reproduces**: 0 of 2 attempts,
  idle and mid-session, both security bytes; the request is answered or ignored with a log line and
  the loop survives. The code it went through is unchanged —

  ```cpp
  // src/iso15118/io/sdp_server.cpp:104-107
  const auto read_result = recvfrom(fd, udp_buffer, sizeof(udp_buffer), 0, …);
  if (read_result <= 0) {
      log_and_throw("Read on sdp server socket failed");
  }
  ```

  — and `read_result == 0`, a zero-length datagram, is still fatal there, so the same shape remains
  reachable in principle. Worth a look while you are in this file, but we are **not** reporting it as
  a live defect.

---

## Before sending

- [x] **Reproduce it yourself.** Done 2026-08-05 on 2026.02.1: 3/3, including their unmodified stock
      config, plus the contrast case showing the session path handling the same error class correctly.
- [x] **Re-check every line reference against the tree**, rather than trusting a five-day-old note —
      done 2026-08-07 against the built 2026.02.1 source: all five still point where they say.
- [x] **Lead with what the project has been worth to us.** Above, and every claim in it has runs
      behind it. A report that opens with "your charger can be bricked" reads differently when the
      sender has been on the receiving end of the same courtesy two dozen times.
- [ ] **File one issue, this one.** The other observations from these runs are deliberately not in
      here: -20 PnC being commented out (their own documented TODO) and their SECC sending only its
      leaf (arguably deployment), both written up in the run notes and able to go separately. The two
      that were listed here as unfilable — the mux's TLS-1.2-only termination in front of a -20
      backend (*"a layering question"*) and `IsoMux` ignoring SAP `Priority` (*"rests on requirement
      text we do not hold"*) — both have their own drafts since 2026-08-09, when that text arrived:
      [`everest-isomux-iso20-over-tls12.md`](everest-isomux-iso20-over-tls12.md) and
      [`everest-isomux-sap-priority.md`](everest-isomux-sap-priority.md). The `PyEvJosev` manifest one
      has its own too.
- [ ] **Post under your own name, in your own words.** Keep a sentence on how it was hit in practice —
      a third-party EVCC against `Evse15118D20`, not a fuzzer — because that tells a maintainer whether
      the scenario is realistic. It is the difference between "hardening" and "a car in the field can
      brick this charger until it is restarted".
- [ ] **Offer the PR only after they have said whether they want it**, and for the accept-path scoping
      first — item 2 is a design call that is theirs to make.
