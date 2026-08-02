# EVerest interop (Tier 2)

Interop between **our** EVCC/SECC and **[EVerest](https://github.com/EVerest/everest-core)** (Apache-2.0),
the Linux Foundation Energy charging stack.

Like the other three harnesses this is **opt-in and never part of the offline CI**. The automated hook is
the `[Explicit] [Category("Interop")]` fixture
[`Vanaheimr.V2G.Simulation.Tests/Interop/EverestInteropTests.cs`](../../Vanaheimr.V2G.Simulation.Tests/Interop/EverestInteropTests.cs),
gated on environment variables — `dotnet test -c Release` skips it entirely.

*Written against `everest-core` as of 2026-08-01. Lines marked **confirm on first contact** could not be
checked from their documentation and are questions for the first run, not statements.*

---

## Why this one, and what is actually new in it

**"Works against EVerest" is closer to a market claim than to a test result.** It is the implementation
most likely to be on the other end of a real charger, and that is a different kind of reason from the
other three — Josev gives an independent codec, eVDriveFlow a second one plus Dynamic -20, tux-evse a real
car's captured route. EVerest gives the field.

Only one half of it is new to us, and it is worth being precise about which:

| Their module | What it is | New to us? |
|---|---|---|
| `modules/EVSE/EvseV2G` | DIN 70121 + ISO 15118-2 charger, C, **cbV2G** underneath | **yes** — a station nothing here has met |
| `modules/EVSE/Evse15118D20` | the ISO 15118-**20** charger | **yes** |
| `modules/EVSE/IsoMux` | multiplexes the two, so one charger answers both | yes |
| `modules/EV/PyEvJosev` | the car — the **Josev**-derived Python stack | **no**: same implementation family as `docs/interop-runs/` already used |

So the **forward** direction (our EVCC → their charger) is where the findings will be, and the flow
report's *station → EV* half is where to look. A green reverse run against `PyEvJosev` is much less news:
it is Josev in a different wrapper.

And because `EvseV2G` sits on **cbV2G** — the encoder our own vector corpus is generated from — a
disagreement there is **not** an EXI disagreement by construction. It is sequencing, timing or semantics,
which is exactly the class a corpus of single messages cannot see. (For independent bytes the
counterparties are Josev and eVDriveFlow.)

### Where the -20 SECC lives now

The counterparty list carried this as an open question: `libiso15118` was **archived on 2026-02-26** and
folded into `everest-core`. It is **`modules/EVSE/Evse15118D20`**, and the SIL configurations that use it
are `config/config-sil-dc-d20.yaml` and `config/config-sil-ac-d20.yaml`.

---

## The short path: a TCP relay, no discovery

**Read this before the setup below, and note that it lines up with where this counterparty's value is
anyway** — the forward direction, against their charger.

After the SupportedAppProtocol handshake an ISO 15118 session is a plain TCP stream. The only part that
needs interfaces, zones and multicast is **SDP**, and the Josev harness has always skipped it,
connecting to `host:port` directly.

```bash
# on the machine running EVerest — their SECC's TCP port is assigned, not configured, so read it off
ss -6 -tlnp | grep -i v2g          # or take it from the SDP response / EvseV2G's log
socat TCP6-LISTEN:15118,fork,reuseaddr 'TCP6:[fe80::…%eth0]:<their-port>'
```

```bash
# from anywhere that can reach it, including a Mac
./live-evcc-iso2-dc.sh '' vm.local:15118            # -2
./live-evcc-iso2-dc.sh '' vm.local:15118 20         # -20, against Evse15118D20
```

No zone, no multicast, no interface names on our side; `--connect` and `V2G_INTEROP_SECC` take an
ordinary `host:port`.

**Why this fits EVerest particularly well.** The half of it that is new to us is the station, and the
station is exactly what a relay can put in front of you. The reverse direction — `PyEvJosev` against
our SECC — is the half that is Josev in a wrapper, so the direction the relay *cannot* cover is also
the one worth doing last.

**What this does not do.**

- **Only the forward direction**, for the reason above: in a reverse run their EV is the one
  discovering, and a relay cannot tell it where to look. That needs SDP on a shared link.
- **SDP is not exercised** — a covered loss; every recorded Josev run drives `--sdp` both ways.
- **`IsoMux` is worth doing on the real topology eventually**, since a single endpoint answering both
  -2 and -20 is a discovery-adjacent behaviour and the relay flattens it to one port.
- **TLS through a relay is untested here.** Transparent unless a certificate is bound to the address it
  was reached at; do the `tls_security: prohibit` runs through the relay first.

Their charger still has to run, so the setup below still applies to *their* side.

## Setup

Nothing here is installed for you; `everest-core` is a large CMake project with its own dependency
manager (`edm`). Follow their getting-started guide at <https://everest.github.io>. The harness assumes
you end up with a build that can run their SIL ("software in the loop") configurations.

### The configurations that matter

Their `config/` directory carries the whole matrix. The ones this harness is built around:

| Config | Session |
|---|---|
| `config-sil-dc.yaml` | -2 DC, the plain starting point |
| `config-sil.yaml` | -2 AC |
| `config-sil-dc-tls.yaml` | -2 DC over TLS |
| `config-sil-dc-d20.yaml` | **-20 DC** (`Evse15118D20` + `PyEvJosev` with TLS 1.3) |
| `config-sil-ac-d20.yaml` | -20 AC |
| `config-sil-dc-isomux.yaml`, `-isomux-tls.yaml` | one charger answering both -2 and -20 |
| `config-sil-mcs.yaml` | **MCS** — see below |
| `config-sil-dc-sae-v2g.yaml`, `-v2h.yaml` | SAE bidirectional profiles |

**MCS is worth noting.** Our own roadmap records MCS (service ids 8/9) as implemented but *"untested
against a live counterpart"*. `config-sil-mcs.yaml` is the first live counterpart in sight for it. Out of
scope for a first contact, and the reason to come back.

### The two settings that decide whether a run can work

| Module | Key | For interop |
|---|---|---|
| `EvseV2G` | `device` (default `eth0`) | "any local interface that has an ipv6 link-local and a MAC addr". Must be the one we are on |
| `EvseV2G` | `enable_sdp_server` (default `true`) | leave it on: it is how our EVCC finds their charger |
| `EvseV2G` | `tls_security` (`prohibit` \| `allow` \| `force`) | start at `prohibit`, so a first failure cannot be the handshake |
| `PyEvJosev` | `device` | same interface; their EV finds a station by SDP on it |
| `PyEvJosev` | `supported_ISO15118_2`, `supported_ISO15118_20_DC`, … | all default **false** — a car that supports nothing negotiates nothing |

`EvseV2G` also has `tls_key_logging` / `tls_key_logging_path`, which writes the pre-master secret for
Wireshark. Turn it on for any TLS run; it is the difference between reading a session and guessing at it.

Their `EvseManager` in the SIL configs uses EVSE ID `DE*PNX*E12345*1` — the same identifier that appears
in tux-evse's captured scenario, because both come from the common Trialog/PNX test material. Useful to
recognise; not evidence of anything.

---

## Running

### Our EVCC → their charger  ([`live-evcc-iso2-dc.sh`](live-evcc-iso2-dc.sh))

**The direction worth the setup.**

```bash
./live-evcc-iso2-dc.sh eth0                        # SDP-discover their EvseV2G
./live-evcc-iso2-dc.sh eth0 '[fe80::…%eth0]:15118' # or connect to a known endpoint
./live-evcc-iso2-dc.sh eth0 '' 20                  # -20, against Evse15118D20
```

Through the fixture, which records the run and compares both directions of the flow against one of our
own recorded sessions:

```bash
V2G_INTEROP_SECC='[fe80::…%eth0]:15118' V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RECORD=/tmp/everest-run \
V2G_INTEROP_SCENARIO=../../Vanaheimr.V2G.Simulation.Tests/Vectors/Session.iso2-dc-eim.trace.json \
  dotnet test ../../Vanaheimr.V2G.Simulation.Tests -c Release \
    --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

Read the **station → EV** section of `flow.md` first. What our car sends is ours and already pinned by the
corpus; what their charger answered is the thing no test here has ever seen.

**Run it twice.** Their `EvseV2G` segfaults on the second V2G session in the same process — see
[Known friction](#known-friction-expect-these-first) — and a harness that only ever opens one connection
cannot see that. Every station is worth two sessions.

### Driving their hardware simulation  ([`sil-car.sh`](sil-car.sh))

**Use this one for a complete charge.** It plugs the simulated car in, which authorizes the session the
way a real car does, and moves the CP line to state C when the station starts its cable check — the two
things a V2G peer arriving over TCP cannot do for itself, and without which every forward run ends at
`CableCheck` with `FAILED`. With it, our EVCC ran an ISO 15118-2 DC session end to end against
`EvseV2G`: [`2026-08-02-everest-iso2-dc-full-charge`](../../docs/interop-runs/2026-08-02-everest-iso2-dc-full-charge/notes.md).

```bash
docker cp sil-car.sh mqtt:/tmp/
docker exec -d mqtt sh -c "/tmp/sil-car.sh > /tmp/sil-car.log 2>&1"
until docker exec everest sh -c "grep -q 'Set PWM On' /tmp/everest.log"; do sleep 2; done   # then connect
```

Their car simulator takes bare command strings on `everest_external/nodered/<connector_id>/carsim/cmd/…`
— no envelope. Three of its habits decide what those strings must look like, and the script's header
comment spells each one out: a command list that runs out **resets the car to unplugged**;
`execute_charging_session` is refused while one is running and resets the simulation when accepted,
while `modify_charging_session` does neither; and `cp C` is overwritten on the next tick, so the state
that holds 6 V is `draw_power_fixed 0,0`.

### Authorizing a session with no hardware at all  ([`mqtt-authorize.sh`](mqtt-authorize.sh))

The shorter path when the station only needs to talk — no plug-in, no contactor, and the run will still
stop at `CableCheck`. **Without either script a forward run stops at `Authorization` and never leaves
it.** EVerest authorizes when a
token arrives, and in the SIL configs the token comes from `DummyTokenProvider`, which is wired to
`EvseManager`'s *plug-in* events. Our EVCC arrives over TCP and plugs nothing in, so the station answers
`EVSEProcessing = Ongoing` for ever — correctly. That is what the 2026-08-02 run recorded, 1 170 times.

The script publishes the same `ProvidedIdToken` on the same topic their own provider uses, triggered by
the HLC instead of by hardware — `EvseV2G` sets `Require_Auth_EIM` the moment the EV has selected EIM
and sent `AuthorizationReq`. Nothing in EVerest is patched, and their `Auth` module cannot tell the
difference.

```bash
docker cp mqtt-authorize.sh mqtt:/tmp/
docker exec -d mqtt sh -c "/tmp/mqtt-authorize.sh > /tmp/auth.log 2>&1"   # before the session
```

Timing matters and the trigger gets it right for free: `connection_timeout` (10 s in the SIL configs)
withdraws the authorization if no transaction starts, so a token published before the EV connects is
already gone by the time it polls.

The script also logs every V2G message their charger publishes — a station-side record of the session.
Trust the message **names**, not the bytes: the responses they publish carry the preceding *request's*
V2GTP length, so each one is truncated or padded with stale buffer. Requests are byte-exact.

**The topic scheme**, since their documentation does not carry it:

| | |
|---|---|
| published variable | `everest/<module_id>/<impl_id>/var` — `{"data": <value>, "name": "<var>"}` |
| command call | `everest/<module_id>/<impl_id>/cmd` — `{"data": {"args": {…}, "id": "<uuid>", "origin": "<caller>"}, "name": "<cmd>", "type": "call"}` |

Module ids are the **keys in the config file**, not the module types. `mosquitto_sub -v -t 'everest/#'`
against their broker is the fastest way to learn any wiring this README does not cover.

### Their PyEvJosev → our SECC  ([`reverse-iso2-dc.sh`](reverse-iso2-dc.sh))

```bash
./reverse-iso2-dc.sh eth0 55000
```

Their EV module's `device` is documented as any interface with a link-local address, and it discovers a
station by SDP — so it is **not** bound to EVerest's own charger, which is what makes this direction
possible. That answers the question the counterparty list carried as open.

**Confirm on first contact:** whether a configuration containing only the EV-side modules
(`ev_manager` / `EvManager`, `iso15118_car` / `PyEvJosev`, `slac` / `SlacSimulator`, plus whatever they
require) can be assembled and started on its own. The SIL configs wire a whole charger alongside; if the
EV half cannot be cut out, the fallback is to run the full SIL config with `EvseV2G`'s `device` pointed at
an interface our station is not on, so their EV discovers ours instead. Ugly, and it works.

### Scenario order

1. ✅ **-2 DC, EIM, `tls_security: allow`** — forward. Done 2026-08-02: a complete charge, 36/36 `OK`.
2. **-20 DC** against `Evse15118D20` (`config-sil-dc-d20.yaml`), forward. **Next** — the same two
   hardware steps should apply, and no complete -20 session against a foreign station exists yet.
3. **-2 AC** (`config-sil.yaml`), forward. A different CP/PWM story: the plug-in sequence wants
   `iec_wait_pwr_ready` rather than the 5 % HLC mode.
4. **TLS** (`config-sil-dc-tls.yaml`), with `tls_key_logging: true`, now that there is a full plaintext
   session to compare against.
5. **IsoMux**, which is the closest thing to a real charger's behaviour: one endpoint answering both.
6. **MCS** — the first live counterpart our MCS support would ever have had.
7. **Reverse** with `PyEvJosev`, lower value (it is Josev in a wrapper) and the one the relay cannot
   cover, so last.

---

## Reading a run

There is no scenario file here — EVerest is a stack, not a replayer — so the reference for the flow report
is one of our own recorded sessions, exactly as for eVDriveFlow. The comparison answers "did this run take
the same route as ours", in **both** directions. For a station-side counterparty the station half is the
one that carries the news, and it is printed as its own section.

Artifacts as everywhere (`V2G_INTEROP_RECORD=<dir>`): raw octets per direction, `frames.log` with message
names and response codes, `flow.md`, and a replayable `*.trace.json` when the session was well-formed
enough to be one. See [`../../docs/interop-runs/README.md`](../../docs/interop-runs/README.md).

Write each run up under `docs/interop-runs/<yyyy-mm-dd>-everest-<scenario>/` with their commit, ours, the
config file used, and every divergence.

## Known friction (expect these first)

- **`PyEvJosev` supports nothing by default.** Every `supported_*` key defaults to `false`. A car that
  announces no protocol gets no session, and the symptom is an empty SupportedAppProtocol negotiation
  rather than an obvious error.
- **`device` on both sides.** Their modules bind an interface by name; ours takes it as `--interface`.
  All three must agree, and it must have an IPv6 link-local address.
- **Link-local addressing with zones**, as everywhere: write `[fe80::…%iface]:port`.
- **cbV2G lineage on their `EvseV2G`.** Do not reach for the vector corpus when something disagrees —
  the bytes are generated from the same encoder. Look at order and timing.
- **Their EV is Josev.** If a reverse run reproduces something already recorded under
  `docs/interop-runs/2026-07-2*`, that is not a new finding; check there first.

Confirmed on first contact, and no longer questions:

- **`EvseV2G` segfaults on the second V2G session in one process** (status 139, while handling
  `PaymentServiceSelectionReq`), and EVerest's manager then terminates every module — one crash takes
  the whole charger down. The first session is always fine, however short. **Restart the manager between
  runs**, and see [`2026-08-02-everest-iso2-dc-mqtt-auth`](../../docs/interop-runs/2026-08-02-everest-iso2-dc-mqtt-auth/notes.md).
- **`CableCheck` needs their hardware simulation.** `EvseManager` waits ~5 s for the board-support module
  to report the contactor closed and answers `FAILED` when it does not. In the SIL that contactor closes
  because the simulated car walks the CP line A→B→C; a V2G peer over TCP has no CP line, so a forward
  run stops there unless [`sil-car.sh`](sil-car.sh) drives it. Publishing `cp C` is **not** the way —
  the state machine rewrites `cp_voltage` from its own state on every tick.
- **Killing the manager orphans its modules.** They are separate processes and stay bound to port 61341.
  `pkill -f 'bin/manager'` leaves a half-dead charger answering connections; kill the process group or
  recreate the container.
- **On colima, publish a port only once its backend is listening.** A relay container that installs
  `socat` at startup leaves the published port empty for ten seconds, and the lima forward is poisoned
  for good afterwards: connections are accepted and silently dropped, `nc -z` says the port is open, and
  the container never sees an accept. Bake `socat` into the image.
