# 2026-08-03 — EVerest `IsoMux`: one endpoint, both protocols

**Two complete DC charges through the same TCP port, in the same process, minutes apart** — one in
ISO 15118-2 and one in ISO 15118-20, routed by their multiplexer on what our car offered in
`SupportedAppProtocolReq`. This is the closest thing to a real charger this project has run against: in
the field a station does not know which protocol is about to arrive.

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2025.10.0**, `IsoMux` in front of `EvseV2G` and `Evse15118D20` |
| Image | `ghcr.io/everest/everest-demo/manager@sha256:5b0136c31a9f4be985df313b5b1d2e90464d00b203f63613199657f2697ce097` |
| Ours | `Vanaheimr.V2G.Exi` @ `fb57470` |
| Session | DC, plain TCP, [`config-mux-ours.yaml`](config-mux-ours.yaml) — their `config-sil-dc-isomux.yaml` with two device lines changed |
| Outcome | **-2: 52/52 `OK`. -20: 105/105 `OK`. Both routes match our own recorded sessions exactly.** |
| Artifacts | [`flow.iso2.md`](flow.iso2.md) / [`frames.iso2.log`](frames.iso2.log), [`flow.iso20.md`](flow.iso20.md) / [`frames.iso20.log`](frames.iso20.log), [`their-charger.log`](their-charger.log), both traces |

## What the multiplexer does, in its own words

`IsoMux` terminates the SupportedAppProtocol handshake itself, reads the offered namespace, and hands
the connection to whichever backend implements it. Both decisions are in their log:

```
02:35:57  Incoming connection on eth0 …
02:35:57  Handling SupportedAppProtocolReq
02:35:57  handshake_req: Namespace: urn:iso:15118:2:2013:MsgDef, Version: 2.0, SchemaID: 1, Priority: 1
02:35:57  iso15118_2:Evse   :: Protocol negotiation was successful. Selected protocol is ISO15118
…
02:36:39  Incoming connection on eth0 …
02:36:39  handshake_req: Namespace: urn:iso:std:iso:15118:-20:DC, Version: 1.0, SchemaID: 1, Priority: 1
02:36:40  iso15118_20:Evs   :: Selected DC service parameters: control mode: Scheduled, …
```

The second session's ISO 15118-2 backend was never involved, and vice versa. From our side nothing
changed except `V2G_INTEROP_PROTOCOL`; the endpoint, the port and the process were identical.

Their two backends sit on `lo` behind the mux with `enable_sdp_server: false`, and the mux owns the
outward-facing interface, its SDP server and both listeners (TCP 61342, TLS 64110). So the layout is
also the answer to a question the harness README carried: the relay flattens a station to one port, and
here one port is what the station really is.

## What it proves, and the one thing it cannot

**Proves:** our EVCC's SupportedAppProtocol offer is unambiguous enough for a real multiplexer to route
on, in both protocols, and both of our -2 and -20 state machines complete a charge against the backend
it picks. Neither had met a station that had a *choice* before — every previous run was against a
charger that spoke exactly one protocol and could not have mis-routed us.

**Cannot:** our EVCC offers one protocol per session, so the mux never had to *choose* between two
offers in one handshake. That is the more interesting case — a car offering -20 with priority 1 and -2
with priority 2 — and it needs an EVCC that can offer both and then run whichever came back. Ours
cannot; the state machine is selected before the handshake. Named here rather than implied by the
result, and it is the same shape as the Dynamic gap found yesterday: a capability that reads as present
because both halves exist separately.

> **Closed the same day, and the answer was not the expected one.** Our EVCC learned to offer both, and
> the rerun found that `IsoMux` **does not read `Priority` at all** — it routes to -20 whenever -20
> appears anywhere in the offer, so an EV ranking -2 first still lands on the -20 backend. See
> [`../2026-08-03-everest-isomux-both/`](../2026-08-03-everest-isomux-both/notes.md).

## Two small things in their log

**Their peer address is rendered wrong.** `Incoming connection on eth0 from [a00:9b48:0:0:fe80::]:39752`
— the words of the `sockaddr_in6` are printed in the wrong order (the `fe80::` prefix ends up at the
tail). Cosmetic, but it is the field one reads to tell two cars apart.

**The mux warns about its own certificate at startup**, with TLS never used in this run:

```
<n> certificates != <n> OCSP responses
No trust anchors for certificate: C:DE CN:SECCCert DC:CPO O:EVerest
trusted_ca_keys support disabled
```

Worth knowing before a `config-sil-dc-isomux-tls.yaml` run: the mux holds the SECC leaf but no trust
anchors, which is consistent with what the [TLS 1.3 run](../2026-08-03-everest-iso20-dc-tls13/notes.md)
found from the other end — their station sends only its leaf.

## How to reproduce

Setup as in the [-20 run](../2026-08-03-everest-iso20-dc-full-charge/notes.md), with
`config-mux-ours.yaml` instead. Two device lines differ from their shipped file: the mux moves from
`auto` to `eth0`, their `PyEvJosev` from `auto` to `lo`. The backends stay on `lo` as they ship them.

The mux binds its TCP port at startup and logs it, so **no SDP step is needed** — unlike a bare
`Evse15118D20`. It does take 61342 rather than 61341, and their `Evse15118D20` behind it still claims
`[::1]:50000`, which is worth knowing if a forwarder wants that port.

```bash
docker cp config-mux-ours.yaml everest:/ext/dist/etc/everest/
docker exec -d everest sh -c "cd /ext/dist && MQTT_SERVER_ADDRESS=mqtt MQTT_SERVER_PORT=1883 \
  ./bin/manager --conf /ext/dist/etc/everest/config-mux-ours.yaml > /tmp/everest.log 2>&1"
docker exec -d mqtt sh -c "CP_AT_PLUGIN=1 /tmp/sil-car.sh > /tmp/sil-car.log 2>&1"

# relay to the mux's port, then run the two sessions back to back
V2G_INTEROP_SECC=127.0.0.1:15141 V2G_INTEROP_PROTOCOL=2  V2G_INTEROP_MODE=dc  dotnet test …
# unplug / re-plug
V2G_INTEROP_SECC=127.0.0.1:15141 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc  dotnet test …
```

## Next

- **An EVCC that offers both protocols in one handshake**, so the multiplexer has a real choice to make.
- **`config-sil-dc-isomux-tls.yaml`**, now that the trust plumbing exists.
- **AC**, in both protocols.
