# Draft report to EVerest — one exception from any poll callback ends `Evse15118D20`'s V2G loop, permanently and silently

Status: **draft, not sent.** The failure mode was **measured** on everest-core 2026.02.1, three times,
one attempt each — including against their own shipped `config-sil-dc-d20.yaml`, unmodified. The claim
that it survives on `main` is a **source reading**, not a run, and this report keeps those apart
everywhere. Post it under your own name; see *Before sending* at the bottom.

> **Re-pitched 2026-08-11, and the change matters.** This report used to lead with *"a failed TLS
> handshake ends the loop"*. **That trigger is fixed on `main`** — see *What you have already fixed*
> — and an issue opened on it today would be closed as stale within a day, taking the real finding
> with it. What is unchanged is the **mechanism**: a throw out of any poll callback ends
> `TbdController::loop()` for the life of the process, and at least eight throw sites still reach it.
> Details in the [audit notes](../interop-runs/2026-08-11-reports-upstream-audit/notes.md).

Evidence in this repository: [`trigger3-tls-accept-shutdown.log`](../interop-runs/2026-08-05-everest-2026021-matrix/trigger3-tls-accept-shutdown.log)
(the three reproductions plus the contrast case, filtered from the station logs), and the run notes
[`2026-08-05-everest-2026021-matrix`](../interop-runs/2026-08-05-everest-2026021-matrix/notes.md).
The original 2025.10.0 sightings are in
[`2026-08-03-everest-iso20-dc-tls13`](../interop-runs/2026-08-03-everest-iso20-dc-tls13/notes.md) and
[`2026-08-03-everest-iso20-dc-full-charge`](../interop-runs/2026-08-03-everest-iso20-dc-full-charge/notes.md).

Two other reports for the same project, unrelated to this one, are in
[`everest-isomux.md`](everest-isomux.md) (four findings in that one module, merged) and
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md). **File them separately.**
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md) and
[`everest-d20-ac-namespace.md`](everest-d20-ac-namespace.md) are **withdrawn** — both fixed on `main`
before they were ever sent.

A third is **not** unrelated: [`everest-d20-client-auth.md`](everest-d20-client-auth.md) hit this
defect again on 2026.02.1 as a side effect on 2026-08-10 — its control arm is a TLS 1.3 `s_client`
with no client certificate, the station refuses it correctly, and the accept loop dies with it. That
is why every arm of that run needed a fresh station. It is an independent sighting of the *old*
trigger, so it dates the exposure rather than proving the current one.

## Before the defect — what everest-core has been worth to us

Not politeness, and worth a paragraph because it is the reason this report exists at all rather than a
bug filed by a stranger.

**everest-core has found more defects in this project than every other counterparty combined**, across
thirty-four runs. Almost all of them share one shape: a value we had taken from our own assumption where
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

**Title:** `Evse15118D20`: an exception thrown from any `PollManager` callback ends
`TbdController::loop()` for the life of the process — the SDP socket stays bound, the charger answers
nothing, and the process stays healthy

**Version:** the failure mode measured on everest-core **2026.02.1** (`b61bb12b8`), libiso15118
**v0.9.1** as vendored at `lib/everest/iso15118`, OpenSSL 3.5.6, Debian 13; also present in 2025.10.0.
The mechanism and the remaining trigger sites read from `main` (`ebcd36d`, 2026-08-11) — **source
only**, not re-run.

## Summary

`TbdController::loop()` runs one `poll()` over every registered fd and dispatches callbacks. If any
callback throws, the loop **breaks**:

```cpp
// src/iso15118/tbd_controller.cpp:71-82 (main)
bool TbdController::poll_once() {
    const auto poll_timeout_ms = get_timeout_ms_until(next_event, POLL_MANAGER_TIMEOUT_MS);
    try {
        poll_manager.poll(poll_timeout_ms);
    } catch (const std::runtime_error& e) {
        logf_error("Shutting down poll loop because of: %s", e.what());
        return false;                       // ← and the caller breaks
    }
    return true;
}
```

```cpp
// src/iso15118/tbd_controller.cpp:128-135 (main)
while (session or not shutdown_active.load()) {
    if (not poll_once()) {
        if (session) { session->close(); session.reset(); }
        break;
    }
    tick();
}
```

`TbdController` is not destroyed, so **the SDP socket stays bound and is never read again**. Since
`Evse15118D20` creates its TCP server only in response to an SDP request, and no SDP request is ever
processed again, the charger can no longer be reached at all — by any EV, over TLS or plaintext. The
module process and the manager both stay alive, so nothing supervising them notices.

**That combination is the defect**: not one bad input, but a design in which any unexpected condition
on any watched descriptor is fatal to the service and invisible to everything outside it.

## The measurement — 2026.02.1, and what it proves

The trigger we used is fixed. The *consequence* it demonstrates is the part that still applies, and it
is worth having measured rather than argued: after one failed handshake,

```
--- SDP after: NO ANSWER                     (multicast, security=00 — TLS)
--- SDP after: NO ANSWER                     (multicast, security=10 — plain TCP)
UNCONN 960 0 *%eth0:15118 users:(("iso15118_charge",pid=21969,fd=10))
module alive: 1; manager alive: 1
```

`Recv-Q 960` is the SDP requests queueing up unread in the kernel buffer: the socket is bound, the
reader is gone. The process is up, the manager is up, and the charger is dead. **No supervisor built
on process liveness can see this**, which is why the severity does not follow from how exotic the
trigger is.

Three reproductions, one attempt each: the stock config as shipped; and with `ENFORCE_TLS` +
`enforce_tls_1_3`, once with no client certificate and once with an untrusted self-signed one. We
first hit it on 2025.10.0 while driving a third-party EVCC against `Evse15118D20` — a real car with a
client-certificate chain your OpenSSL could not build, not a fuzzer.

## What you have already fixed — and why that is the argument, not a retraction

Two instances of this pattern are closed on `main`, both after this report was written and neither
because of it. They are the reason we think the remaining ones are worth an issue rather than a shrug.

**1. The TLS handshake** — the trigger this report used to lead with:

```cpp
// src/iso15118/io/connection_ssl.cpp:264-270 (main)
case tls::Connection::result_t::closed:
    // convert() folds all fatal handshake outcomes (peer close, alert,
    // protocol error) into closed, so this teardown handles every
    // non-success, non-blocking handshake result.
    logf_error("TLS handshake failed: connection closed");
    this->close();
    return;
```

against 2026.02.1's `log_and_raise_openssl_error("Failed to SSL_accept(): …")`. **This is a better fix
than the one this report originally proposed**: it covers the whole class of fatal handshake outcomes
rather than scoping the one call, and the comment says so.

**2. A malformed SDP datagram:**

```cpp
// src/iso15118/io/sdp_server.cpp:142-146 (main)
if (parse_sdp_result != V2GTP_ERROR__NO_ERROR) {
    // FIXME (aw): we should not die here immediately
    logf_warning("Sdp server received an unexpected payload");
    return PeerRequestContext{false};
}
```

**That `FIXME` is this entire report in your own words**, written next to a fix that applies it once.
The question we are raising is why it stops there — two earlier exits from the same function, on the
same datagram, still do exactly what the comment warns against.

## Where it is still reachable on `main`

Read from `main` (`ebcd36d`) on 2026-08-11; **not run**. Every site below is inside a function that is,
or is called from, a callback registered with `PollManager`, so a throw from it reaches `poll_once()`
and ends the loop.

| # | site on `main` | condition | registered at |
|---|---|---|---|
| 1 | `sdp_server.cpp:125` | `recvfrom()` returns `<= 0` — including a **zero-length datagram** | `tbd_controller.cpp:60` |
| 2 | `sdp_server.cpp:129` | `peer_addr_len > sizeof(peer_address)` | as above |
| 3 | `connection_ssl.cpp:219` | `::accept()` returns `< 0` | `connection_ssl.cpp:143` |
| 4 | `connection_ssl.cpp:245` | `wrap_accepted_fd()` returns `nullptr` | as above |
| 5 | `connection_ssl.cpp:278` | the `default:` arm of the handshake switch | `connection_ssl.cpp:249` |
| 6 | `connection_plain.cpp:118` | `accept4()` fails | `connection_plain.cpp:34` |
| 7 | `connection_plain.cpp:128` | the peer address will not render as a string | as above |
| 8 | `sdp_server.cpp:179-230` | six throw sites in the `TlsKeyLoggingServer` constructor, built from `connection_ssl.cpp:96` when `enable_tls_key_logging` is set — i.e. **in the accept path** | as above |

Two of them deserve a sentence each.

**Site 1 is the one we would look at first.** It is in **the same function** as the `FIXME` fix quoted
earlier — seventeen lines above it, on the same datagram, in the same read. `handle_sdp_server_input()`
(`tbd_controller.cpp:320-321`) calls `get_peer_request()` with no `try`, and the only three `catch`
blocks in that file are `poll_once()`'s, the per-session one, and the connection-factory one. So a
read failure on the SDP socket still ends the loop, and it is the trigger we saw on 2025.10.0
(`EAGAIN` on a non-blocking socket). We could **not** reproduce it on 2026.02.1 — 0 of 2 attempts,
idle and mid-session, both security bytes — so we are **not** claiming it is currently reachable from
outside. We are saying the guard is missing where the neighbouring one was added.

**Site 8 is the `enable_tls_key_logging` trigger** we saw on 2025.10.0
(`Could not set interface name:eth0 (reason: Protocol not available)`). That run was under
`qemu-x86_64`, where `SO_BINDTODEVICE` may simply not be implemented, so it is a weak trigger — but
the constructor still throws from inside the accept path on `main`, so it is the same shape, and a
config flag should not be able to end the loop.

## The asymmetry, in your own code

The project already distinguishes "this connection is finished" from "this service is finished" —
one level up, in the same file:

```cpp
// src/iso15118/tbd_controller.cpp:100-113 (main)
if (session) {
    try {
        const auto next_session_event = session->poll();
        next_event = std::min(next_event, next_session_event);
    } catch (const std::runtime_error& e) {
        logf_error("Shutting down session because of: %s", e.what());
        logf_info("Restarting session ...");
        session->close();
    }
    …
}
```

An established session that dies is closed and the station carries on. A callback that dies takes the
station with it. The two are ten lines apart, and the second is the one every *new* peer arrives
through — which is the wrong way round, because an unauthenticated stranger reaches the poll path and
only an accepted peer reaches the session path.

Observed on 2026.02.1, same build, minutes apart:

| what happened | their log | loop afterwards |
|---|---|---|
| valid mutual handshake, client disconnects mid-session | `Shutting down session because of: Failed to SSL_read_ex(): 6` → `Closing TLS connection` → `TLS connection closed gracefully` | **alive** — SDP answers, port 50000 served again |
| handshake fails during `SSL_accept` | `Shutdown loop() because of: Failed to SSL_accept(): 1: …` | **gone** — SDP silent, socket still bound |

The right-hand trigger is fixed; the left-hand behaviour is what we are asking for on the other path
too.

## A second contrast, from a module of your own

`IsoMux` was given the same treatment on 2026-08-06 — a TLS 1.3 hello it refuses (it serves the -2
profile, so it answers `alert 70`), then a second refused handshake from `openssl` — and **it kept
accepting**: the TLS 1.2 probe afterwards completed normally, and two full ISO 15118-20 sessions ran
to `SessionStop` in the same process ([run notes](../interop-runs/2026-08-06-everest-isomux-tls/notes.md)).

It is a different TLS termination (`lib/everest/tls`), so this is not a fix to copy across. What it
gives is a second data point in the same tree: **the project's other TLS-terminating server survives
what this one died on**, and nothing about the deployment makes surviving it impractical.

## Reproduction

**On 2026.02.1** — one command, no ISO 15118 stack, their shipped configuration unmodified. This
reproduces the *consequence* and a trigger that is now fixed; it is here because it is what we ran.

1. Start `config/config-sil-dc-d20.yaml` as it ships. (The module needs a V2G certificate present or
   it aborts at startup; we copied `tests/ocpp_tests/test_sets/everest-aux/certs/` into
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
4. Repeat step 2. **No answer**, for either security byte, for as long as the process runs.

**On `main`** — we have not done this, and it is the honest gap in the report. What would close it is
a build of `main` plus a trigger for any row of the table above; the cheapest is probably site 3 or 6,
by having `accept()` fail (an fd-limit squeeze), since neither needs TLS or a certificate. If you
would rather we ran it before you look at the issue, say so and we will.

## Suggested direction

We would happily send a PR, if you agree with the shape — but the choice here is a design call and it
is yours:

1. **Catch per callback, not per loop.** `PollManager::poll()` already knows which fd it is
   dispatching; wrapping the dispatch in a `try` and unregistering (or closing) only the offending
   descriptor would generalise both fixes you have already made, and would have made them unnecessary.
2. **Or keep the loop-level catch and make it visible.** If ending `loop()` is ever the right answer,
   closing the listening sockets on the way out would at least let a supervisor see a dead module
   instead of a healthy process that answers nothing. As it stands the failure mode is silent by
   construction, and that is the half we would argue hardest for.
3. **Or, minimally, apply the `FIXME` to its own function.** Sites 1 and 2 are six lines from a fix
   that says what to do.

(1) and (2) are not alternatives — (2) is worth having even with (1), for whatever the next
unanticipated throw turns out to be.

---

## Before sending

- [x] **Reproduce the consequence yourself.** Done 2026-08-05 on 2026.02.1: 3/3, including their
      unmodified stock config, plus the contrast case showing the session path handling the same error
      class correctly.
- [x] **Check whether it is already fixed upstream — done 2026-08-11, and partly it is.** Two
      instances closed on `main`, the mechanism untouched, eight sites still reaching it. The report
      was re-pitched around that rather than filed as it stood; see the box at the top. (The standalone
      `EVerest/libiso15118` still shows the old code and is irrelevant — that repository is not
      maintained.)
- [x] **Re-check every line reference against the tree.** The 2026.02.1 references were checked on
      2026-08-07 and again on 2026-08-11 in the sweep over every `file:line` in this directory. **The
      `main` references in *Where it is still reachable* were read on 2026-08-11 and have not been
      re-read since** — `main` moves daily, so re-read them on the day you post, and note that they are
      `main` line numbers while the measurement quotes 2026.02.1.
- [x] **Lead with what the project has been worth to us.** Above, and every claim in it has runs
      behind it. A report that opens with "your charger can be bricked" reads differently when the
      sender has been on the receiving end of the same courtesy two dozen times.
- [ ] **Say plainly which half is measured and which is read.** The consequence is measured on
      2026.02.1; that it survives on `main` is a source reading. This draft keeps them apart in every
      section and the issue must too — blurring them is the fastest way to have a real finding
      dismissed, and it nearly happened to a sibling report in this directory.
- [ ] **Do not lead with the TLS handshake.** It is fixed. Leading with it invites *"already fixed on
      main"* as the first and last reply. Lead with the loop, and cite their own two fixes as agreement
      rather than as a correction.
- [ ] **Quote their `FIXME` early, and generously.** *"we should not die here immediately"* is the
      whole argument, it is theirs, and it is sitting in the file. Ask why it stops at one call site —
      do not assert that it should not have.
- [ ] **Do not overstate site 1.** It did not reproduce on 2026.02.1, 0 of 2. The claim is that the
      guard is missing, not that a stranger can currently fire it.
- [ ] **File one issue, this one.** The other observations from these runs are deliberately not in
      here: -20 PnC being commented out (their own documented TODO) and their SECC sending only its
      leaf (arguably deployment), both written up in the run notes and able to go separately.
- [ ] **Post under your own name, in your own words.** Keep a sentence on how it was hit in practice —
      a third-party EVCC against `Evse15118D20`, not a fuzzer — because that tells a maintainer whether
      the scenario is realistic. It is the difference between "hardening" and "a car in the field can
      brick this charger until it is restarted".
- [ ] **Offer the PR only after they have said whether they want it.** (1) is a change to
      `PollManager`'s contract and touches every caller; that is not ours to decide.
