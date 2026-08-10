# 2026-08-02 — EVerest `EvseV2G`, ISO 15118-2 DC, no TLS, authorized over MQTT

**The deepest live session this project has had against any counterparty, and the first time one of
our own fixes was exercised by a real peer.** Authorizing over MQTT moved the wall from
`Authorization` to `CableCheck`: seven phases negotiated, and their station then answered
`CableCheckRes` with **`FAILED`**. It ended the session in one line, because the response-code handling
added the day before was there to read it — the second live `FAILED` this project has been sent, and
the first one it noticed.

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) via `ghcr.io/everest/everest-demo/manager:main`, `EvseV2G` |
| Ours | `Vanaheimr.V2G.Exi` @ `2032d05` |
| Direction | our EVCC → their charger |
| Session | ISO 15118-2 DC, plain TCP, the same `config-ours.yaml` as the previous run — **unchanged** |
| Authorization | a `ProvidedIdToken` published over MQTT, triggered by their own `Require_Auth_EIM` |
| Outcome | **seven phases, then `CableCheckRes` = `FAILED`, session ended by our EVCC** |
| Artifacts | [`flow.md`](flow.md), [`frames.log`](frames.log), [`their-charger.log`](their-charger.log), [`their-mqtt-view.log`](their-mqtt-view.log), [`their-crash-on-second-session.log`](their-crash-on-second-session.log) |

## The session

```
0   SupportedAppProtocolReq      → SupportedAppProtocolRes      OK_SuccessfulNegotiation
1   SessionSetupReq              → SessionSetupRes              OK_NewSessionEstablished
2   ServiceDiscoveryReq          → ServiceDiscoveryRes          OK
3   PaymentServiceSelectionReq   → PaymentServiceSelectionRes   OK
4…9 AuthorizationReq × 6         → AuthorizationRes             OK, EVSEProcessing = Ongoing
10  AuthorizationReq             → AuthorizationRes             OK, EVSEProcessing = Finished   ← the token
11  ChargeParameterDiscoveryReq  → ChargeParameterDiscoveryRes  OK
12… CableCheckReq × 34           → CableCheckRes                OK, Ongoing
46  CableCheckReq                → CableCheckRes                FAILED
```

Compare with the previous run, which was the same setup without the token: four phases and then 1 170
authorization polls. **Six of the seven `AuthorizationReq` are the same behaviour;** the seventh is the
one the token answered.

## How the authorization was driven

Their `DummyTokenProvider` publishes a token when `EvseManager` reports a plug-in, which a TCP-only EV
never produces. So instead of a plug event, the **HLC** triggers the token, and nothing in EVerest is
patched to make it happen — [`tools/interop-everest/mqtt-authorize.sh`](../../../tools/interop-everest/mqtt-authorize.sh)
is a `mosquitto_sub` piped into a `case`, and a `mosquitto_pub`:

```
everest/iso15118_charger/charger/var   →  {"data": null, "name": "Require_Auth_EIM"}
everest/token_provider/main/var        ←  {"data": {"id_token": "TOKEN1", ...}, "name": "provided_token"}
```

`Require_Auth_EIM` is set by `EvseV2G` the moment the EV has selected EIM and sent `AuthorizationReq`,
so the token arrives exactly when a card would have been presented. Their `Auth` module cannot tell the
difference, and their own log reads as an ordinary authorization:

```
auth:Auth        :: Received new token: { "id_token": "TOKEN1", ... }
token_validator  :: Returning validation status: Accepted
auth:Auth        :: Providing authorization to connector#1
evse_manager:Ev  :: EVSE IEC Session Started: Authorized
evse_manager:Ev  :: EVSE ISO V2G AuthorizationRes
```

Two details worth keeping. Their own `DummyTokenProvider` then publishes `TOKEN1` as well — the
authorization *it* was waiting for is `EvseManager`'s session-started event, which our token caused —
and `Auth` discards it as `ALREADY_IN_PROCESS`. And `connection_timeout: 10` means the authorization is
withdrawn ten seconds later if nothing starts a transaction, so the token has to arrive while the EV is
polling, not before. Triggering on `Require_Auth_EIM` gets that right by construction.

**The topic scheme, since it is not in their documentation:** `everest/<module_id>/<impl_id>/var` for a
published variable, payload `{"data": <value>, "name": "<var_name>"}`; `…/cmd` for a call, payload
`{"data": {"args": {…}, "id": "<uuid>", "origin": "<module_id>"}, "name": "<cmd>", "type": "call"}`.
The module ids are the keys in the config file, not the module types.

## The finding — theirs: `EvseV2G` segfaults on the second session

**Reproduced four times, always at the same line, and it takes the whole charger down with it.**

```
iso15118_charge  :: SessionSetupReq.EVCCID: AB:CD:EF:01:02:03
iso15118_charge  :: Created new session with id 0x16968261566908476828
iso15118_charge  :: SelectedPaymentOption: ExternalPayment
manager          :: Module iso15118_charger (pid: 1263) exited with status: 139. Terminating all modules.
```

Status 139 is SIGSEGV. The first V2G session in a process is always fine; the **second** one dies while
handling `PaymentServiceSelectionReq`, and it does not matter how far the first one got — a first
session consisting of nothing but the SupportedAppProtocol handshake is enough to arm it. Because
EVerest's manager terminates every module when one exits, a single crash in the ISO 15118 module takes
the charger, the auth stack and the energy manager with it.

[`their-crash-on-second-session.log`](their-crash-on-second-session.log) is one process from startup:
session 1 complete, session 2 dead 200 ms in.

This is also why the previous run never saw it. One session, three minutes of polling, no second
connection. **Every interop run so far has been one session long** — the shape of the harness hid a
crash the second car of the day would hit. *Run it twice* belongs in the harness, not in the write-up.

*Corrected 2026-08-03:* **not present in everest-core 2025.10.** The same two-session procedure against
that release's `EvseV2G` gives two complete charges and no crash, so this is a defect of the 2023.10.0
image and there is nothing to report to EVerest. Checking before reporting cost ten minutes and is the
practical case for pinning an image digest —
[`../2026-08-03-everest-iso20-dc-full-charge/notes.md`](../2026-08-03-everest-iso20-dc-full-charge/notes.md).

## The wall now: the cable check waits for hardware that does not exist

Not a defect. Their station is right and the run is simply asking a simulated charger to close a
contactor no car is holding:

```
evse_manager:Ev  :: EVSE ISO Start cable check...
evse_manager:Ev  :: CableCheck Thread: Contactors are still open after timeout, giving up.
iso15118_charge  :: Failed response code detected for message "Cable Check", error: Response FAILED
```

`EvseManager::cable_check()` closes the contactor, waits ~5 s for the board-support module to report it
closed, and answers `FAILED` when it does not. In the SIL the contactor closes because the simulated car
drives the CP line through state B into C; ours is a V2G peer over TCP with no simulated hardware
attached, so the CP line stays at A and the relais never close. Their IEC-level charger state stays
`Idle` for the entire HLC session and then goes to `Error`.

Publishing `cp C` to `car_simulator`'s `executeChargingSession` over MQTT (the obvious shortcut) does
**not** help — tried, and the cable check still timed out. The CP state machine has to be walked from
`A`, and that means simulating the plug-in, not just its end state.

So the honest description of where this counterparty now stands: **the protocol half is done and the
physical half is not.** Everything up to and including `ChargeParameterDiscovery` works against a real
charger implementation; `CableCheck` onwards needs their hardware simulation driven as a car would drive
it, which is a piece of work in its own right and the next thing to do.

## What this validated — ours

**The `FAILED` handling paid for itself, against a different peer than the one that revealed it.**

```
SessionAborted: the station answered CableCheckResType with FAILED; the session ends here.
```

Until 2026-08-01 neither EVCC read a response code at all: this session would have continued into
`PreCharge` with the cable check refused behind it, and the abort would have come from somewhere much
further downstream, or not at all. That is exactly what happened the day before against eVDriveFlow,
which is how the hole was found.

**And it was the same message.** eVDriveFlow answered `DC_CableCheckRes` with `FAILED` in ISO 15118-20;
EVerest answers `CableCheckRes` with `FAILED` in ISO 15118-2. Two unrelated stacks, two protocols, and
both of them refuse at the cable check — because that is the first message where a station has to
consult hardware, and a bare TCP peer has none. It is the natural first `FAILED` of any bench run, and
a fixture built only from our own SECC will never contain it.

The ongoing-poll deadline did not fire, and correctly so: the authorization ended after seven polls and
the cable check took 35 polls in five seconds, both far inside the 60 s limit. It was armed the whole
time, which is the point.

## The finding — theirs, minor: their MQTT record of a response is not what they sent

`EvseV2G` publishes every message on `everest/iso15118_charger/charger/var` as name + base64 + hex,
which is an attractive station-side record of a session — [`their-mqtt-view.log`](their-mqtt-view.log)
is this run's. **Requests are byte-exact. Responses are not**, and the pattern is exact enough to name:

| Message | on the wire | published |
|---|---|---|
| `SessionSetupRes` | 31 B payload | 21 B — the response, truncated |
| `ServiceDiscoveryRes` | 36 B | 13 B — truncated |
| `ChargeParameterDiscoveryRes` | 62 B | 29 B — truncated |
| `SupportedAppProtocolRes` | 4 B | 36 B — the response plus stale bytes |
| `PaymentServiceSelectionRes` | 14 B | 16 B — the response plus stale bytes |

Every published response carries the **preceding request's** V2GTP length field, so it is the response
truncated to the request's size, or the response followed by whatever the shared buffer still held.
Only visible because we had our own recording of the same session to compare against — their telemetry
on its own looks entirely plausible.

> **Filed 2026-08-10** as the twenty-third:
> [`reports/everest-evsev2g-session-log-responses.md`](../../reports/everest-evsev2g-session-log-responses.md).
> Reading the 2026.02.1 source found the mechanism unchanged and named it exactly:
> `publish_var_V2G_Message()` sizes both the hex and the base64 from `conn->payload_len`, which is
> written **only** by `v2g_incoming_v2gtp()` from the request's header — and both response publish
> sites run *before* `v2g_outgoing_v2gtp()`, which is what computes the response's own length from the
> stream and fixes the header up. The second site says so in the comment on the following line:
> `/* Write header and send next res-msg */`.
>
> Two things the filing adds that this note did not have. The switch that turns the telemetry on is
> `EvseManager`'s **`session_logging`** — the option an operator sets in order to obtain a faithful
> record — and several of their own shipped configurations set it. And the *name* attached to each
> record is correct, because it comes from the message type rather than the buffer; right name, wrong
> bytes is why it survived. Unticked on that report: the byte table below is from the 2023 demo image,
> and a re-measurement on 2026.02.1 has not been done.

## Two operational traps, both mine, both worth writing down

**1. `pkill -f 'bin/manager'` orphans the modules.** EVerest's modules are separate processes; killing
the manager leaves them running and still bound to port 61341. The next generation's sessions are then
served, or half-served, by a module whose peers are gone — which produced an hour of erratic behaviour
that looked like a finding and was not. Kill the whole process group, or recreate the container.

**2. On colima, publishing a port before its backend listens poisons the forward permanently.** The
relay container ran `apk add socat` at startup, so for ten seconds the published port existed with
nothing behind it. Every connection from macOS afterwards was accepted and dropped — `nc -z` reported
the port open, the VM-side listener was there, and socat never saw a single accept. A fresh port with a
prebuilt image worked immediately. Build the relay image with `socat` already in it:

```
FROM alpine
RUN apk add --no-cache socat
```

This is the same trap as the stale fixture holding port 55000 in the eVDriveFlow run, one layer down:
the port answers, so nothing looks broken.

## How to reproduce

```bash
docker network create --ipv6 --subnet fd00:beef::/64 v2gnet
docker run -d --name mqtt --network v2gnet eclipse-mosquitto:2 mosquitto -c /mosquitto-no-auth.conf
docker run -d --name everest --platform linux/amd64 --network v2gnet \
  --entrypoint /bin/sh ghcr.io/everest/everest-demo/manager:main -c "sleep infinity"

# the same config as the previous run — device eth0 for their charger, lo for their EV
docker cp ../2026-08-02-everest-iso2-dc-notls/config-ours.yaml everest:/workspace/dist/etc/everest/
docker exec -d everest sh -c "cd /workspace/dist && MQTT_SERVER_ADDRESS=mqtt MQTT_SERVER_PORT=1883 \
  ./bin/manager --conf /workspace/dist/etc/everest/config-ours.yaml > /tmp/everest.log 2>&1"

# authorize over MQTT, and record their side of the session while we are at it
docker cp ../../../tools/interop-everest/mqtt-authorize.sh mqtt:/tmp/
docker exec -d mqtt sh -c "/tmp/mqtt-authorize.sh > /tmp/auth.log 2>&1"

# relay their link-local listener to a port the Mac can reach — socat already in the image, see above
docker run -d --name ev-relay --network v2gnet -p 15130:15120 v2g-socat \
  socat -d -d TCP4-LISTEN:15120,fork,reuseaddr 'TCP6:[fe80::…%eth0]:61341'

V2G_INTEROP_SECC=127.0.0.1:15130 V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RECORD=/tmp/everest-mqtt-run \
V2G_INTEROP_SCENARIO=$PWD/ISO15118ConformanceTests.Simulation/Vectors/Session.iso2-dc-eim.trace.json \
  dotnet test ISO15118ConformanceTests.Simulation -c Release \
    --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

Restart the manager between attempts — the second session crashes it.

The `*.trace.json` the recorder built is **not** checked in: 47 exchanges of which 35 are the same
`CableCheckReq`, and the one interesting frame in it is the `FAILED`. That frame is arguably worth
having in the corpus, since nothing in `Vectors/` contains a station-produced `FAILED` — but adding a
foreign station's message to the corpus is a deliberate decision, not a side effect of a run.

## Next

- ✅ **Simulate the plug** so the cable check can pass — done the same day, and it produced a complete
  charge: [`../2026-08-02-everest-iso2-dc-full-charge/`](../2026-08-02-everest-iso2-dc-full-charge/notes.md).
  The route was `car_simulator`'s external MQTT interface, and `cp C` was indeed not enough: the
  command that holds the CP line at 6 V is `draw_power_fixed 0,0`.
- ~~Report the second-session crash to EVerest~~ — **withdrawn 2026-08-03**: it does not reproduce on
  everest-core 2025.10. Five reproductions on 2023.10.0, none on the current release.
- **Run every future session twice**, in every harness. One session is not a test of a station.
- Then `config-sil-dc-d20.yaml` (`Evse15118D20`) — the -20 charger, and the one this stack has the most
  to say about — `config-sil-dc-isomux.yaml`, and eventually `config-sil-mcs.yaml`.
