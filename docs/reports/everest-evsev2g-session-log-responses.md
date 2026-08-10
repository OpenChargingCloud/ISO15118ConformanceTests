# Draft report to EVerest — `session_logging` records every response with the wrong length

Status: **draft, not sent.** Measured on the wire 2026-08-02 against the `everest-demo` manager image,
and the mechanism read out of **2026.02.1** (`b61bb12b8`) on 2026-08-10, where it is unchanged. Post it
under your own name; see *Before sending* at the bottom — the first item is a re-measurement on the
current release that has not been done.

Evidence in this repository:
[`2026-08-02-everest-iso2-dc-mqtt-auth`](../interop-runs/2026-08-02-everest-iso2-dc-mqtt-auth/notes.md)
— the second finding, with [`their-mqtt-view.log`](../interop-runs/2026-08-02-everest-iso2-dc-mqtt-auth/their-mqtt-view.log)
(your station's own published record of that session) and
[`frames.log`](../interop-runs/2026-08-02-everest-iso2-dc-mqtt-auth/frames.log) (what was actually on
the wire, recorded independently by us).

Four other reports for the same project are in
[`everest-loop-shutdown.md`](everest-loop-shutdown.md),
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md),
[`everest-isomux-iso20-over-tls12.md`](everest-isomux-iso20-over-tls12.md) and
[`everest-isomux-sap-priority.md`](everest-isomux-sap-priority.md), plus
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) and one to your fork of Josev's
certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)). **File them separately.**
The framing in `everest-loop-shutdown.md` — what everest-core has been worth to this project, and why a
report from us is not a bug filed by a stranger — applies here unchanged and is not repeated.

---

**Title:** `EvseV2G` `publish_var_V2G_Message()`: a response is published using `conn->payload_len`,
which still holds the **request's** length — the record is taken before `v2g_outgoing_v2gtp()` writes
the response's own header, so every logged response is truncated or padded with stale buffer

**Version:** everest-core **2026.02.1** (`b61bb12b8`), module `EvseV2G`. The measurement below is from
the `ghcr.io/everest/everest-demo/manager:main` image of 2026-08-02; the three lines that cause it are
identical in both.

## What we saw

Your station publishes every message on `everest/<module>/charger/var` as name + base64 + hex when
`EvseManager`'s `session_logging` is on. It is an attractive station-side record of a session, and we
used it as one. **Requests are byte-exact. Responses are not**, and the pattern is exact enough to name:

| Message | on the wire | published |
|---|---|---|
| `SessionSetupRes` | 31 B | 21 B — the response, truncated |
| `ServiceDiscoveryRes` | 36 B | 13 B — truncated |
| `ChargeParameterDiscoveryRes` | 62 B | 29 B — truncated |
| `SupportedAppProtocolRes` | 4 B | 36 B — the response plus stale bytes |
| `PaymentServiceSelectionRes` | 14 B | 16 B — the response plus stale bytes |

Each published length is the **preceding request's**. The name attached to it is correct, which is what
makes it hard to notice: the record says `SessionSetupRes` and carries 21 bytes that are the first 21
bytes of one.

We could only see it because we had our own recording of the same session to compare against. On its
own the telemetry looks entirely plausible.

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
   out, header included, and `payload_len` stops being consulted for responses at all — but the
   publisher would need the length from somewhere other than `payload_len`, so this pairs with (2).
2. **Give the publisher the length.** `publish_var_V2G_Message(conn, is_req)` already knows which
   direction it is in; for a response it can take `exi_bitstream_get_length(&conn->stream)` instead of
   `conn->payload_len + V2GTP_HEADER_LENGTH`. Three lines, no ordering change.

Worth checking `Evse15118D20` for the same shape while you are there — we have not looked, and it
publishes its own telemetry.

## Not part of this

The same 2026-08-02 run recorded a second-session segfault in `EvseV2G`. That was **withdrawn** the
next day: it does not reproduce on everest-core 2025.10 or later, so it was a defect of the 2023.10.0
image and there is nothing to report. Mentioned only so that anyone reading those run notes does not
resurrect it.

---

## Before sending

- [ ] **Re-measure on 2026.02.1.** The byte table above is from the demo image; the source at
      2026.02.1 says it must still happen, but that is a reading. One session with `session_logging`
      on, `mosquitto_sub` on `everest/+/charger/var`, and any independent capture of the wire settles
      it — and the report should say *measured on 2026.02.1* rather than *measured in 2026-08 and read
      in 2026.02.1*.
- [x] **Reproduce it yourself.** The table is from their own published stream against their own
      station; the comparison side is our recorder, not our claim.
- [x] **Re-check every line reference against the tree.** `v2g_server.cpp:103-127`, `:157`, `:202-216`,
      `:383`, `:410-415`, `:470`, `:572-579`, `v2g_ctx.cpp:317`,
      `charger/ISO15118_chargerImpl.cpp:131`, `EvseManager.cpp:988` — read from the built 2026.02.1
      source on 2026-08-10.
- [ ] **Lead with the option, not the arithmetic.** `session_logging` is what makes this matter; an
      off-by-a-length in a debug helper reads as trivia until you say which switch produces it and what
      people use that switch for.
- [ ] **Say the name is right and the bytes are wrong.** That combination is the reason it survived
      this long, and it is the sentence a maintainer needs in order to care.
- [ ] **Check `Evse15118D20` before claiming it is only `EvseV2G`.** We did not look.
- [ ] **File one issue, this one.**
- [ ] **Post under your own name, in your own words.**
