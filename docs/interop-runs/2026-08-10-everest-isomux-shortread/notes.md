# 2026-08-10 — `IsoMux` announces that reading the V2GTP header failed, and then carries on anyway

Two connections against the same station, six seconds apart, differing in one byte count. One sends a
complete 8-byte V2GTP header; the other sends six bytes of it. **The station reports the failure and
then behaves identically to the successful case** — decodes the buffer it never read, takes its
protocol-routing decision from the result, and proxies the connection to the ISO 15118-2 backend.

Not a new sighting. It is in [`2026-08-03-everest-isomux-both`](../2026-08-03-everest-isomux-both/notes.md)'s
station log, twice, where it read as noise from a probe. This run makes it deliberate, adds the control
that shows what the error path costs, and names the four lines that cause it.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), source build in WSL2 |
| Their module | `IsoMux` in front of `EvseV2G` + `Evse15118D20`, `config-mux-ours.yaml`, plain TCP |
| Ours | nothing — two `socat` connections and their own log |
| Outcome | **a failed header read does not stop the handshake; the backend choice is made on unread bytes** |
| Artifacts | [`probe-a-b.log`](probe-a-b.log) (the two probes, side by side), [`their-charger.log`](their-charger.log) |
| Filed | [`everest-isomux-continues-after-read-failure.md`](../../reports/everest-isomux-continues-after-read-failure.md) — the twenty-fifth |

## The A/B

Both probes: open TCP to the port SDP announced, send some bytes, stay quiet, close after the read
timeout. The only difference is six bytes versus eight.

**A — a complete header, payload length 0.** Nothing is wrong at the transport layer, so there is no
read error; the EXI decode then fails because there is no body, which is correct and expected:

```
Incoming connection on eth0
Handling SupportedAppProtocolReq
decode_appHandExiDocument() failed
Connected to proxy module for ISO-2/DIN
Multiplexer: Proxy TCP->TCP
```

**B — six bytes of the same header.** The read fails, the station says so twice, and then does exactly
what it did in A:

```
Incoming connection on eth0
connection_read(header) too short: expected 8, got 6      ← the transport says no
v2g_incoming_v2gtp() failed                               ← and the caller agrees
Handling SupportedAppProtocolReq                          ← and then it carries on
decode_appHandExiDocument() failed
Connected to proxy module for ISO-2/DIN
Multiplexer: Proxy TCP->TCP
```

The last three lines are the finding. After announcing that it could not read the message, the station
decoded the buffer anyway, concluded from that decode that the peer does not speak ISO 15118-20, and
routed the connection to the `-2` backend — which then met the same bytes and closed
(`v2g_handle_connection exited with -1`).

## The four lines

`modules/EVSE/IsoMux/v2g_server.cpp:145-179`, `v2g_detect_iso20_support()`:

```cpp
rv = v2g_incoming_v2gtp(conn);

if (rv != 0) {
    dlog(DLOG_LEVEL_ERROR, "v2g_incoming_v2gtp() failed");   // logged, and that is all
}

if (conn->ctx->is_connection_terminated == true) {
    rv = -1;
}

bool iso20 = false;
app_protocol_received = v2g_sniff_apphandshake(conn, iso20);  // runs regardless
…
} while ((rv == 1) && not app_protocol_received);
```

Two things are wrong in those seven lines, and the second is the one that makes the first hard to see.

1. **The error is logged, not acted on.** `rv != 0` means the header was not read — short read
   (`:48-51`), malformed header (`:53-56`), oversized payload, or the peer closing. Every one of those
   ends in the same place: a decode of `conn->buffer` at `:172` and a routing decision taken from it.
2. **The retry condition is inverted in effect.** `rv == 1` is what `v2g_incoming_v2gtp()` returns when
   **the peer closed the connection** — `IsoMux/v2g_server.cpp:45-47`, `if (rv == 0) return 1;` under
   the comment *"peer closed connection"*. So the loop at `:178` retries **only** when the peer has
   gone, and never in the case a retry is for. (The function's own doc comment at `:30` says it returns
   *"0 … otherwise -1"* and does not mention 1 at all, which is presumably how the condition came to be
   written that way.)

The module this was forked from does it correctly. `EvseV2G/v2g_server.cpp:387-391`, same call, same
variable:

```cpp
rv = v2g_incoming_v2gtp(conn);

if (rv != 0) {
    dlog(DLOG_LEVEL_ERROR, "v2g_incoming_v2gtp() failed");
    goto error_out;
}
```

That is what makes the intent unambiguous rather than a matter of taste: the two modules were the same
code, and one of them kept the `goto`. `EvseV2G` also handles the peer-closed case explicitly and by
name, in its *second* loop (`:473-477`, `if (rv == 1) … break;`) — the case `IsoMux` turned into its
retry condition.

## What it costs

For a peer that sends a short header, nothing much: the session was not going to work anyway. What the
missing exit removes is the station's ability to *distinguish*. `iso20` is the multiplexer's one
decision, and after a failed read it is taken from a buffer whose contents are undefined — with
`payload_len` zeroed and the EXI stream sized 0, in this build, but that is an accident of the setup
code two lines above rather than a guarantee.

The visible consequence is that every unreadable first message becomes an **ISO 15118-2 session**. A
`-20`-only EV whose first message is truncated by anything — a retransmission the station timed out on,
a middlebox, a half-open connection — is routed to a backend that cannot serve it, instead of being
dropped where the fault occurred. And the station's log says both things at once: it reports the read
failure and then reports handling the message it failed to read, which is exactly the combination that
made this look like noise for a week.

## What produced the short read here, and in August

Here: `printf '\x01\xfe\x80\x01\x00\x00' | socat -T6 - TCP6:[…]:61342`, on purpose.

In the 2026-08-03 run: unknown, and it does not matter to the defect. Their `connection_read()` loops
until the byte count is satisfied or the sequence timeout expires
(`EvseV2G/connection/connection.cpp:265-300`), so a header split across TCP segments is reassembled
correctly — "got 6" means the peer really did send six bytes and then stop or close. Two connections in
that run did.

## How it was run

```bash
/usr/sbin/mosquitto -p 1883 &
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-mux-ours.yaml &
bash tools/interop-everest/sdp-probe.sh eth0                  # → [fe80::…%eth0]:61342
printf '\x01\xfe\x80\x01\x00\x00\x00\x00' | socat -T6 - 'TCP6:[fe80::…%eth0]:61342'   # A
printf '\x01\xfe\x80\x01\x00\x00'         | socat -T6 - 'TCP6:[fe80::…%eth0]:61342'   # B
```

No EV, no car simulation, no authorization. The station is untouched — this is its stock behaviour on
a stock-shaped config.

*One housekeeping note for anyone repeating it: `Evse15118D20` writes its session log to a **relative**
path, so it lands in whatever directory the manager was started from. It wrote one into this
repository during this run; `.gitignore:52` already carries a rule for that filename shape, from
someone hitting it before. It was deleted rather than left ignored.*
