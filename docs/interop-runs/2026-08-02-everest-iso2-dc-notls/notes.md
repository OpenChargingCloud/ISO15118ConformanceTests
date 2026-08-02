# 2026-08-02 — EVerest `EvseV2G`, ISO 15118-2 DC, no TLS

**The deepest session against a non-Josev counterparty so far, and a finding about our own EVCC.** Our
car negotiated five phases with EVerest's charger and then polled authorization 1 170 times without
ever giving up — because nothing in our -2 EVCC bounds an `EVSEProcessing = Ongoing` loop.

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) via `ghcr.io/everest/everest-demo/manager:main`, `EvseV2G` |
| Ours | `Vanaheimr.V2G.Exi` @ `b111842` |
| Direction | our EVCC → their charger |
| Session | ISO 15118-2 DC, plain TCP, their `config-sil-dc.yaml` with two edits (below) |
| Outcome | **five phases negotiated, then an unbounded authorization poll** |
| Artifacts | [`flow.md`](flow.md) (abridged), [`their-charger.log`](their-charger.log) |

## The session

```
0  SupportedAppProtocolReq        → SupportedAppProtocolRes    OK_SuccessfulNegotiation
1  SessionSetupReq                → SessionSetupRes            OK_NewSessionEstablished
2  ServiceDiscoveryReq            → ServiceDiscoveryRes        OK
3  PaymentServiceSelectionReq     → PaymentServiceSelectionRes OK
4… AuthorizationReq × 1170        → AuthorizationRes           OK, EVSEProcessing = Ongoing
```

1 174 request frames, 1 174 responses, every one of them `OK`. The recorder even built a
`SessionTrace` — strictly alternating and untruncated, and worth nothing as a corpus entry, which is
why it is not checked in here.

**Four message pairs of ISO 15118-2 exchanged cleanly with an implementation that runs on real
chargers.** `EvseV2G` sits on cbV2G, the same encoder our vector corpus is generated from, so that is
not an independent codec result — but the sequencing, the framing and the field semantics are, and they
held.

## The finding — ours: no bound on an "Ongoing" poll

Their station answered `AuthorizationRes` with `ResponseCode = OK` and `EVSEProcessing = Ongoing`,
correctly and indefinitely: nothing had authorized the session, so there was nothing else it could say.
Our EVCC polled until the fixture's own 3-minute budget ran out.

```csharp
while ((await Send<AuthorizationResType>(authReq, ct, authSignature))
           .EVSEProcessing != EVSEProcessing.Finished)
```
(`Vanaheimr.V2G.Simulation/StateMachines/Iso2/Evcc2.cs`)

No counter, no deadline. The same shape appears at two more places in the same file — the
charge-parameter-discovery and cable-check/pre-charge polls — and the -20 EVCC's poll loops are built
the same way.

ISO 15118-2 has a timer for exactly this: the EVCC's *ongoing* timeout, which ends a phase that stays
`Ongoing` too long instead of waiting for a station that will never finish. We have the
per-**message** timeout (5 s here, and every single response arrived well inside it) and the caller's
cancellation token, and nothing in between. A real car in this situation polls until somebody unplugs
it.

**Why nothing here could have found it.** Our own SECC always answers `Finished` within a poll or two,
so no loopback test and no recorded session ever contains a station that keeps saying `Ongoing`. It is
the same blind spot as the FAILED response codes, one layer along: the corpus can only contain
behaviour our own station exhibits.

**Fixed on 2026-08-02, in both protocols and all three languages.** `OngoingGuard` is a per-phase
deadline — 60 s by default, ISO 15118's EVCC ongoing timeout — checked once per poll in the
authorization, cable-check and charge-parameter loops. The error names the phase and how long it
actually waited, because that is the line a live run is read from.

One deliberate difference between the implementations, documented at all three: C# reads the session's
injected `TimeProvider`, so a pinned-clock replay pins this too; Kotlin and Swift have no clock
parameter on their `Evcc2` and use a monotonic wall clock instead. The measured quantity — real time
spent waiting for a peer — is the same.

The tests needed a station our own SECC cannot be. In C# that meant answering the authorization poll
directly rather than through `Secc2`/`Secc20Dc`, whose sequence guards reject a second
`AuthorizationReq` — correctly, since a station that authorizes normally has moved on by then. That
detail is itself worth keeping: the thing being reproduced is precisely a station that never moves on.

## Why their station never finished authorizing

Not a defect on their side — a consequence of the setup, and worth writing down so nobody reads it as
one. EVerest authorizes a session when a token arrives: `DummyTokenProvider` publishes `TOKEN1` in
reaction to the *EvseManager's* session events, which start when a car plugs in. Our EVCC arrives over
TCP without any of that, so the connector was never authorized.

Setting `car_simulator.auto_exec: true` (so their simulated car plugs in) did not change it — their
plug sequence is written for AC (`iec_wait_pwr_ready`, `draw_power_regulated 16,3`) while this is a DC
configuration. Driving the authorization properly means either their API module over MQTT or a DC-shaped
command sequence, and that is the first thing to fix for the next run.

## Setup

`ghcr.io/everest/everest-demo/manager:main` carries a full EVerest install (`/workspace/dist`, with
`bin/manager` and all the `config-sil-*.yaml`). It is amd64; on an ARM host the qemu registration from
the tux-evse run applies here too. Everything below ran under emulation — config loading alone took
8.7 s and module startup 45 s, so the timeouts in the fixture matter more than usual.

Two edits to `config-sil-dc.yaml`, both recorded in the artifacts:

1. `iso15118_charger.device: auto` → **`eth0`**, and `iso15118_car.device: auto` → **`lo`**. Their own
   `PyEvJosev` stays in the module graph (removing it breaks `car_simulator`'s required `ev`
   connection) but can no longer reach the charger, so the V2G session is ours.
2. `car_simulator.auto_exec: false` → `true` in the second attempt, to try to trigger authorization.
   It did not help; see above.

Their charger then listens on **two** ports, which is worth knowing:

```
TCP server on eth0 is listening on port [fe80::7821:a5ff:fec7:dc60%2]:61341
TLS server on eth0 is listening on port [fe80::7821:a5ff:fec7:dc60%2]:64109
SDP socket setup succeeded
```

`tls_security: allow` means both are open at once and SDP advertises whichever the EV asks for. For a
plain run, relay 61341.

```bash
docker run -d --name mqtt --network v2gnet eclipse-mosquitto:2 mosquitto -c /mosquitto-no-auth.conf
docker run -d --name everest --platform linux/amd64 --network v2gnet \
  --entrypoint /bin/sh ghcr.io/everest/everest-demo/manager:main -c "sleep infinity"
docker cp config-ours.yaml everest:/workspace/dist/etc/everest/
docker exec -d everest sh -c "cd /workspace/dist && MQTT_SERVER_ADDRESS=mqtt MQTT_SERVER_PORT=1883 \
  ./bin/manager --conf /workspace/dist/etc/everest/config-ours.yaml > /tmp/everest.log 2>&1"

# their listener is a link-local; relay it to a port the Mac can reach
docker run -d --name ev-relay --network v2gnet -p 15120:15120 <image-with-socat> \
  socat TCP4-LISTEN:15120,fork,reuseaddr 'TCP6:[fe80::…%eth0]:61341'

V2G_INTEROP_SECC=127.0.0.1:15120 V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RECORD=/tmp/ev-run \
V2G_INTEROP_SCENARIO=../../Vanaheimr.V2G.Simulation.Tests/Vectors/Session.iso2-dc-eim.trace.json \
  dotnet test ../../Vanaheimr.V2G.Simulation.Tests -c Release \
    --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

The relay path held again — no veth pairs, no zones, no multicast on our side. Unlike tux-evse, nothing
about the image had to be repaired: it runs as published.

## Next

- **Decide on the ongoing-poll bound**, in -2 and -20 and all three languages.
- **Authorize the session properly** — their API module over MQTT, or a DC plug sequence — and the run
  should continue into ChargeParameterDiscovery, CableCheck, PreCharge and the charge loop, which is
  where the *station → EV* half of the flow comparison finally has something to say.
- Then `config-sil-dc-d20.yaml` (`Evse15118D20`), `config-sil-dc-isomux.yaml`, and eventually
  `config-sil-mcs.yaml` — the first live counterpart our MCS support would ever have had.
