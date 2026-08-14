# EVerest interop (Tier 2)

Interop between **our** EVCC/SECC and **[EVerest](https://github.com/EVerest/everest-core)** (Apache-2.0),
the Linux Foundation Energy charging stack.

**This file is how to run it.** For what has already run and what each session caught — including the
defects it found in *this* project, which is most of them — see
[`docs/everest-cross-validation.md`](../../docs/everest-cross-validation.md).

Like the other three harnesses this is **opt-in and never part of the offline CI**. The automated hook is
the `[Explicit] [Category("Interop")]` fixture
[`ISO15118ConformanceTests.Simulation/Interop/EverestInteropTests.cs`](../../ISO15118ConformanceTests.Simulation/Interop/EverestInteropTests.cs),
gated on environment variables — `dotnet test -c Release` skips it entirely.

*Written against `everest-core` as of 2026-08-01. Lines marked **confirm on first contact** could not be
checked from their documentation and are questions for the first run, not statements.*

*Update 2026-08-05: the whole matrix was repeated against **everest-core 2026.02.1 built from source**
(WSL2, no demo image) — see
[`docs/interop-runs/2026-08-05-everest-2026021-matrix/`](../../docs/interop-runs/2026-08-05-everest-2026021-matrix/notes.md).
The demo-image ceiling no longer binds this harness; `everest-demo` still ships nothing newer than
`2025.10.0-patches`. Deltas that matter to this README: stock `config-sil-dc-d20.yaml` is now
**Dynamic-only** (re-enable `supported_scheduled_mode` in an `-ours` config), `config-sil-mcs.yaml`
**exists** in 2026.02.1, the unicast-SDP loop shutdown is **fixed** there while the refused-TLS-handshake
one is **not**, and `sdp-probe.sh`/`sil-car.sh` must run under **bash** (dash's `printf` has no `\x`).*

---

## Why this one, and what is actually new in it

**"Works against EVerest" is closer to a market claim than to a test result.** It is the implementation
most likely to be on the other end of a real charger, and that is a different kind of reason from the
other three — Josev gives an independent codec, eVDriveFlow a second one plus Dynamic -20, tux-evse a real
car's captured route. EVerest gives the field.

Only one half of it is new to us, and it is worth being precise about which:

| Their module | What it is | New to us? |
|---|---|---|
| `modules/EVSE/EvseV2G` | DIN 70121 + ISO 15118-2 charger, C. **cbV2G** underneath at HEAD — but **OpenV2G** in the demo image, see below | **yes** — a station nothing here has met |
| `modules/EVSE/Evse15118D20` | the ISO 15118-**20** charger | **yes** |
| `modules/EVSE/IsoMux` | multiplexes the two, so one charger answers both | yes |
| `modules/EV/PyEvJosev` | the car — the **Josev**-derived Python stack | **no**: same implementation family as `docs/interop-runs/` already used |

So the **forward** direction (our EVCC → their charger) is where the findings will be, and the flow
report's *station → EV* half is where to look. A green reverse run against `PyEvJosev` is much less news:
it is Josev in a different wrapper.

At `everest-core` HEAD, `EvseV2G` sits on **cbV2G** — the encoder our own vector corpus is generated
from — so a disagreement there would **not** be an EXI disagreement by construction: it would be
sequencing, timing or semantics, which is exactly the class a corpus of single messages cannot see.

> ⚠️ **Check what the image you are running actually links, before repeating that sentence.**
> `ghcr.io/everest/everest-demo/manager:main` is **everest-core 2023.10.0** (built 2023-12-05, per its
> own `release.json`) and its `EvseV2G` links **`libopenv2g.so.1`** — there is no libcbv2g anywhere in
> it. OpenV2G is a different codebase from chargebyte's cbexigen generator, so every run against that
> image *was* an independent-codec result, in both directions. The `:main` tag has simply not been
> rebuilt in years; newer tags (`2025.10.0-patches`, `2025.6.x-dt-esdp`, …) exist and are what to use
> for anything -20, which 2023.10.0 does not have at all.
>
> ```bash
> docker exec everest sh -c "ldd /workspace/dist/libexec/everest/modules/EvseV2G/EvseV2G | grep -i v2g"
> docker exec everest sh -c "head -c 200 /workspace/dist/etc/everest/release.json"
> ```

(For independent bytes the other counterparties are Josev and eVDriveFlow.)

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
| `config-sil-mcs.yaml` | **MCS** (`Evse15118D20` + `EvseManager` with `connector_type: cMCS`) — see below |
| `config-sil-dc-sae-v2g.yaml`, `-v2h.yaml` | SAE bidirectional profiles |

**MCS: run it.** This is the only live MCS counterpart this project has ever had, and it is the reason
`V2G_INTEROP_MODE=mcs` exists. Their `EvseManager`'s `connector_type: cMCS` is the line that makes the
station an MCS one: it hands `Evse15118D20` the energy-transfer modes `MCS` and (because their
`DCSupplySimulator` defaults to `bidirectional: true`) `MCS_BPT`, so the catalogue carries service ids
**8** and **9**. Three sessions ran complete on 2026-08-05 —
[`2026-08-05-everest-mcs`](../../docs/interop-runs/2026-08-05-everest-mcs/notes.md). Two things to know
before repeating it:

- Their stock MCS config, like their stock d20 one, **enables neither control mode**, and the module
  defaults to Dynamic-only — a Scheduled EVCC fails service selection against it. Re-enable both, as
  [`config-mcs-ours.yaml`](../../docs/interop-runs/2026-08-05-everest-mcs/config-mcs-ours.yaml) does.
- Their MCS SIL is **electrically an ordinary charger** (22 kW HLC limits, same fuse configuration as
  their plain -20 DC config). The run validates the service catalogue, not the power envelope.

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
V2G_INTEROP_SCENARIO=../../ISO15118ConformanceTests.Simulation/Vectors/Session.iso2-dc-eim.trace.json \
  dotnet test ../../ISO15118ConformanceTests.Simulation -c Release \
    --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

Read the **station → EV** section of `flow.md` first. What our car sends is ours and already pinned by the
corpus; what their charger answered is the thing no test here has ever seen.

**The MCS arm** is the same fixture with one variable, against a station brought up on
`config-sil-mcs.yaml`:

```bash
V2G_INTEROP_SECC=127.0.0.1:15200 V2G_INTEROP_MODE=mcs V2G_INTEROP_RECORD=/tmp/mcs-run \
  dotnet test ../../ISO15118ConformanceTests.Simulation -c Release \
    --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

`mcs` implies ISO 15118-20 — service ids 8/9 exist in no other catalogue — and a contradicting
`V2G_INTEROP_PROTOCOL` is refused rather than silently outranked. Add `V2G_INTEROP_DYNAMIC=1` for the
Dynamic arm. The fixture asserts that the negotiated service really was 8 or 9: `Evcc20Mcs` falls back to
a plain DC service when none is offered, by design, and a fallback that completed would otherwise be
written up as an MCS result.

Add **`V2G_INTEROP_BPT_FIRST=1`** to ask for the **bidirectional** entry of whichever catalogue the run
uses — AC_BPT (5), DC_BPT (6) or MCS_BPT (9) — and the assertion narrows to "a bidirectional service was
negotiated". It works on all three because it is a flag on the vehicle
(`Evcc20Base.PreferBidirectionalService`) rather than a subclass; while it was spelt
`V2G_INTEROP_MCS_FIRST=9` it reached MCS only, and their SIL's DC_BPT went untouched for a week of runs
([`2026-08-06-everest-bpt`](../../docs/interop-runs/2026-08-06-everest-bpt/notes.md)). The old spelling is
still honoured, because the run notes up to 2026-08-06 record it.
This now runs to `SessionStop` with the discharge half declared and their station logging
`Max discharge current 3000.000000A`. It took two app fixes to get there, both found by the run that
failed before them: the EVCC's service ranking used to be ignored in favour of the station's catalogue
order, and no `Evcc20*` built the `BPT_*` request types, so a BPT service was answered with charge-only
parameters and refused — `FAILED_WrongChargeParameter`. See
[`2026-08-06-everest-mcs-bpt-complete`](../../docs/interop-runs/2026-08-06-everest-mcs-bpt-complete/notes.md).

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

**It speaks all three versions, and it had to be taught the third.** On 2026.02.1 the variable name
became a topic level of its own, the payload gained a `msg_type` envelope with two levels of `data`,
and `ProvidedIdToken.id_token` became an object — any one of which turns the script into a silent
no-op. Carried forward and verified on 2026-08-10 with a control that shows the previous version
authorizing nothing against the same station:
[`2026-08-10-everest-mqtt-authorize-2026021`](../../docs/interop-runs/2026-08-10-everest-mqtt-authorize-2026021/notes.md)
— 4 `AuthorizationReq` with the new script, 401 and no token at all with the old one.

That run also recorded a second thing to know when driving a session with no car: **2026.02.1 answers
`CableCheckRes` = `Ongoing` indefinitely**, where the 2023 demo image answered `FAILED` after 34 tries.
A harness that waits for `FAILED` to conclude "no hardware" waits forever.

The script also logs every V2G message their charger publishes — a station-side record of the session.
Trust the message **names**, not the bytes: the responses they publish carry the preceding *request's*
V2GTP length, so each one is truncated or padded with stale buffer, and 42 of 43 in the run that
measured it also carry `0x00` where the V2GTP version byte belongs. Requests are byte-exact. Measured
over a complete -2 DC charge on 2026.02.1 —
[`2026-08-10-everest-session-log-lengths`](../../docs/interop-runs/2026-08-10-everest-session-log-lengths/notes.md),
43 of 43 responses wrong. `Evse15118D20` publishes the message id and no bytes at all, so a `-20`
session has no station-side byte record to distrust.

**The topic scheme**, since their documentation does not carry it — and **it changed between the
versions this harness has used**, in both directions. Check which you are on before concluding that a
publish did nothing:

| | 2023.10.0 (`manager:main`) | **2026.02.1** |
|---|---|---|
| variable | `everest/<mod>/<impl>/var` | `everest/modules/<mod>/impl/<impl>/var/<name>` |
| command | `everest/<mod>/<impl>/cmd`<br>`{"name":"<cmd>","type":"call","data":{"args":{…},"id":"…","origin":"…"}}` | `everest/modules/<mod>/impl/<impl>/cmd/<cmd>`<br>`{"msg_type":"Cmd","data":{"args":{…},"id":"…","origin":"…"}}` |

The 2026.02.1 forms are read off their framework rather than guessed: topic at
`lib/everest/framework/lib/everest.cpp:877` with `config.cpp:445-448`, payload at `everest.cpp:408`
wrapped by `types.cpp:177-179`. The command name moved **into the topic** and the envelope key from
`type`/`name` to `msg_type` — so a publish in the old shape is not rejected, it is simply never
subscribed, which looks exactly like a working script with nothing to say.

Module ids are the **keys in the config file**, not the module types. `mosquitto_sub -v -t 'everest/#'`
against their broker is the fastest way to learn any wiring this README does not cover.

### Deciding a Plug & Charge contract  ([`contract-validator-arm.sh`](contract-validator-arm.sh))

**Their SIL does not decide whether a contract is good, and that is by design.** `EvseV2G` verifies the
chain against the MO root locally and hands the whole token — eMAID, chain in PEM, OCSP hash data — to
whoever is wired as `token_validator`. In every SIL config that is `DummyTokenValidator`, which returns
a value from its own config file and never reads the token. In a real deployment the decider is the
CSMS, through their OCPP module.

This arm is that backend, minus the CSMS: the manager is started with the validator **withheld** and
[`contract-validator.py`](contract-validator.py) answers on its topics over MQTT, using their own
`everestpy`. No patch, no new module, no manifest — `--standalone <module_id>` is the mechanism their
own `everest-testing` `ProbeModule` uses, and the module id is one the stock configs already declare.

```bash
bash contract-validator-arm.sh ~/everest/configs-ours/config-dc2-pnc-validator-ours.yaml policy.json
# then drive a session; every validate_token call is appended whole to the tokens JSONL
echo '{"status":"Invalid","certificate_status":"CertificateRevoked"}' > policy.json   # re-read per call
```

**One line of configuration decides whether this works at all, and its absence is invisible.**
`EvseManager` republishes the contract token through its own `token_provider` implementation, and only
`config-sil-ocpp-pnc.yaml` and `config-sil-ocpp201-pnc.yaml` connect that to `auth`. Everywhere else the
token is published to a variable nobody subscribed to: `PaymentDetailsRes` is `OK`, the signature
verifies, and then the session polls `AuthorizationReq` until `auth_timeout_pnc` and answers `FAILED` —
no token in any log, no error anywhere. **That is what every PnC run against their plain SIL has been
doing since 2026-08-03.** Add to the `auth` module's connections:

```yaml
      token_provider:
      - module_id: token_provider
        implementation_id: main
      - module_id: evse_manager          # without this there is no PnC token, ever
        implementation_id: token_provider
```

The arm refuses to start a config that lacks it. EIM tokens arrive without it, which is why the gap
survived so long — and because an EIM-only measurement is a real use of this arm, `EIM_ONLY=1` says you
meant it. **Every `-20` run is EIM-only**: `Evse15118D20` offers no PnC at all
(`ISO15118_chargerImpl.cpp:713`), so pair the arm with
[`mqtt-authorize.sh`](mqtt-authorize.sh) — that supplies the token, this supplies the verdict.

Measured on 2026-08-13 —
[`2026-08-13-everest-contract-validator`](../../docs/interop-runs/2026-08-13-everest-contract-validator/notes.md):
`Accepted` carried a `-2` PnC session past `Authorization` for the first time, and
`Invalid` + `certificate_status: CertificateRevoked` produced **`AuthorizationRes =
FAILED_CertificateRevoked`** — a response code no configuration of their SIL can reach, because
`DummyTokenValidator` cannot set `certificate_status` at all and `evse_managerImpl.cpp:386` then fills
in `value_or(Accepted)`.

The `-20` arm the same day found the sharper result: **a rejected verdict does not reach
`Evse15118D20` at all**, because `EvseManager` forwards them for PnC only
(`evse_managerImpl.cpp:381-387`) and that module has no PnC. Their station answers `Ongoing` until
`TIMEOUT_EIM_ONGOING` and then `FAILED`, where `[V2G20-2230]` allows 1,5 s to answer `Finished` with
`WARNING_EIMAuthorizationFailure`. The `-2` twin does the same thing and is **correct** doing it —
`[V2G2-854]` requires exactly that — so the rule changed between the protocols and the shared module
kept `-2`'s: [`…-d20-eim-rejection`](../../docs/interop-runs/2026-08-13-everest-d20-eim-rejection/notes.md).

**`V2G_INTEROP_ONGOING=<seconds>`** is what makes any of that visible. Our car stops polling an
`Ongoing` phase after 60 s, and all three station timers worth measuring are longer —
`auth_timeout_eim` 300 s, `TIMEOUT_EIM_ONGOING` 180 s, `auth_timeout_pnc` 55 s. Without it a run
measures our patience and reads exactly like a station that never answered; the first pass at this
finding did, and stopped 118 s short.

Two things the arm does **not** do. It does not test their chain validation: that already happened in
`iso_server.cpp:1049` before the token was built, and we measured it working on 2026-08-03. And it is a
test double, so what it proves is that their station *asks* correctly and *carries the verdict*
correctly — not that it decides correctly, because by design it does not decide.

Run it inside WSL. Through a `wsl.exe -- bash -lc '…' | …` wrapper the call does not return while the
background station is alive; the rig is up regardless, and the logs say so.

### Reporting a contactor state  ([`contactor-report.sh`](contactor-report.sh))

`ac_contactor_closed(bool)` is a command on their `ISO15118_charger` interface, called in a running
station by `EvseManager` from Control-Pilot events. A foreign EV produces no CP events, so `-20` AC
stops at `PowerDelivery(Start)` with `FAILED_ContactorError` after their 3 s timeout. This publishes the
same command their own `EvseManager` publishes.

```bash
bash contactor-report.sh --status false --watch charger.log   # fires when the window opens
bash contactor-report.sh --status true  --now
```

`--watch` polls for their *"Waiting for contactor is closed"* line, which is the 3 s window opening.
Deliberately polled and not `tail -F | grep -m1`: grep exits on the match, `tail` takes `SIGPIPE`, and
under `set -o pipefail` that reads as failure — the first version announced *"trigger never appeared"*
21 ms after the trigger appeared.

It found [a defect in their `-20` state machine](../../docs/reports/everest-iso20-ac-contactor-latch.md):
a contactor reported **open** is charged through.

### Arriving inside a window  ([`carsim-on-trigger.sh`](carsim-on-trigger.sh))

**This is the one that opened `-20` AC.** Same watcher as above, but instead of telling the HLC layer a
hardware fact, it moves the **simulated car** — so the station reaches its own conclusion through its own
IEC layer, its own `CPEvent::PowerOn` and its own `ac_contactor_closed(true)`. That difference is why its
sessions count as interop and `contactor-report.sh`'s do not.

```bash
CP_AT_PLUGIN=0 bash sil-car.sh &                     # plug in, hold at state B
bash carsim-on-trigger.sh --watch charger.log        # raise CP when the 3 s window opens
```

`PowerDelivery` waits for a `ClosedContactor` **event** inside that window and remembers nothing that
arrived earlier (`power_delivery.cpp:118`, gated on `is_ac_charger()` — which is why `-20` DC never meets
it, and why `-2` does not either: `EvseV2G` latches the value in a loop that re-tests it). Raising CP at
plug-in, as every AC run here did until 2026-08-13, puts their own `PowerOn` about **five seconds early**,
where it is produced and discarded. Firing it into the window gives `PowerOn` at +783…1005 ms against
3 000 ms: [`…-d20-ac-contactor-window`](../../docs/interop-runs/2026-08-13-everest-d20-ac-contactor-window/notes.md).

Two timing constraints, both of which cost a run: the session must start within ~45 s of `Set PWM On`
(after that the station declares `Car Paused`, and waking from it takes longer than the window), and the
watcher must be armed **before** the session.

### A PKI both sides agree on  ([`tls-pki-setup.sh`](tls-pki-setup.sh), [`tls-pki-restore.sh`](tls-pki-restore.sh))

**Every `-20` TLS run starts here, and it cannot be skipped or cached.** `Evse15118D20` switches to
`SSL_VERIFY_PEER | SSL_VERIFY_FAIL_IF_NO_PEER_CERT` the moment the client offers TLS 1.3, so the session
is mutual — and their pristine `everest-aux` PKI ships **no vehicle credential at all**.

```bash
bash tls-pki-setup.sh          # back up, regenerate with their own create_certs.sh, install, export
# … run …
bash tls-pki-restore.sh        # put the pristine tree back
```

Nor can the material be kept between runs: `create_certs.sh` regenerates the whole tree including the
station leaf, so the two sides agree only if it is installed wholesale — and the run then **restores**
the pristine tree, so that a later Plug & Charge run is not standing on material this harness minted.
Measured on 2026-08-13: the credentials left over from the 2026-08-06 TLS run chain to V2G root
`5E:77:33:20…` while the installed root was `88:F8:C2:D5…`, so they could not have worked. The restore
is a separate script rather than a sentence in a run note for exactly that reason.

Two traps it now handles for you, each of which cost an attempt:

- **The client chain needs *both* Vehicle Sub-CAs.** The path their station builds is
  `VEHICLE_LEAF ← VehicleSubCA2 ← VehicleSubCA1 ← V2GRootCA`; ship only SubCA2 and the handshake dies
  with `tls_process_client_certificate:certificate verify failed`, which names the client certificate
  rather than the missing link.
- **A refused handshake takes their whole V2G loop down** (`Shutdown loop() because of: Failed to
  SSL_accept()`), so the station needs restarting before the next attempt — not another SDP probe.

And one that is yours to remember: **their SDP is one-shot per session.** A probe not followed by a
connection leaves them answering *"Ignoring sdp request message because a session is already created and
running"* to every later probe. Probe and connect belong in one sequence.

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

### Their OEM provisioning chain → our SECC  ([`oem-certinstall-chain.sh`](oem-certinstall-chain.sh))

```bash
./oem-certinstall-chain.sh "$CERTS/ca/oem/OEM_ROOT_CA.pem" oemroot     # valid, anchored at OEMRootCA
./oem-certinstall-chain.sh "$BUNDLE/oem-subs-only.pem"     oemsubs     # refused: a Sub-CA is not an anchor
./oem-certinstall-chain.sh "$CERTS/ca/v2g/V2G_ROOT_CA.pem" v2groot     # refused: real root, wrong branch
```

`is_cert_install_needed: true` in `PyEvJosev`'s `config_module` turns their car into one that asks for a
contract certificate, and it then sends its OEM provisioning chain signed. Everything else is the reverse
setup above — their station on `lo`, their car on `eth0`, our SECC inside WSL with `--sdp`.

**Two things to do first, or it fails before the session starts.** Their `-20` cert-install path loads
`ca/oem/OEM_SUB_CA{1,2}.**der**` and this dist's store carried only the `.pem` — their own
`pki/create_certs.sh` writes both, so copy them from `pki/iso15118_20/certs/ca/oem/` or convert in place.
And their EVCC's *response* handler is `raise NotImplementedError` (it is Josev-derived): the session
ends the moment our answer arrives, which is expected, not a failure of the run. The verdict is printed
before the response is sent —
[`2026-08-08-everest-oem-provisioning-chain`](../../docs/interop-runs/2026-08-08-everest-oem-provisioning-chain/notes.md).

### Scenario order

1. ✅ **-2 DC, EIM, `tls_security: allow`** — forward. Done 2026-08-02: a complete charge, 36/36 `OK`.
2. **-20 DC** against `Evse15118D20` (`config-sil-dc-d20.yaml`), forward. **Next** — the same two
   hardware steps should apply, and no complete -20 session against a foreign station exists yet.
3. **-2 AC** (`config-sil.yaml`), forward. A different CP/PWM story: the plug-in sequence wants
   `iec_wait_pwr_ready` rather than the 5 % HLC mode.
4. **TLS** (`config-sil-dc-tls.yaml`), with `tls_key_logging: true`, now that there is a full plaintext
   session to compare against.
5. ✅ **IsoMux**, the closest thing to a real charger's behaviour: one endpoint answering both. Done
   2026-08-03 — it binds its own TCP port at startup (61342, no SDP step needed) and routes on the
   offered namespace. Its backends sit on `lo` with `enable_sdp_server: false`, and `Evse15118D20`
   behind it still claims `[::1]:50000`.
   <br>**Correction, 2026-08-09:** it does **not** terminate the SupportedAppProtocol handshake, as this
   list said until today. `v2g_sniff_apphandshake()` decodes the request only to decide a route; the
   buffered request is then written through to the backend, which answers
   (`IsoMux/connection/connection.cpp:462`, `.../tls_connection.cpp:334`, both under their own comment
   *"still in buffer, we need to forward it"*). The distinction decides who is answerable for the
   SchemaID, which is what the **twentieth filing** turns on:
   [`everest-isomux.md`](../../docs/reports/everest-isomux.md) — the router
   picks the backend on the first `-20` entry it sees and never reads `Priority`, while both backends
   read it correctly.
6. ✅ **MCS** — the first live counterpart our MCS support ever had. Done 2026-08-05 against
   `config-sil-mcs.yaml`: three complete sessions, service id 8 read back as MCS by their stack.
   ✅ **MCS_BPT (9)** followed on 2026-08-06 (`V2G_INTEROP_MCS_FIRST=9`): two complete sessions with the
   discharge half declared, and 3.75 MW decoded by their `EvseManager`. The first attempt a day earlier
   was refused with `FAILED_WrongChargeParameter` and is what drove the two app fixes behind it —
   [`2026-08-05-everest-mcs-bpt`](../../docs/interop-runs/2026-08-05-everest-mcs-bpt/notes.md) for the
   refusal, [`2026-08-06-everest-mcs-bpt-complete`](../../docs/interop-runs/2026-08-06-everest-mcs-bpt-complete/notes.md)
   for the result.
7. ✅ **Reverse** with `PyEvJosev` — generally lower value (it is Josev in a wrapper), **except for MCS**,
   which is why it was done: their `config-sil-mcs.yaml` gives their car
   `supported_d20_energy_services: MCS`, and that is the only way to put *our* catalogue in front of a
   foreign chooser. Done 2026-08-06: offered `{ 8, 9 }`, their EV took **8** and ran to `SessionStop` —
   and authorized with **Plug & Charge**, which our SECC verified.
   [`2026-08-06-everest-mcs-reverse`](../../docs/interop-runs/2026-08-06-everest-mcs-reverse/notes.md).

   The relay cannot cover this direction: `PyEvJosev` finds a station only by SDP multicast on its own
   interface, so our SECC has to run **inside WSL** on the same link — `secc --listen 55000 --protocol 20
   --mode mcs --sdp --interface eth0`, exactly what [`reverse-iso2-dc.sh`](reverse-iso2-dc.sh) assumes.
   Point their `Evse15118D20` at `lo` first, or it answers the SDP request before ours does.

   ✅ **…and through the recording fixture**, since `V2G_INTEROP_SDP=<iface>` — see
   [running a reverse run recorded](#running-a-reverse-run-recorded) below.
   [`…-mcs-reverse-recorded`](../../docs/interop-runs/2026-08-06-everest-mcs-reverse-recorded/notes.md).

8. ✅ **`IsoMux` over TLS** (`config-sil-dc-isomux-tls.yaml`), done 2026-08-06 — the last item that was on
   this list. It binds its TCP port at startup, so **no SDP probe**, and it serves **TLS 1.2 only**: a -20
   hello gets alert 70, while a both-offer negotiates 1.2 and is then routed to the **-20** backend, giving
   a complete -20 session on a profile ISO 15118-20 does not allow. `IsoMux` also survives a refused
   handshake, unlike `Evse15118D20`
   ([`…-isomux-tls`](../../docs/interop-runs/2026-08-06-everest-isomux-tls/notes.md)). The profile half is
   the **nineteenth filing** since 2026-08-09, `[V2G20-2356]` having settled that a station must not
   select -20 there: [`everest-isomux.md`](../../docs/reports/everest-isomux.md).

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

### Running a reverse run *recorded*

A reverse run used to have to go through the CLI, which can advertise over SDP but writes no artifacts,
because the fixture could only bind a socket and their EV cannot be pointed at one. `V2G_INTEROP_SDP=<iface>`
closes that: the fixture runs a `SECC_SDPServer` beside its listener, on the port it actually bound, so a
discovered session is a recorded one. Three things to get right:

1. **Run `dotnet test` inside WSL.** SDP is multicast on the EV's link and Windows is not on it. .NET 10 is
   there; pass `--artifacts-path ~/wsl-artifacts` so the Linux build does not fight the Windows `bin/`+`obj/`
   in the same working tree.
   <br>**That flag costs three red tests, and they are not real.** `SchemaSetIntegrationTests` resolves
   `Schemas/` relative to the default `bin/` layout, so `--artifacts-path` moves the output out from under
   it and all three `FullIso2SchemaSet_*` cases fail with `DirectoryNotFoundException`. Use a filter for
   interop work, and **verify the offline gate on Windows** — `dotnet test -c Release` there is the run
   that means 1 404 green. Measured 2026-08-13: 3 failed under WSL with the flag, 0 without it on Windows,
   same commit.
   <br>**And it is a separate output tree from the Windows `bin/`, which `--no-build` will happily run.**
   A fixture change built on Windows is not in `~/wsl-artifacts`; the first reverse TLS attempt on
   2026-08-14 advertised `NoTLS` with `V2G_INTEROP_TLS_SERVER` set for exactly that reason, and everything
   else about the run looked right. **Rebuild in WSL after any fixture change**, or drop `--no-build`.
2. **Fixture first, station second.** Their EV probes once, shortly after the manager boots. If nothing
   answers that probe the session never starts, and the timeout looks exactly like a peer that never came.
3. **Their `Evse15118D20` on `lo`**, as for the CLI, or it answers the probe before ours does.
4. **Over TLS: `V2G_INTEROP_TLS_SERVER=<pfx>[:password]`**, and `V2G_INTEROP_TLS_REQUIRE_CLIENT=1` for the
   mutual handshake `-20` wants. The certificate has to be **theirs** — their EV anchors at
   `CertPath.V2G_ROOT_PEM` in its own PKI path with `CERT_REQUIRED` — so run
   [`tls-pki-setup.sh`](tls-pki-setup.sh) and bundle `SECC_LEAF` with both CPO Sub-CAs into a PKCS#12;
   that script exports the *EV* half (`trust.pem`, `vehicle.p12`) and not this one. The SDP security byte
   follows the listener automatically; a station advertising TLS while serving plaintext is discovered and
   then fails, which reads as a defect of theirs
   ([`…-d20-ac-reverse-tls`](../../docs/interop-runs/2026-08-14-everest-d20-ac-reverse-tls/notes.md)).

```bash
/usr/sbin/mosquitto -p 1883 &                                    # not on PATH
cd /mnt/d/…/ISO15118ConformanceTests
V2G_INTEROP_LISTEN=55000 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=mcs \
V2G_INTEROP_RECORD=$HOME/everest/run/mcs-reverse \
  dotnet test ISO15118ConformanceTests.Simulation/ISO15118ConformanceTests.Simulation.csproj -c Release \
    --artifacts-path ~/wsl-artifacts -l "console;verbosity=detailed" \
    --filter FullyQualifiedName~TheirPyEvJosev_AgainstOurSecc &
sleep 8 && ~/everest/dist/bin/manager --config ~/everest/configs-ours/config-mcs-reverse-ours.yaml
```

The SDP server logs each request and each answer as it happens, which is what to look at when a reverse run
times out — it separates "their EV never probed" from "it probed and we dropped it".

**A PnC session cannot become a corpus trace, and should not.** Their EV signs the `AuthorizationReq` with a
key that is theirs, so `SessionTrace.Build` refuses the recording rather than substitute the recorded
signature and verify nothing. Add `V2G_INTEROP_NO_PNC=1` for a second, EIM run if the run is meant to
produce a corpus entry — keep both, they are different evidence.

## Known friction (expect these first)

- **`PyEvJosev` supports nothing by default.** Every `supported_*` key defaults to `false`. A car that
  announces no protocol gets no session, and the symptom is an empty SupportedAppProtocol negotiation
  rather than an obvious error.
- **`device` on both sides.** Their modules bind an interface by name; ours takes it as `--interface`.
  All three must agree, and it must have an IPv6 link-local address.
- **Link-local addressing with zones**, as everywhere: write `[fe80::…%iface]:port`.
- **Check their `EvseV2G`'s EXI lineage per image, not per project.** At HEAD it is cbV2G, the encoder
  our corpus comes from, so a byte disagreement would be with ourselves and the thing to read is order
  and timing. In the `:main` demo image it is OpenV2G, and a byte disagreement is a real one. `ldd` the
  module before deciding which of those two you are looking at.
- **Their EV is Josev.** If a reverse run reproduces something already recorded under
  `docs/interop-runs/2026-07-2*`, that is not a new finding; check there first.
- **A Plug & Charge token reaches `auth` in two configs only.** `evse_manager`'s `token_provider`
  implementation is connected to `auth` in `config-sil-ocpp-pnc.yaml` and `config-sil-ocpp201-pnc.yaml`
  and nowhere else, so everywhere else the contract token is published and dropped — and the session
  fails on the auth timeout with nothing in any log to say why. EIM is unaffected, which is what makes
  it hard to see. See [the contract-validator arm](#deciding-a-plug--charge-contract--contract-validator-armsh).

Confirmed on first contact, and no longer questions:

- **`EvseV2G` in the `:main` demo image segfaults on the second V2G session in one process** (status 139,
  while handling `PaymentServiceSelectionReq`), and EVerest's manager then terminates every module — one
  crash takes the whole charger down. The first session is always fine, however short. **Not present in
  2025.10**, where two consecutive sessions both complete; it is a property of everest-core 2023.10.0.
  Restart the manager between runs on that image. See
  [`2026-08-02-everest-iso2-dc-mqtt-auth`](../../docs/interop-runs/2026-08-02-everest-iso2-dc-mqtt-auth/notes.md).
- **`Evse15118D20` has no TCP port until an SDP request arrives.** Use [`sdp-probe.sh`](sdp-probe.sh),
  which multicasts.
- **Any error on their accept path ends the whole event loop, not the connection** — one defect with at
  least three triggers: a unicast SDP request, key logging enabled, a refused TLS handshake. The sockets
  stay bound afterwards, so the station keeps accepting connections and answers nothing, which looks
  like a hung peer rather than a crash. **Restart the manager after every failed attempt**, or you will
  debug the corpse instead of the run.
- **`Evse15118D20` refuses to start without a V2G certificate**, even with `ENFORCE_NO_TLS`. The image
  ships CA roots and an empty `client/cso`; their own test PKI is at
  `tests/ocpp_tests/test_sets/everest-aux/certs/` and its `SECC_LEAF` password matches the SIL configs,
  so copying it into `etc/everest/certs/` is enough — no key generation.
- **A TLS 1.3 run needs a Vehicle certificate**, which no image ships: `Evse15118D20` switches to
  `SSL_VERIFY_PEER | SSL_VERIFY_FAIL_IF_NO_PEER_CERT` as soon as the client offers 1.3, so mutual TLS is
  mandatory there. Their `create_certs.sh` (Josev, `iso15118/shared/pki/`) generates the whole chain,
  station and vehicle, and that is their own documented workflow.
- **`Evse15118D20` sends only its leaf** — no CPO Sub-CAs on the wire, so an EV must already hold them;
  pass root + both Sub-CAs as one PEM bundle in `V2G_INTEROP_TLS_TRUST`.
  <br>**This is per module, and the general phrasing it used to have was hiding a defect of ours.**
  `EvseV2G` sends the **whole** path — leaf, `CPOSubCA2`, `CPOSubCA1` — so against the `-2` station the
  V2G root alone is a sufficient anchor, measured 2026-08-14. That could not be said until the same day,
  because `InteropEnvironment.DevTlsOrNull` discarded the validation callback's `X509Chain` and judged
  every peer on its bare leaf; a bundle carrying the Sub-CAs passes either way, so only a root-only
  anchor could tell. Fixed, with the regression in `ChainValidationTests`
  ([`…-iso2-ac-tls12`](../../docs/interop-runs/2026-08-14-everest-iso2-ac-tls12/notes.md)).
- **`enable_tls_key_logging: true` kills their -20 server here** — it binds a UDP socket to an interface
  and the call fails under qemu (`Could not set interface name:eth0`). Probably emulation rather than a
  defect; leave it off unless you are on x86-64.
- **`supported_scheduled_mode` defaults to false** on `Evse15118D20` while `supported_dynamic_mode`
  defaults to true. Our EVCC negotiates Scheduled unless told otherwise.
- **`CableCheck` needs their hardware simulation.** `EvseManager` waits ~5 s for the board-support module
  to report the contactor closed and answers `FAILED` when it does not. In the SIL that contactor closes
  because the simulated car walks the CP line A→B→C; a V2G peer over TCP has no CP line, so a forward
  run stops there unless [`sil-car.sh`](sil-car.sh) drives it. Publishing `cp C` is **not** the way —
  the state machine rewrites `cp_voltage` from its own state on every tick.
- **`-20` AC has two clocks, and both cost a run before they were understood.** The contactor window is
  **3 s** wide and only an event arriving inside it counts, so the car must raise CP *after*
  `PowerDelivery(Start)` — that is [`carsim-on-trigger.sh`](carsim-on-trigger.sh). And with the car
  waiting at state B the station gives up after about **45 s** (`PrepareCharging → T_step_EF →
  Car Paused`), from which waking takes longer than the window; so the session has to start inside that
  runway, which in practice means `dotnet test --no-build` launched right after `Set PWM On`. A run that
  misses the second clock looks exactly like a run that missed the first: `FAILED_ContactorError` at
  3,0 s. Read the log for `Car Paused` before concluding anything.
- **Killing the manager orphans its modules.** They are separate processes and stay bound to port 61341.
  `pkill -f 'bin/manager'` leaves a half-dead charger answering connections; kill the process group or
  recreate the container.
- **And a native build's modules do not carry the binary path at all**, so a pattern built from it kills
  nothing and reports success. The manager execs each module with the prefix as a **flag** —
  `evse_security:EvseSecurity --prefix <p> --module …` — so `pkill -f "dist-main/bin/manager"` matches
  neither the manager (`./bin/manager --prefix …`) nor any child. **Kill on `--prefix <p>`.**
  <br>On 2026-08-12 that left a station running under a second one: two managers on one MQTT prefix,
  `json.exception.type_error.302` and `std::future_error: Promise already satisfied` seconds after
  *"Starting 18 modules"*, then a manager-wide crash shutdown with modules exiting on signal 11. It
  reads exactly like a defect in whatever you changed last
  ([run notes](../../docs/interop-runs/2026-08-12-everest-main-chain-selection/notes.md)).
- **Verify the kill by reading `pgrep -af`, not by trusting a count.** Two different things go wrong
  with a count, and both did:
  <br>`pgrep -f "prefix /home/…"` matches the shell that is running the `pgrep`, so it answers
  *"still running"* forever. Bracketing one letter — `pgrep -cf [d]ist-main` — fixes that **in a native
  shell**.
  <br>But through a `wsl.exe -- bash -lc '…'` wrapper the nested quotes do not survive:
  `pgrep -cf "[d]ist-main"` returned **0** while twenty-one matching processes were running, and the
  same pattern **unquoted** returned 21. A verification that can silently return zero is worse than
  none, because it confirms whatever you already believe.
  <br>So: run `pgrep -af <pattern>` and *look at the lines*. The self-match is obvious when you can see
  it — it is the one whose command line is your own `pgrep`.
- **On colima, publish a port only once its backend is listening.** A relay container that installs
  `socat` at startup leaves the published port empty for ten seconds, and the lima forward is poisoned
  for good afterwards: connections are accepted and silently dropped, `nc -z` says the port is open, and
  the container never sees an accept. Bake `socat` into the image.
