# Draft report to EVerest — `session_logging` records every response with the wrong length

Status: **draft, not sent.** Measured on the wire against **everest-core 2026.02.1** (`b61bb12b8`) on
2026-08-10 — a complete ISO 15118-2 DC charge, 43 responses, all 43 wrong. First seen 2026-08-02
against the `everest-demo` manager image; the three lines that cause it are identical in both. Post it
under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-10-everest-session-log-lengths`](../interop-runs/2026-08-10-everest-session-log-lengths/notes.md)
— the measurement on the current release, with
[`their-mqtt-view.log`](../interop-runs/2026-08-10-everest-session-log-lengths/their-mqtt-view.log)
(your station's own published record of that session) and
[`frames.log`](../interop-runs/2026-08-10-everest-session-log-lengths/frames.log) (what was actually on
the wire, recorded independently by us). The first sighting is in
[`2026-08-02-everest-iso2-dc-mqtt-auth`](../interop-runs/2026-08-02-everest-iso2-dc-mqtt-auth/notes.md),
its second finding.

Three other reports for the same project are in
[`everest-loop-shutdown.md`](everest-loop-shutdown.md),
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md) and
[`everest-isomux.md`](everest-isomux.md) (four findings in that one module, merged), plus
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) and one to your fork of Josev's
certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)). **File them separately.**
The framing in `everest-loop-shutdown.md` — what everest-core has been worth to this project, and why a
report from us is not a bug filed by a stranger — applies here unchanged and is not repeated.

---

**Title:** `EvseV2G` `publish_var_V2G_Message()`: a response is published using `conn->payload_len`,
which still holds the **request's** length — the record is taken before `v2g_outgoing_v2gtp()` writes
the response's own header, so every logged response is truncated or padded with stale buffer

**Version:** everest-core **2026.02.1** (`b61bb12b8`), module `EvseV2G`, `EvseManager` with
`session_logging: true`. Measured 2026-08-10 on a complete ISO 15118-2 DC charge (SIL, plain TCP, EIM).

## What we saw

Your station publishes every message on `everest/modules/<module>/impl/charger/var/v2g_messages` as
name + base64 + hex when `EvseManager`'s `session_logging` is on. It is an attractive station-side
record of a session, and we used it as one. **Requests are byte-exact. Responses are not** — none of
them — and the pattern is exact enough to name:

```
requests   published byte-exact                       : 43 / 43
responses  published byte-exact                       :  0 / 43
responses  published length == the request's length   : 43 / 43
responses  published length == the response's length  :  1 / 43   ← and that one is a coincidence
```

| Message | on the wire | published | what the published bytes are |
|---|---|---|---|
| `SupportedAppProtocolRes` | 12 B | 44 B | the whole response + 32 bytes of the request still in the buffer |
| `SessionSetupRes` | 39 B | 29 B | first 21 of the response's 31 payload bytes |
| `ServiceDiscoveryRes` | 27 B | 21 B | first 13 of the response's 19 payload bytes |
| `PaymentServiceSelectionRes` | 22 B | 24 B | the response + 2 bytes of the request still in the buffer |
| `ChargeParameterDiscoveryRes` | 71 B | 37 B | first 29 of the response's 63 payload bytes |
| `CableCheckRes` ×28 | 27 B | 24 B | first 16 of the response's 19 payload bytes |
| `PreChargeRes` | 30 B | 32 B | the response + 2 bytes of the request still in the buffer |
| `PowerDeliveryRes` | 26 B | 30 B | the response + 4 bytes of the request still in the buffer |
| `CurrentDemandRes` ×3 | 83 / 67 / 84 B | 33 B | first 25 payload bytes of each |
| `WeldingDetectionRes` | 30 B | 24 B | first 16 of the response's 22 payload bytes |
| `SessionStopRes` | 22 B | 22 B | payload identical — request and response happen to be the same length |

Each published length is the **preceding request's**, in all 43 cases without exception. The name
attached to it is correct, which is what makes it hard to notice: the record says `SessionSetupRes`,
and what follows is a V2GTP header declaring 21 bytes of payload followed by the first 21 bytes of a
39-byte message.

The clearest single line is the first response, because the request before it is the longest message of
the session and the response is the shortest:

```
SupportedAppProtocolReq  on the wire : 01fe8001 00000024 8000ebab9371d34b…b30020000040040
SupportedAppProtocolRes  on the wire : 01fe8001 00000004 80400040
SupportedAppProtocolRes  published   : 01fe8001 00000024 804000409371d34b…b30020000040040
                                                └ 36, the request's payload length
                                                          └ the 4-byte response …
                                                                  └ … then 32 bytes of the request
```

We could only see it because we had our own recording of the same session to compare against. On its
own the telemetry looks entirely plausible.

### And the version byte, in 42 of the 43

The published response's **first byte is `0x00`, not the V2GTP version `0x01`** — every response except
`SupportedAppProtocolRes`. So the record is not a truncated frame; it is not a frame at all, and a
V2GTP reader handed those bytes rejects them on the version check before reaching the length. Same
root cause, one line further along: see *Where it comes from*.

## Where it comes from

`publish_var_V2G_Message()` sizes both the hex and the base64 from `conn->payload_len`
(`modules/EVSE/EvseV2G/v2g_server.cpp:103-127`):

```cpp
for (int i = 0; ((tempbuff != NULL) && (i < conn->payload_len + V2GTP_HEADER_LENGTH)); i++) { … }
EXI_Base64 = openssl::base64_encode(conn->buffer, conn->payload_len + V2GTP_HEADER_LENGTH);
```

`conn->payload_len` is written in exactly one place — `v2g_incoming_v2gtp()`, from the **request's**
V2GTP header (`v2g_server.cpp:157`) — and otherwise only zeroed before the next read (`:383`, `:470`).
Nothing sets it for a response.

The response's true length exists, but it is computed one function later. `v2g_outgoing_v2gtp()`
(`:202-216`) takes it from the stream and only then fixes up the header:

```cpp
const auto len = exi_bitstream_get_length(&conn->stream);
V2GTP_WriteHeader(conn->buffer, len - V2GTP_HEADER_LENGTH);
```

And both response publish sites run **before** that call — at `:410-415` and, more plainly still,
at `:572-579`, where the comment on the next line says what is about to happen:

```cpp
/* form the content of V2G_Message type and publish the response for debugging*/
if (conn->ctx->debugMode == true) {
    publish_var_V2G_Message(conn, false);
}

/* Write header and send next res-msg */
if ((rv != 0) || ((rv = v2g_outgoing_v2gtp(conn)) == -1)) {
```

So the record is taken while the buffer holds a response body and a header belonging to the request
before it. Shorter response than request → truncated. Longer → the response followed by whatever the
shared buffer still held from the previous message.

The `0x00` first byte is the same ordering, one line further along. Each encode is preceded by a buffer
reset (`:537-540`):

```cpp
/* Reset v2g-buffer */
conn->stream.data[0] = 0;
```

`conn->stream.data` *is* `conn->buffer`, so that clears the V2GTP version byte, and
`V2GTP_WriteHeader()` — which would put it back — is again inside `v2g_outgoing_v2gtp()`, after the
publish. `SupportedAppProtocolRes` escapes it only because the handshake path resets at `:382-384`,
*before* the request is read, and reading the request writes a real header over it.

## Why we think it is worth fixing

**Because of which option turns it on.** `debugMode` reaches `EvseV2G` from
`EvseManager`'s `session_logging` (`EvseManager.cpp:988`, `:1597`, `:1627` → `call_setup(evseid,
sae_mode, config.session_logging)`), and several of your own shipped configurations set it. That is the
switch an operator flips to *obtain a faithful record of a session* — for a bug report, a plugfest, a
compliance question, an argument with a vehicle manufacturer about who sent what. A record that is
silently wrong for one whole direction is worse for those purposes than no record at all, because it
will be believed.

It is also cheap to get right, and nothing about the fix is a judgement call: the correct length is
already computed a few lines away.

## Suggested direction

Two shapes, both small; which belongs in your tree is yours, and we would send a PR only if you want
one.

1. **Publish after the header is written.** Move the `publish_var_V2G_Message(conn, false)` calls to
   just after `v2g_outgoing_v2gtp()` succeeds, at both sites. The buffer then holds exactly what went
   out, header and version byte included, and `payload_len` stops being consulted for responses at all
   — but the publisher would need the length from somewhere other than `payload_len`, so this pairs
   with (2).
2. **Give the publisher the length.** `publish_var_V2G_Message(conn, is_req)` already knows which
   direction it is in; for a response it can take `exi_bitstream_get_length(&conn->stream)` instead of
   `conn->payload_len + V2GTP_HEADER_LENGTH`. Three lines, no ordering change — but on its own it
   leaves the version byte at `0x00`, so it wants (1) as well, or a `V2GTP_WriteHeader()` before the
   publish.

**`Evse15118D20` does not have this problem**, because it publishes no bytes: its callback fills only
the message id (`charger/ISO15118_chargerImpl.cpp:526-528`), and `exi`/`exi_base64` are optional in the
type (`types/iso15118.yaml:443-448`). `IsoMux` forwards whichever module is selected and adds nothing.
Which is worth saying plainly: the byte-level session record exists only for -2/DIN, and it is the one
that is wrong.

## Not part of this

The same 2026-08-02 run recorded a second-session segfault in `EvseV2G`. That was **withdrawn** the
next day: it does not reproduce on everest-core 2025.10 or later, so it was a defect of the 2023.10.0
image and there is nothing to report. Mentioned only so that anyone reading those run notes does not
resurrect it.

---

## Before sending

- [x] **Re-measure on 2026.02.1.** Done 2026-08-10, on a complete -2 DC charge against the
      source-built release: 43 requests byte-exact, 43 responses wrong, every one of them carrying the
      preceding request's length —
      [`2026-08-10-everest-session-log-lengths`](../interop-runs/2026-08-10-everest-session-log-lengths/notes.md).
      The table above is that run, not the demo image.
- [x] **Reproduce it yourself.** The table is from their own published stream against their own
      station; the comparison side is our recorder, not our claim.
- [x] **Re-check every line reference against the tree.** `v2g_server.cpp:103-127`, `:157`, `:202-216`,
      `:382-384`, `:410-415`, `:470`, `:537-540`, `:572-579`, `v2g_ctx.cpp:317`,
      `charger/ISO15118_chargerImpl.cpp:131`, `EvseManager.cpp:988`,
      `Evse15118D20/charger/ISO15118_chargerImpl.cpp:526-528`, `types/iso15118.yaml:443-448` — read
      from the built 2026.02.1 source on 2026-08-10.
- [x] **Check `Evse15118D20` before claiming it is only `EvseV2G`.** Looked, 2026-08-10: it publishes
      the message id and nothing else, so there are no bytes to get wrong. Said in *Suggested
      direction* as a fact about where the byte-level record exists at all.
- [ ] **Lead with the option, not the arithmetic.** `session_logging` is what makes this matter; an
      off-by-a-length in a debug helper reads as trivia until you say which switch produces it and what
      people use that switch for.
- [ ] **Say the name is right and the bytes are wrong.** That combination is the reason it survived
      this long, and it is the sentence a maintainer needs in order to care.
- [ ] **File one issue, this one.**
- [ ] **Post under your own name, in your own words.**
