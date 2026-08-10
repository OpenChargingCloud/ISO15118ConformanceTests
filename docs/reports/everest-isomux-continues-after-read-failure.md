# Draft report to EVerest — `IsoMux` logs that the V2GTP read failed, then routes on the buffer anyway

Status: **draft, not sent.** Reproduced deliberately 2026-08-10 against everest-core **2026.02.1**
(`b61bb12b8`) built from source, with a control connection that differs by two bytes. First seen in a
station log on 2026-08-03, twice, where it read as noise. Post it under your own name; see
*Before sending* at the bottom.

Evidence in this repository:
[`2026-08-10-everest-isomux-shortread`](../interop-runs/2026-08-10-everest-isomux-shortread/notes.md) —
the run notes, with [`probe-a-b.log`](../interop-runs/2026-08-10-everest-isomux-shortread/probe-a-b.log)
(the two connections side by side) and
[`their-charger.log`](../interop-runs/2026-08-10-everest-isomux-shortread/their-charger.log). The
accidental first sighting is in
[`2026-08-03-everest-isomux-both`](../interop-runs/2026-08-03-everest-isomux-both/notes.md)'s station
log.

Six other reports for the same project are in
[`everest-loop-shutdown.md`](everest-loop-shutdown.md),
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md),
[`everest-isomux-iso20-over-tls12.md`](everest-isomux-iso20-over-tls12.md) and
[`everest-isomux-sap-priority.md`](everest-isomux-sap-priority.md) — **the last two are the same module
and the same function family, and all three `IsoMux` reports could reasonably be one issue if you
prefer** —
[`everest-evsev2g-session-log-responses.md`](everest-evsev2g-session-log-responses.md) and
[`everest-evse-security-ocsp-dropped.md`](everest-evse-security-ocsp-dropped.md), plus
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) and one to your fork of Josev's
certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)). The framing in
`everest-loop-shutdown.md` — what everest-core has been worth to this project, and why a report from us
is not a bug filed by a stranger — applies here unchanged and is not repeated.

---

**Title:** `IsoMux` `v2g_detect_iso20_support()`: the return value of `v2g_incoming_v2gtp()` is logged
but not acted on, so a failed V2GTP header read still produces a backend routing decision — and the
retry condition tests the one value that means the peer has closed

**Version:** everest-core **2026.02.1** (`b61bb12b8`), native build, Debian 13. Module `IsoMux` in front
of `EvseV2G` and `Evse15118D20`, `config-sil-dc-isomux`-shaped config, plain TCP.

## Summary

`v2g_detect_iso20_support()` is where the multiplexer decides which backend a connection belongs to. It
reads the first V2GTP message, sniffs the `SupportedAppProtocolReq`, and sets `iso20`. When the read
fails it says so — and then sniffs, decides and proxies exactly as if it had not.

Two connections, six seconds apart, one station, differing only in how many bytes of the header they
sent.

**A — a complete 8-byte header, payload length 0.** No transport error; the EXI decode then fails
because there is no body, which is correct:

```
Incoming connection on eth0
Handling SupportedAppProtocolReq
decode_appHandExiDocument() failed
Connected to proxy module for ISO-2/DIN
Multiplexer: Proxy TCP->TCP
```

**B — six bytes of the same header.**

```
Incoming connection on eth0
connection_read(header) too short: expected 8, got 6      ← the transport says no
v2g_incoming_v2gtp() failed                               ← and the caller agrees
Handling SupportedAppProtocolReq                          ← and then it carries on
decode_appHandExiDocument() failed
Connected to proxy module for ISO-2/DIN
Multiplexer: Proxy TCP->TCP
```

The last three lines are the report. Having announced that it could not read the message, the station
decoded the buffer, concluded from that decode that the peer does not speak ISO 15118-20, and proxied
the connection to the `-2` backend — which met the same bytes and closed.

## Where it comes from

`modules/EVSE/IsoMux/v2g_server.cpp:145-179`:

```cpp
rv = v2g_incoming_v2gtp(conn);

if (rv != 0) {
    dlog(DLOG_LEVEL_ERROR, "v2g_incoming_v2gtp() failed");   // logged, and that is all
}

if (conn->ctx->is_connection_terminated == true) {
    rv = -1;
}

bool iso20 = false;
app_protocol_received = v2g_sniff_apphandshake(conn, iso20);   // :172, runs regardless
…
} while ((rv == 1) && not app_protocol_received);              // :178
```

Two problems, and the second is why the first is easy to miss.

1. **The error is logged, not acted on.** `rv != 0` covers a short read (`:48-51`), an invalid header
   (`:53-56`), an oversized payload, and the peer closing. All four continue to `:172`.
2. **The retry condition tests the wrong value.** `rv == 1` is what `v2g_incoming_v2gtp()` returns when
   the **peer closed the connection** — `:45-47`, `if (rv == 0) return 1;` under the comment *"peer
   closed connection"*. The loop therefore retries only when there is nobody left to read from. Its doc
   comment at `:30` says the function returns *"0 … otherwise -1"* and does not mention 1, which may be
   how the condition came to be written.

`EvseV2G`, which this was forked from, does both correctly — `v2g_server.cpp:387-391`:

```cpp
rv = v2g_incoming_v2gtp(conn);

if (rv != 0) {
    dlog(DLOG_LEVEL_ERROR, "v2g_incoming_v2gtp() failed");
    goto error_out;
}
```

and handles the peer-closed case by name in its own loop (`:473-477`, `if (rv == 1) … break;`). We
mention that not to score a point but because it settles what the intended behaviour is: the two
modules were once the same code and one of them kept the exit.

## Why we think it is worth fixing

**Because `iso20` is the multiplexer's whole job**, and after a failed read it is decided from a buffer
that was never filled. In this build the surrounding setup zeroes `payload_len` and sizes the EXI
stream at 0 two lines earlier, so the decode fails and the answer comes out `false` — but that is an
accident of the setup code, not a property anyone stated. The routing decision is being taken from
undefined input.

**Because the outcome is silently wrong rather than loudly wrong.** Every unreadable first message
becomes an ISO 15118-2 session. A `-20`-only EV whose first message is truncated — a retransmission
that missed the sequence timeout, a middlebox, a half-open connection — is handed to a backend that
cannot serve it, instead of being dropped where the fault happened. The `-2` backend then fails on the
same bytes, so the log blames the module that did nothing wrong.

**And because of what the log looks like.** The station reports that it failed to read the message and,
in the next line, reports handling that message. That combination is why this sat in one of our
recorded logs for a week reading as probe noise.

## Suggested direction

1. **Exit on the error**, as `EvseV2G` does — `return false` (or break to the same place
   `is_connection_terminated` leads) when `rv != 0`. One line, and it makes the behaviour match the
   module this was copied from.
2. **Fix the loop condition.** If the intent is *"read again until an app-protocol message arrives"*,
   the test wants `rv == 0`; `rv == 1` is the peer-closed case and should end the loop, not repeat it.
   Worth deciding deliberately, since with (1) in place the loop can only ever run once as written.
3. **Correct the doc comment at `:30`** while you are there — it does not mention the `1` return that
   `:45-47` produces and that `:178` depends on.

## Not part of this

The same connection path has two cosmetic problems we noticed and did not chase: `connection_read(header)
failed: Success` when the read returns `-1` without setting `errno` (the message formats `strerror(errno)`
unconditionally), and the peer address in *"Incoming connection on eth0 from
`[a00:deb2:0:0:fe80::]:57010`"*, which does not look like the link-local address the connection actually
came from. Neither affects behaviour; both would make the next report of this kind easier to write.

---

## Before sending

- [x] **Reproduce it deliberately, with a control.** Two `socat` connections, six seconds apart,
      differing by two bytes; the control shows what the same station does when the read succeeds. No
      EV, no car simulation, no authorization — this is a two-line reproduction a maintainer can run in
      a minute.
- [x] **Check every line reference against the tree.** `IsoMux/v2g_server.cpp:30`, `:32-40`, `:45-47`,
      `:48-51`, `:53-56`, `:145-179`, `:172`, `:178`; `EvseV2G/v2g_server.cpp:387-391`, `:473-477`;
      `EvseV2G/connection/connection.cpp:265-300` — read from the built 2026.02.1 source on 2026-08-10.
- [x] **Establish that a short read is not a split TCP segment.** Their `connection_read()` loops until
      the byte count is satisfied or the sequence timeout expires, so a header split across segments is
      reassembled; "got 6" means the peer sent six bytes and then stopped.
- [ ] **Decide whether this is its own issue or joins the other two `IsoMux` reports.** Same module,
      same area, three findings; a maintainer may prefer one issue with three headings.
- [ ] **Lead with the two log lines, not with the code.** *"failed to read the message"* followed by
      *"handling the message"* is the whole report in two lines, and it is what makes a reader care.
- [ ] **Say what we did not establish.** We did not find a peer that produces a short header in normal
      operation; the 2026-08-03 sighting has no known cause. The defect is in the response to it, not
      in how often it happens — say so plainly rather than implying a frequency.
- [ ] **File one issue, this one.**
- [ ] **Post under your own name, in your own words.**
