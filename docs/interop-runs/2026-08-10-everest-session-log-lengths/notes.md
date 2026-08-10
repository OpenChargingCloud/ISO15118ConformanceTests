# 2026-08-10 — `session_logging` on everest-core 2026.02.1: the response-length defect, measured

**The re-measurement the twenty-third filing was waiting for.** That report was written from a
2026-08-02 capture against the demo image and a *reading* of the 2026.02.1 source. This run puts the
current release on the wire: a complete ISO 15118-2 DC charge against `EvseV2G` at **2026.02.1**, with
`session_logging` on, their published `v2g_messages` captured off MQTT and our own recording of the
same session beside it.

**Every one of the 43 responses is published with the wrong number of bytes, and every one of the 43
requests is byte-exact.** Nothing about the defect has changed. Two things this run adds that the
2026-08-02 capture could not: the length is not merely *wrong*, it is **exactly the preceding
request's**, in all 43 cases without exception; and the published frame's first byte is `0x00` rather
than the V2GTP version `0x01` in 42 of them, so what the log records is not even a well-formed frame.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), built from source in WSL2 — no demo image |
| Their module | `EvseV2G` (cbV2G underneath), `config-dc2-ours.yaml`, `session_logging: true` |
| Ours | `WWCP_ISO15118` @ `433b698`, EVCC |
| Direction | our EVCC → their charger |
| Session | ISO 15118-2 DC, plain TCP, EIM, driven to a complete charge by `sil-car.sh CP_AT_PLUGIN=1` |
| Outcome | **43 phases, `SessionStopRes` OK** — and 43 of 43 published responses wrong |
| Artifacts | [`flow.md`](flow.md), [`frames.log`](frames.log) (ours), [`their-mqtt-view.log`](their-mqtt-view.log), [`their-charger.log`](their-charger.log) |

## The session

A full charge, not a stalled negotiation — which matters, because it puts messages of every size
through the same code path, including the three `CurrentDemandRes` that are the largest responses of
the session and the `SupportedAppProtocolRes` that is the smallest.

```
0     SupportedAppProtocolReq     → OK_SuccessfulNegotiation
1     SessionSetupReq             → OK_NewSessionEstablished
2     ServiceDiscoveryReq         → OK
3     PaymentServiceSelectionReq  → OK
4…5   AuthorizationReq × 2        → OK        (the plug-in token had already authorized)
6     ChargeParameterDiscoveryReq → OK
7…34  CableCheckReq × 28          → OK
35    PreChargeReq                → OK
36    PowerDeliveryReq (Start)    → OK
37…39 CurrentDemandReq × 3        → OK
40    PowerDeliveryReq (Stop)     → OK
41    WeldingDetectionReq         → OK
42    SessionStopReq              → OK
```

## The measurement

Their record is the `v2g_messages` variable, one publish per message, name + hex + base64:

```
everest/modules/iso15118_charger/impl/charger/var/v2g_messages
{"data":{"data":{"exi":"01fe8001…","exi_base64":"Af6AAQ…","id":"SupportedAppProtocolReq"}},"msg_type":"Var"}
```

Ours is [`frames.log`](frames.log), recorded by our EVCC from the socket. Compared message by message,
by position within each direction:

```
requests   published byte-exact                       : 43 / 43
responses  published byte-exact                       :  0 / 43
responses  published length == the request's length   : 43 / 43
responses  published length == the response's length  :  1 / 43   ← and that one is a coincidence
responses  payload (after the 8 B header) identical   :  1 / 43   ← the same one
responses  header bytes 1..7 == the request's         : 43 / 43
responses  first byte 0x00 (V2GTP version is 0x01)    : 42 / 43
```

Per message type — "on the wire" is our recording, "published" is theirs:

| Message | on the wire | published | what the published bytes are |
|---|---|---|---|
| `SupportedAppProtocolRes` | 12 B | 44 B | the whole response + 32 bytes of the request still in the buffer |
| `SessionSetupRes` | 39 B | 29 B | first 21 of the response's 31 payload bytes |
| `ServiceDiscoveryRes` | 27 B | 21 B | first 13 of the response's 19 payload bytes |
| `PaymentServiceSelectionRes` | 22 B | 24 B | the whole response + 2 bytes of the request still in the buffer |
| `AuthorizationRes` ×2 | 23 B | 21 B | first 13 of the response's 15 payload bytes |
| `ChargeParameterDiscoveryRes` | 71 B | 37 B | first 29 of the response's 63 payload bytes |
| `CableCheckRes` ×28 | 27 B | 24 B | first 16 of the response's 19 payload bytes |
| `PreChargeRes` | 30 B | 32 B | the whole response + 2 bytes of the request still in the buffer |
| `PowerDeliveryRes` (Start) | 26 B | 30 B | the whole response + 4 bytes of the request still in the buffer |
| `CurrentDemandRes` | 83 B | 33 B | first 25 of the response's 75 payload bytes |
| `CurrentDemandRes` | 67 B | 33 B | first 25 of the response's 59 payload bytes |
| `CurrentDemandRes` | 84 B | 33 B | first 25 of the response's 76 payload bytes |
| `PowerDeliveryRes` (Stop) | 26 B | 24 B | first 16 of the response's 18 payload bytes |
| `WeldingDetectionRes` | 30 B | 24 B | first 16 of the response's 22 payload bytes |
| `SessionStopRes` | 22 B | 22 B | payload identical — request and response happen to be the same length |

The clearest single line in the whole capture is the first response, because the request before it is
the longest message of the session and the response is the shortest:

```
SupportedAppProtocolReq  on the wire : 01fe8001 00000024 8000ebab9371d34b9b79d189a98989c1d191d191818999d26b9b3a232b30020000040040
SupportedAppProtocolRes  on the wire : 01fe8001 00000004 80400040
SupportedAppProtocolRes  published   : 01fe8001 00000024 804000409371d34b9b79d189a98989c1d191d191818999d26b9b3a232b30020000040040
                                                └ 36, the request's payload length
                                                          └ the 4-byte response …
                                                                  └ … then 32 bytes of the request, untouched
```

`SessionStopRes` is the one row that looks harmless, and is worth reading carefully rather than
counting as a pass: request and response are both 22 bytes, so the wrong length happens to be the
right number. The bytes still differ, in the first one.

## The mechanism, confirmed against the running build

Exactly as the report reads it from the source, plus one detail the source reading missed.

`publish_var_V2G_Message()` sizes both the hex and the base64 from `conn->payload_len`
(`modules/EVSE/EvseV2G/v2g_server.cpp:103-127`), and `conn->payload_len` is written in exactly one
place: `v2g_incoming_v2gtp()`, from the **request's** V2GTP header (`:157`). The response's true length
is `exi_bitstream_get_length(&conn->stream)`, computed in `v2g_outgoing_v2gtp()` (`:207-209`) — one
function later, and both response publish sites run before it (`:412` and `:575`). The measurement is
the direct consequence: 43 of 43 responses carry the request's length, and the header bytes 1..7 are
the request's own header, unmodified.

The detail: the published response's **first byte is `0x00`, not the V2GTP version `0x01`**, in every
response but the first. It comes from the buffer reset that precedes each encode —

```cpp
/* Reset v2g-buffer */
conn->stream.data[0] = 0;
conn->stream.bit_count = 0;
conn->stream.byte_pos = V2GTP_HEADER_LENGTH;
```

`v2g_server.cpp:537-540`; `conn->stream.data` *is* `conn->buffer`. `V2GTP_WriteHeader()` puts the
version back, but that also happens in `v2g_outgoing_v2gtp()`, after the publish. The one exception is
`SupportedAppProtocolRes`: the SAP path resets at `:382-384`, *before* the request is read, and reading
the request writes a real header back over it — which is why that single response is published with
`01fe8001` and the other 42 with `00fe8001`.

So the published record is not a truncated frame. It is not a frame: a V2GTP reader handed those bytes
rejects them on the version check before it ever gets to the length.

## `Evse15118D20` does not have this defect, because it publishes no bytes at all

The report's open question. `Evse15118D20` fills only the message id
(`charger/ISO15118_chargerImpl.cpp:526-528`):

```cpp
callbacks.v2g_message = [this](iso15118::message_20::Type id) {
    const auto v2g_message_id = convert_v2g_message_type(id);
    publish_v2g_messages({v2g_message_id});
};
```

`exi` and `exi_base64` are optional in the type — `types/iso15118.yaml:443-448` lists `id` as the only
required property, `exi`/`exi_base64` follow at `:462-468` — so the -20 module is within its rights and
simply offers no byte-level record. `IsoMux` forwards whichever of the two is selected
(`IsoMux/charger/ISO15118_chargerImpl.cpp:436-445`) and adds nothing.

Worth saying in the report as a fact rather than a caveat: **the byte-level session record exists only
for -2/DIN, and it is the one that is wrong.**

## How it was run

Native build, no containers. `session_logging` is already on in our `-2` DC config, so nothing was
changed for this run beyond starting a subscriber:

```bash
/usr/sbin/mosquitto -p 1883 &
mosquitto_sub -v -t 'everest/modules/iso15118_charger/impl/charger/var/v2g_messages' > v2g.log &
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-dc2-ours.yaml &
CP_AT_PLUGIN=1 bash tools/interop-everest/sil-car.sh          # plug in, hold CP at state C
bash tools/interop-everest/sdp-probe.sh eth0                  # → [fe80::…%eth0]:61341, security=10
V2G_INTEROP_SECC='[fe80::…%eth0]:61341' V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RECORD=~/everest/run/sesslog-record \
  dotnet test ISO15118ConformanceTests.Simulation/… --artifacts-path ~/wsl-artifacts \
    --filter FullyQualifiedName~OurEvcc_AgainstTheirEvseV2G
```

The whole thing runs inside WSL, our EVCC included — with `dotnet` there, the socat relay the earlier
runs needed is unnecessary for a plain-TCP forward run.

Two notes for anyone repeating it:

- **The topic carries the variable name on 2026.02.1**:
  `everest/modules/<module>/impl/<impl>/var/<name>`, and the payload is
  `{"data":{"data":{…}},"msg_type":"Var"}` — two levels of `data`. A subscription in the pre-2025.10
  shape matches nothing and looks exactly like a station with nothing to say.
- **Compare by position, per direction.** Our recorder keeps two octet streams and no clock, and their
  publishes interleave request and response. Splitting their stream on the `Req`/`Res` suffix gives two
  ordered lists that align 1:1 with ours — 43 and 43, with the names agreeing throughout, which is
  itself the check that the alignment is right.

### Noticed in passing: `mqtt-authorize.sh` cannot work on 2026.02.1

Not used in this run — the plug-in did the authorizing — but the same topic change disables it.
[`tools/interop-everest/mqtt-authorize.sh`](../../../tools/interop-everest/mqtt-authorize.sh)
subscribes to two exact topics, `everest/<charger>/charger/var` and
`everest/modules/<charger>/impl/charger/var`, and on 2026.02.1 the variable name is a further topic
level (`…/var/require_auth_eim`), which neither filter matches — MQTT filters are level-exact without
a wildcard. The publish side has the same problem, plus the envelope changed to
`{"msg_type":"Var","data":{…}}`. So the script would write an empty log and look like a station with
nothing to say: precisely the failure its own header warns about, one release later. Fixing it means
a subscription per shape *and* a payload per shape, and it should be fixed against a running station
rather than by inspection — left alone here rather than changed untested.

## What this settles

The twenty-third filing's first checklist item, and its last-but-two. The report can now say *measured
on 2026.02.1* instead of *measured in 2026-08 and read in 2026.02.1*, it can state that
`Evse15118D20` is unaffected instead of admitting it was not looked at, and it gains the `0x00`
version byte — a second, independent way the published record is not what went out, and one that makes
the defect trivially detectable by anyone who tries to feed the log back into a decoder.
