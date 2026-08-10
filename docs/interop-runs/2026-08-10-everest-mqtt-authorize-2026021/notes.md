# 2026-08-10 — `mqtt-authorize.sh` on everest-core 2026.02.1, with the control that proves it was dead

Authorizing an EVerest session over MQTT with no hardware at all had stopped working, silently, when
everest-core moved the variable name into the topic. This carries the script forward and runs both
versions against the same station, back to back: **the old one authorizes nothing and the session sits
at `AuthorizationReq` for 401 messages; the new one authorizes on the fourth.**

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), source build in WSL2 |
| Config | `config-dc2-ours.yaml`, **no car** — `sil-car.sh` was deliberately not run |
| Ours | `WWCP_ISO15118` @ `433b698`, EVCC |
| Under test | [`tools/interop-everest/mqtt-authorize.sh`](../../../tools/interop-everest/mqtt-authorize.sh) |
| Outcome | **new: authorized, on to `ChargeParameterDiscovery` and `CableCheck`. old: no token ever seen by their `Auth`** |
| Artifacts | [`authorize.log`](authorize.log), [`flow.md`](flow.md), [`frames.log`](frames.log), [`their-charger.log`](their-charger.log), and for the control [`control.authorize.log`](control.authorize.log), [`control.flow.md`](control.flow.md) |

## The control first, because it is the point

The script as it stood before today, unchanged, against the station:

```
06:37:32 watching everest/iso15118_charger/charger/var
06:37:32      and everest/modules/iso15118_charger/impl/charger/var
```

That is the whole log. Their `Auth` module logged `Received new token` **zero** times, and the session:

```
401 x AuthorizationReq   →   AuthorizationRes, EVSEProcessing = Ongoing
  1 x PaymentServiceSelectionReq
  1 x ServiceDiscoveryReq
  1 x SessionSetupReq
  1 x SupportedAppProtocolReq
```

A script that starts, prints what it is watching, and then does nothing at all — which is exactly what
its own header warns about, one release later than the warning was written.

## Why it was dead

Two changes, either of which alone is enough.

**The topic.** On 2026.02.1 the variable name is a topic level of its own —
`everest/modules/<module>/impl/<impl>/var/<name>` — and MQTT topic filters are level-exact unless they
carry a wildcard. A filter ending in `…/var` therefore matches `…/var/require_auth_eim` not at all. The
publish comes from `Everest::publish_var` (`lib/everest/framework/lib/everest.cpp:462-500`).

**The payload.** It gained an envelope:

```
2023.10.0 / 2025.10.0   {"data": <value>, "name": "<var>"}
2026.02.1               {"msg_type": "Var", "data": {"data": <value>}}
```

Two levels of `data`, and no name — it is in the topic. The receive side
(`message_handler.cpp:289-408`) switches on `msg_type` and hands `data["data"]` to variable
subscribers; a payload **without** `msg_type` is routed to *external* MQTT handlers instead, so
publishing the old shape on the new topic would not have been an error either. It would have been a
second silent no-op.

And a third, on the token itself: `ProvidedIdToken.id_token` is now an `IdToken` **object**
(`types/authorization.yaml:60-99`), not a bare string beside an `id_token_type`. The type sets
`additionalProperties: false` and the framework validates on receive, so the old token would have been
dropped with *"Ignoring incoming var … because not matching manifest schema"* even on the right topic
in the right envelope.

## The fix, and what it looked like on the wire

Three subscriptions instead of two, the trigger matched on the **topic** when the name is not in the
payload (`require_auth_eim` is a `"null"`-typed variable — its payload carries nothing to match), and a
token in whichever shape the trigger arrived in. The new token is what their own `DummyTokenProvider`
publishes (`modules/Testing/DummyTokenProvider/main/auth_token_providerImpl.cpp:16-24`), minus
`parent_id_token`.

```
06:34:23 watching everest/iso15118_charger/charger/var
06:34:23      and everest/modules/iso15118_charger/impl/charger/var
06:34:23      and everest/modules/iso15118_charger/impl/charger/var/require_auth_eim
06:35:09 everest/modules/iso15118_charger/impl/charger/var/require_auth_eim: {"data":{"data":null},"msg_type":"Var"}
06:35:09 -> TOKEN1 to everest/modules/token_provider/impl/main/var/provided_token
```

Their side, from [`their-charger.log`](their-charger.log) — nothing of theirs patched, and their `Auth`
cannot tell it from their own provider:

```
auth:Auth        Received new token: { "authorization_type": "RFID", "id_token": { …
token_validator  Got validation request for token: [redacted] hash: CE55F71752B68164
token_validator  Returning validation status: Accepted
auth:Auth        Providing authorization to evse#1
evse_manager     EVSE IEC Session Started: Authorized
evse_manager     EVSE ISO V2G AuthorizationRes
auth:Auth        Result for token: … USED_TO_START_TRANSACTION
```

and the session moved on ([`flow.md`](flow.md)):

```
0  SupportedAppProtocolReq     → OK_SuccessfulNegotiation
1  SessionSetupReq             → OK_NewSessionEstablished
2  ServiceDiscoveryReq         → OK
3  PaymentServiceSelectionReq  → OK
4…7 AuthorizationReq × 4       → OK   ← the fourth got EVSEProcessing = Finished
8  ChargeParameterDiscoveryReq → OK
9… CableCheckReq × 401         → OK, Ongoing
```

Four polls against 401: the same station, the same config, the same EVCC, ten minutes apart.

**Where it stops is unchanged and expected.** With no car there is no CP line to move to state C, so
`CableCheck` never completes — the README has always said the MQTT path gets a session talking, not
charging. One difference worth recording against the 2026-08-02 run on the 2023 demo image, which
answered `CableCheckRes` = `FAILED` after 34 tries: **2026.02.1 answers `Ongoing` indefinitely**, and
it was our EVCC's own timeout that ended this run. Neither is wrong — a station waiting on hardware
that will never report may say either — but a harness that keys off `FAILED` to detect "no car" will
wait forever here.

## How it was run

```bash
/usr/sbin/mosquitto -p 1883 &
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-dc2-ours.yaml &
sh tools/interop-everest/mqtt-authorize.sh > auth.log 2>&1 &     # native, no container
bash tools/interop-everest/sdp-probe.sh eth0                     # → [fe80::…%eth0]:61341
V2G_INTEROP_SECC='[fe80::…%eth0]:61341' V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RECORD=… dotnet test … --filter FullyQualifiedName~OurEvcc_AgainstTheirEvseV2G
```

The control was the identical sequence with the pre-change script, extracted from `0d0aed5`, against a
freshly restarted manager.

## What this settles

The warning added to the harness earlier the same day
([`2026-08-10-everest-session-log-lengths`](../2026-08-10-everest-session-log-lengths/notes.md), the
*noticed in passing* section) said the script could not work on 2026.02.1 and left it alone rather than
change it untested. It is now changed and tested, with a control, and the warning is replaced by the
version table.
