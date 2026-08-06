# Draft report to IoT.bzh (tux-evse) — one connection that closes makes the binder log 200,000 lines a second

Status: **draft, not sent.** Reproduced 2026-08-06 on `iso15118-simulator-rs` **`main` @ `fc51088`**
built from source, with their own shipped scenario and no third-party stack in the loop. Post under
your own name; see *Before sending* at the bottom.

This is the third finding from these runs; the two about TLS are in
[`tux-evse-tls.md`](tux-evse-tls.md) and are unrelated to this one.

Evidence: [`spin-repro.sh`](../interop-runs/2026-08-06-tux-head-reverse/spin-repro.sh) (the whole
reproduction, 40 lines) and [`spin-repro.log`](../interop-runs/2026-08-06-tux-head-reverse/spin-repro.log)
(measurements plus the repeated line verbatim).

---

**Title:** A peer that disconnects (or simply pauses) sends `afb-evse` into an unbounded log loop —
365 MB in 11 s — and the socket is never closed

**Version:** `iso15118-simulator-rs` `main` (`fc51088`), `iso15118-network-rs` `f1ab338`,
`iso15118-encoders-rs` `fe6c0aa`, `afb-binder` from `redpesk-core` HEAD, Debian 13, x86-64 native
(no container, no qemu).

## Summary

Start the responder as your README does, connect once, send a single well-formed
`SupportedAppProtocolReq`, let it be answered, and close the socket. Measured:

```
sent one SupportedAppProtocolReq; got 12 byte(s) back; socket closed.
log lines: at-start=1745   at-close=332557   after-10s-idle=2485606
written during 10 s with NO peer connected: 2153049 lines, 365M total
```

~215,000 lines per second, of one line:

```
NOTICE: [API libafb_sim15118_evse.so] tcp-client:iso2-exi-decode:fail to decode iso-2
        (ExiDocument) from stream   file: exi-15118/src/net-exi.rs:344
```

It does not stop by itself. And the connection is never reaped:

```
State       Recv-Q  Local Address:Port                            Peer Address:Port
CLOSE-WAIT  0       [fe80::78be:f0ff:fe18:d1e6]%evcc-veth:61341   [fe80::24fd:6ff:fef3:807c]:50376
```

Note that the 332,557 lines were already written **while the connection was still open and merely
idle** — the client sent nothing further for four seconds. So a disconnect is not required to enter
the state; a pause is enough. The disconnect is what makes it permanent.

## Why we think it is worth fixing

- **An unattended binder fills a disk in minutes**, and 200k log writes per second is a busy core
  doing nothing. Our runs produced 2.1 GB, 1.3 GB and 950 MB logs before we noticed and started
  capping every binder with `timeout`.
- **The trigger is ordinary.** Any peer that pauses between messages, times out, or hangs up — a car
  unplugged mid-session, a test client that finishes, a crashed EVCC — reaches it. We first met it
  three different ways before isolating this one.
- **A wedged binder does not answer SIGTERM.** `pkill afb-evse` leaves it running; only `SIGKILL`
  stops it. Combined with the leaked `CLOSE-WAIT` socket and the still-bound ports, a stuck instance
  blocks the next run of your own test suite until someone notices.

One rig note that may save you a minute: the binder renames its process to whatever `--name` says, so
it is `afb-evse` / `afb-evcc` in `ps`, not `afb-binder`.

## Where we would look

We are reporting an observation, not a diagnosis — but three things in the source line up with it, and
they may save you the first hour:

1. **EOF is not distinguishable from "no data".** `TcpConnection::get_data` returns `Ok(count)`
   straight from `read()` (`iso15118-network-rs/src/ipv6-tcp.rs:66-75`), so a closed peer arrives as
   `Ok(0)` — the same value as an empty read. Nothing in the callers treats 0 as end-of-stream, and
   the fd is never removed from the poll set, which fits both the loop and the `CLOSE-WAIT`.

2. **The EXI stream is reset on the *send* path only** — `self.stream.reset(&mut lock)` appears at
   `exi-15118/src/net-exi.rs:141` and `:159`, both inside `send_exi_stream` / `send_exi_message`.
   `decode_from_stream` never resets. In the normal flow that is invisible, because every received
   message is followed by a sent one.

3. **The receive path's early returns leave the buffer as it is.** In
   `afb-evcc/src/controller.rs:337-350`, a message whose id does not match `pending` is logged and
   `return Ok(())` — no reset. Our other two sightings logged
   `unexpected exi message expected:Iso2(AuthorizationRes) got:Iso2(AuthorizationReq)`, i.e. their own
   last *outbound* request being decoded again from the shared buffer, which is what made us look here.

If (1) is the root cause, handling `Ok(0)` as EOF — close the connection, drop the fd from the poll
set — would end all of it, including the `CLOSE-WAIT` leak, without touching the stream logic.

## Suggested fix

```rust
// iso15118-network-rs/src/ipv6-tcp.rs — get_data(), or its callers
let count = match data_set.connection.read(buffer) {
    Ok(0)     => return Err(/* or a dedicated IsoStreamStatus::Closed */),  // peer hung up
    Ok(count) => count as u32,
    Err(_)    => return afb_error!("sock-client-read", …),
};
```

…and, whatever the caller does with that, close the socket so it leaves `CLOSE-WAIT`. A rate limit on
the decode-failure log would be a reasonable belt-and-braces addition, but on its own it would turn a
loud spin into a silent one.

## How to reproduce

[`spin-repro.sh`](../interop-runs/2026-08-06-tux-head-reverse/spin-repro.sh) does it end to end; the
whole of it is:

```bash
# their responder, their shipped scenario (autorun added so it runs headless)
IFACE_SIMU=… SIMULATION_MODE=responder SCENARIO_UID=evse \
  afb-binder --name afb-evse --config=binding-simu15118-evse-no-tls.yaml --config=audi-autorun.json

# one connection: a single SupportedAppProtocolReq, then close
printf '\x01\xfe\x80\x01\x00\x00\x00\x24\x80\x00\xeb\xab\x93\x71\xd3\x4b\x9b\x79\xd1\x89\xa9\x89\x89\xc1\xd1\x91\xd1\x91\x81\x89\x99\xd2\x6b\x9b\x3a\x23\x2b\x30\x02\x00\x00\x04\x00\x40' \
  | timeout 4 socat - 'TCP6:[<their link-local>%<your iface>]:61341'

# then just watch
wc -l <their log>; sleep 10; wc -l <their log>
```

The scenario file is irrelevant to the loop — only the first exchange happens before it starts.

---

## Before sending

- [x] **Reproduce it yourself.** Done, with nothing of ours involved: their binder, their scenario,
      `socat` as the peer. The first three sightings were incidental to interop runs; this one is
      deliberate and minimal.
- [ ] **File it separately from the TLS issues.** Different subsystem, different fix, and it is the one
      a maintainer can confirm in two minutes.
- [ ] **Lead with the reproduction, not the byte counts.** "One connection, one message, disconnect"
      is the part that makes it real; 365 MB is only the consequence.
- [ ] **Offer the `Ok(0)` patch only if they want it.** Whether EOF should surface as an error, a
      dedicated stream status, or a callback-level close is their architecture's call, and it touches
      both the TCP and the TLS connection types.
