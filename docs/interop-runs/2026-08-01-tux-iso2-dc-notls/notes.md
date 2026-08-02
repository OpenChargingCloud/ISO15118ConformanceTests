# 2026-08-01 — tux-evse/iso15118-simulator-rs, ISO 15118-2 DC, no TLS

Our EVCC against their **responder**, from a Mac against an emulated amd64 container. Two exchanges,
one hard finding, four workarounds to get that far, and one thing I could not explain.

| | |
|---|---|
| Counterparty | [tux-evse/iso15118-simulator-rs](https://github.com/tux-evse/iso15118-simulator-rs), image `registry.redpesk.bzh/tux-evse/afb-iso15118:v0.1` (21 months old), `iso15118-simulator-rs-0.2`, `afb-binder 5.1.8` |
| Ours | `Vanaheimr.V2G.Exi` @ `8d59a43` |
| Direction | our EVCC → their responder |
| Session | ISO 15118-2 DC, plain TCP, their shipped `audi-dc-iso2-compact.json` |
| Outcome | **stopped at SessionSetupReq** — they refuse a request whose fields differ from the capture's |
| Artifacts | [`flow.md`](flow.md), [`frames.log`](frames.log), [`their-responder.log`](their-responder.log) |

## The session

```
0  SupportedAppProtocolReq → SupportedAppProtocolRes   OK_SuccessfulNegotiation
1  SessionSetupReq         → (no answer)
```

## The finding: their responder validates the *request*, field by field, against the capture

Their own log says it exactly:

```
-- rec:{"id":"[ab,cd,ef,01,02,03]","tagid":"session_setup_req","proto":"iso2","msgid":0}
-- exp:{"id":"[00,7d,fa,07,5e,4a]","tagid":"session_setup_req","proto":"iso2","msgid":0}
NOTICE: [REQ/API iso15118-responder] responder-req-fail:query check return invalid value:
        Err(id:jsonc-match invalid value received:"[ab,...
CRITICAL: [API iso15118-evse] responder::call_sync verb:evse:iso2:session_setup_req
```

`00:7D:FA:07:5E:4A` is the **captured Audi's EVCCID**. Ours is `AB:CD:EF:01:02:03`. Both are legal;
an EVCCID is the car's own identifier and no two cars share one. Their responder refuses to answer
because it is not the car in the recording.

This is the mirror image of the friction the harness README already warned about, and the more
serious half. That warning was about their `expect` blocks holding the captured *charger's* values, so
that **their** verdict on **our responses** is noisy. This is the other direction: in responder mode
the `query` block is matched against the **incoming request**, so a foreign EV is refused at the first
message that carries an identifier of its own — which is the first message after the handshake.

**What it means for this counterparty.** With a shipped scenario, their responder answers the capture
and nothing else. To use it as a station for a foreign EV, every `query` field that the EV chooses for
itself has to be relaxed or rewritten. Patching just the EVCCID (tried, see below) is the first of an
unknown number of such fields — the same wall would appear at ServiceDiscovery, PaymentServiceSelection
and every request carrying our own values.

The README's advice to prefer their `strong` compaction mode does **not** help here: compaction governs
which requests are *played* on the injector side, not how strictly a received request is matched.

## Four workarounds needed before a single byte crossed

Each of these is a fact about the published artifact, not about ISO 15118.

1. **Their container image is incomplete.** It is a single 35 MB layer over a base
   (`FROM 98072c178779`) that the registry does not ship. There is no `/bin`, no `/tmp`, no
   coreutils — `docker run … sh` fails with *executable file not found*, and so does `/bin/bash`.
   Everything they built is present under `/usr/bin` (`afb-binder`, `binding-start-evse`,
   `binding-start-evcc`, `pcap-iso15118`, `bash`, `grep`) plus `/usr/redpesk/…/lib/*.so`, so the image
   is usable with an explicit `--entrypoint /usr/bin/bash`. A log file needs a mounted volume; there is
   nowhere writable otherwise.
2. **It is amd64 only.** On an ARM host, `docker run --privileged --rm tonistiigi/binfmt --install amd64`
   registers qemu inside the VM and their binder runs — slowly but correctly.
3. **`binding-start-evse` hardcodes `export IFACE_SIMU=evse-veth`**, so their own network script is
   effectively mandatory even when a plain container interface would do. Calling `afb-binder` directly
   with `IFACE_SIMU=eth0` works — the exact command is printed in their own startup log.
4. **`autorun: 0` in the shipped scenario means the responder answers nothing.** The TCP server listens,
   accepts, and closes. With `autorun: 1` the session runs. This is the single most important line to
   change for an automated run, and it is not in their README, whose workflow is to open the devtools UI
   and drive it by hand.

## What I could not explain

After the one session above, every further connection was **accepted and immediately closed**, with a
single line in their log:

```
DEBUG: [API libafb_sim15118_evse.so] async-tcp-client: closing tcp:[fe80::…%2]:42314
```

A fresh binder in a fresh container behaved the same way — one session's worth of answers at most, and
not reliably even that. Their API has a `reset` verb documented as *"scenario sequence counter"*;
calling it (`/api/iso15118-responder/reset`, which returns success) did **not** restore the behaviour.
Restarting the binder did not reliably restore it either.

So the reproduction below is honest about being unreliable: it produced the two exchanges once, and
several identical attempts afterwards produced only the handshake or nothing. Whether that is a
one-shot scenario, a state machine left wedged by the failed request match, or an artefact of running
their Rust binder under qemu, I do not know — and guessing would be worth less than the gap.

## The other half of this run: their scenario format, checked for real

Independent of the session, this run put **their actual files** in front of the parser that was written
from their README:

- `TuxEvseScenario` and `tools/interop-tux-evse/scenario-expectations.py` read
  `audi-dc-iso2-compact.json` correctly: 1 scenario, 27 transactions, verbs `iso2:sdp_evse_req`,
  `iso2:app_proto_req`, `iso2:session_setup_req`, … exactly the structure the harness assumed. **No
  unknown verbs.**
- The probe's verdict on the real file: **22 of 24 compared responses carry at least one field the
  captured charger chose for itself.** The harness's central warning is confirmed by their own shipped
  scenario rather than by a reconstruction of it.
- Their image also ships `audi-dc-iso2-full.json`, `audi-dc-iso2-minimal.json`, `tesla-3-din.json` and
  two pcaps (`audi-dc-iso2.pcap`, `tesla-3-din.pcap`) — worth knowing for a later run, and the DIN one
  is the only DIN 70121 material this project has seen.

## How to reproduce

```bash
docker run --privileged --rm tonistiigi/binfmt --install amd64          # once, ARM hosts only
docker network create --ipv6 --subnet fd00:beef::/64 v2gnet             # if it does not exist
docker run -d --name tux-evse --platform linux/amd64 --network v2gnet -v tuxlogs:/logs \
  --entrypoint /usr/bin/bash registry.redpesk.bzh/tux-evse/afb-iso15118:v0.1 \
  -c "while :; do read -t 3600; done"

# their scenario with autorun enabled
python3 -c "import json;d=json.load(open('audi-dc-iso2-compact.json'));d['binding'][0]['autorun']=1;json.dump(d,open('audi-autorun.json','w'))"
docker cp audi-autorun.json tux-evse:/logs/

docker exec -d tux-evse /usr/bin/bash -c "export IFACE_SIMU=eth0 SIMULATION_MODE=responder \
  SCENARIO_UID=evse CARGO_BINDING_DIR=/usr/redpesk/iso15118-simulator-rs/lib \
  INJECTOR_BINDING_DIR=/usr/redpesk/injector-binding-rs/lib; \
  /usr/bin/afb-binder -vvv --name=afb-evse \
    --config=/usr/share/iso15118-simulator-rs/binding-simu15118-evse-no-tls.yaml \
    --config=/logs/audi-autorun.json > /logs/evse.log 2>&1"

# their listener is a link-local on the container's eth0, port 61341 (fixed, in their YAML) —
# read it from the log, then relay it to a port the Mac can reach
docker run -d --name tux-relay --network v2gnet -p 15119:15119 <any-image-with-socat> \
  socat TCP4-LISTEN:15119,fork,reuseaddr 'TCP6:[fe80::…%eth0]:61341'

V2G_INTEROP_SECC=127.0.0.1:15119 V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RECORD=/tmp/tux-run V2G_INTEROP_SCENARIO=<their audi-dc-iso2-compact.json> \
  dotnet test ../../Vanaheimr.V2G.Simulation.Tests -c Release \
    --filter "FullyQualifiedName~TuxEvseInteropTests.OurEvcc"
```

The relay path documented in [`../../../tools/interop-tux-evse/README.md`](../../../tools/interop-tux-evse/README.md)
held up again: no veth pairs, no zones and no multicast on our side, and their bridge script was never
run at all. Their `tcp_port: 61341` is fixed in `binding-simu15118-evse-no-tls.yaml`, so the port did
not even have to be discovered — the README's `ss` step is unnecessary for the responder.

## Next

- **Relax their `query` matching** — rewrite every field a foreign EV chooses for itself, or find
  whether their matcher supports a wildcard. That is the gate on everything past SessionSetup.
- **Ask them about the one-shot behaviour**, or read `afb-evse/src/verbs.rs` — this is the kind of
  question a first contact is supposed to produce.
- The **reverse** direction (their injector against our SECC) is untouched, and is where their captured
  Audi would be driving *our* station — the direction their design actually favours, and the one this
  finding does not block, since there the field matching applies to responses they can be told to
  ignore.
