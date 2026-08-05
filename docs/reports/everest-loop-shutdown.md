# Draft report to EVerest — libiso15118 `loop()` shutdown

Status: **draft, not sent — re-verified on 2026.02.1 (2026-08-05); file it with trigger 3 as the lead.**
Evidence: [`../interop-runs/2026-08-03-everest-iso20-dc-tls13/`](../interop-runs/2026-08-03-everest-iso20-dc-tls13/notes.md)
and [`../interop-runs/2026-08-03-everest-iso20-dc-full-charge/`](../interop-runs/2026-08-03-everest-iso20-dc-full-charge/notes.md).

> **2026-08-05, against everest-core 2026.02.1 built from source** (libiso15118 v0.9.1 vendored
> in-tree; see [`../interop-runs/2026-08-05-everest-2026021-matrix/`](../interop-runs/2026-08-05-everest-2026021-matrix/notes.md)):
>
> - **Trigger 1 (unicast SDP) no longer reproduces** — 0/2 attempts, idle and in-session, both
>   security bytes. The request is answered (idle) or ignored with a log line (in-session), the loop
>   survives. The quoted `recvfrom`/`log_and_throw` code below is **unchanged** at
>   `sdp_server.cpp:106`, so this is a behavioural fix upstream of it, not a repair of the pattern;
>   the reproduction section below is now historical (2025.10.0).
> - **Trigger 3 (refused TLS handshake) still reproduces — 3/3.**
>   `Shutdown loop() because of: Failed to SSL_accept(): … tls_process_client_certificate:certificate
>   verify failed`, and the zombie state was observed directly afterwards: the SDP socket stays bound
>   and answers nothing (a multicast probe times out), nothing restarts the module.
> - The asymmetry is now the cleanest form of the argument: an `SSL_read_ex` failure on an
>   **established** session is scoped to the session ("Shutting down session … closed gracefully",
>   loop survives — observed) while the same class of error inside `SSL_accept` ends `loop()`.
> - Trigger 2 (TLS key logging) was not retried (its 2025.10 sighting was qemu-specific).
>
> The `tbd_controller.cpp` line numbers below now read 50-56 (catch/break at :54); everything else
> in the code quotes is current.

---

**Title:** libiso15118: an error in any poll handler ends `TbdController::loop()` while the listening
sockets stay bound

**Version:** everest-core 2025.10.0 (`ghcr.io/everest/everest-demo/manager:2025.10.0-patches`);
libiso15118 as vendored there, `c641ffcbe22a7b4b635dc82d6df70767460e685d`

## Summary

`TbdController::loop()` wraps `poll_manager.poll()` in a `try`/`catch` whose handler is `break`:

```cpp
// src/iso15118/tbd_controller.cpp:50-55
try {
    poll_manager.poll(poll_timeout_ms);
} catch (const std::runtime_error& e) {
    logf_error("Shutdown loop() because of: %s", e.what());
    break;
}
```

Every registered poll handler runs inside that call, so a failure in any one of them ends the whole
V2G loop. `TbdController` itself is not destroyed, so the SDP socket and the TCP listener **stay
bound**: the charger goes on completing TCP handshakes and never answers a V2G message again. To a
client that is indistinguishable from a hung peer, and to a process supervisor the module still looks
healthy, so nothing restarts it.

The same function already contains the pattern we would expect one level up — the session poll
catches, logs, calls `session->close()` and carries on:

```cpp
// src/iso15118/tbd_controller.cpp:59-66
try {
    const auto next_session_event = session->poll();
    …
} catch (const std::runtime_error& e) {
    logf_error("Shutting down session because of: %s", e.what());
    logf_info("Restarting session ...");
    session->close();
}
```

## Three handlers we reached it from

All three are poll handlers, and all three throw `std::runtime_error` via `log_and_throw`
(`src/iso15118/misc/helper.cpp:16`, which appends `strerror(errno)`).

| # | Throw site | Reached from | Observed message |
|---|---|---|---|
| 1 | `io/sdp_server.cpp:106`, `SdpServer::get_peer_request()` | `handle_sdp_server_input()` (`tbd_controller.cpp:140`), registered at `tbd_controller.cpp:33` | `Read on sdp server socket failed (reason: Resource temporarily unavailable)` |
| 2 | `io/sdp_server.cpp:202`, `TlsKeyLoggingServer` ctor | constructed in the accept path, `io/connection_ssl.cpp:455` | `Could not set interface name:eth0 (reason: Protocol not available)` |
| 3 | `io/connection_ssl.cpp:480`, `ConnectionSSL::handle_data()` | registered at `io/connection_ssl.cpp:459` | `Failed to SSL_accept(): … peer did not return a certificate` |

### 1 is the clearest, and is a bug on its own

```cpp
// src/iso15118/io/sdp_server.cpp:104-107
const auto read_result = recvfrom(fd, udp_buffer, sizeof(udp_buffer), 0, …);
if (read_result <= 0) {
    log_and_throw("Read on sdp server socket failed");
}
```

`Resource temporarily unavailable` is `EAGAIN`/`EWOULDBLOCK`, which on a non-blocking socket is a
normal return and not an error condition. `read_result == 0` — a zero-length datagram — is likewise
treated as fatal. Either ends the module's entire V2G loop.

Note that the TLS path already distinguishes these cases correctly, at
`io/connection_ssl.cpp:477-479`: `SSL_ERROR_WANT_READ` / `WANT_WRITE` simply `return`. The SDP read
has no equivalent.

### 2 and 3 are per-connection failures

Both happen while handling one client. Neither seems like a reason to stop serving every future
client — `2` in particular is a socket-option failure in an optional diagnostic feature
(`enable_tls_key_logging`), and it takes the charger down with it.

## How we ran into it

Driving a third-party ISO 15118 EVCC against `Evse15118D20` and `IsoMux`, over TCP and over TLS 1.3.
Happy-path sessions are fine — we completed ISO 15118-2 and -20 DC charges against this same build —
so this is off-path robustness, not something that breaks your demo. It cost us about an hour the
first time, because a charger that accepts TCP and answers nothing does not look like a crash.

## Reproduction (trigger 1, the cheapest)

1. Start `config-sil-dc-d20.yaml`. (We ran it with `device: eth0` and
   `tls_negotiation_strategy: ENFORCE_NO_TLS`; the module also needs a V2G certificate present, else
   it aborts at startup — we copied `tests/ocpp_tests/test_sets/everest-aux/certs/` into
   `/ext/dist/etc/everest/certs/`.)
2. Send a normal **multicast** SDP request so the module opens its TCP port, and note the port:
   ```
   printf '\x01\xfe\x90\x00\x00\x00\x00\x02\x10\x00' \
     | socat -T3 - UDP6-DATAGRAM:[ff02::1%eth0]:15118,bind=:15119
   ```
3. Send a second SDP request **unicast** to the charger's own link-local address:
   ```
   printf '\x01\xfe\x90\x00\x00\x00\x00\x02\x10\x00' \
     | socat -T3 - UDP6-DATAGRAM:[<charger-ll-addr>%eth0]:15118
   ```
4. It is answered, and ~20 ms later the log shows `Shutdown loop() because of: Read on sdp server
   socket failed (reason: Resource temporarily unavailable)`. TCP connections to the V2G port still
   complete; no V2G message is ever answered again.

Reproduced twice, with security byte `0x00` and `0x10`, so it is unrelated to the TLS flag. The same
request sent multicast does not trigger it — which is why ordinary EVCC behaviour never hits this.

## Suggested direction

Two separable things, and we would happily send a PR for the first if you agree with the shape:

1. **`sdp_server.cpp:106`** — treat `EAGAIN`/`EWOULDBLOCK` as "no datagram, return" rather than as an
   error, and decide deliberately what a zero-length datagram should do.
2. **`tbd_controller.cpp:52`** — scope handler failures to the handler. If ending `loop()` is
   sometimes right, closing the listening sockets on the way out would at least make the failure
   visible to whatever supervises the process, instead of leaving a socket that accepts and never
   answers.

---

## Before sending

- [x] ~~Reproduce step 1–4 yourself once.~~ Done 2026-08-05 on 2026.02.1: trigger 1 does **not**
      reproduce any more, trigger 3 does (3/3). **Rewrite the summary around trigger 3** (a refused
      TLS handshake in `SSL_accept` ends `loop()`; sockets stay bound) and present the unicast-SDP
      case as "fixed in 2026.02.1, underlying pattern unchanged at `sdp_server.cpp:106`".
- [ ] File **one** issue, this one. The other observations from these runs (`IsoMux` not reading
      `Priority`, -20 PnC being commented out in `Evse15118D20`) are deliberately not in here: the
      first rests on a requirement we cannot cite, the second is their own documented TODO.
- [ ] Post under your own name, in your own words. The "how we ran into it" paragraph matters —
      it tells a maintainer whether the scenario is realistic.
- [ ] Offer the PR for item 1 only after they have said whether they want it.
