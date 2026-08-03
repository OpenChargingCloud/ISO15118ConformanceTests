# 2026-08-02 — EVerest `EvseV2G`, ISO 15118-2 DC: a complete charge

**The first complete ISO 15118-2 charging session this project has run against a foreign station.**
Thirty-six exchanges from `SupportedAppProtocolReq` to `SessionStopRes`, every response `OK`, through
CableCheck, PreCharge, the CurrentDemand loop, WeldingDetection and a clean SessionStop — and the flow
report's verdict on the route: **"The order matches the declared flow exactly."**

The previous two runs against this station stopped at `Authorization` and at `CableCheck`. What was
missing both times was not protocol but hardware, and it turns out their hardware simulation is
addressable over MQTT like everything else in EVerest.

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) via `ghcr.io/everest/everest-demo/manager:main`, `EvseV2G` |
| Ours | `Vanaheimr.V2G.Exi` @ `f4455d7` |
| Direction | our EVCC → their charger |
| Session | ISO 15118-2 DC, plain TCP, the same `config-ours.yaml` as the two runs before it — **still unchanged** |
| Driven by | [`tools/interop-everest/sil-car.sh`](../../../tools/interop-everest/sil-car.sh) — two MQTT publishes |
| Outcome | **complete charge, 36/36 `OK`, route identical to our own recorded session** |
| Artifacts | [`flow.md`](flow.md), [`frames.log`](frames.log), [`session.trace.json`](session.trace.json), [`their-charger.log`](their-charger.log), [`sil-car.log`](sil-car.log) |

## The session

```
0    SupportedAppProtocolReq      → SupportedAppProtocolRes      OK_SuccessfulNegotiation
1    SessionSetupReq              → SessionSetupRes              OK_NewSessionEstablished
2    ServiceDiscoveryReq          → ServiceDiscoveryRes          OK
3    PaymentServiceSelectionReq   → PaymentServiceSelectionRes   OK
4–5  AuthorizationReq × 2         → AuthorizationRes             OK  (Ongoing, then Finished)
6    ChargeParameterDiscoveryReq  → ChargeParameterDiscoveryRes  OK
7–27 CableCheckReq × 21           → CableCheckRes                OK  (Ongoing, then Finished)
28   PreChargeReq                 → PreChargeRes                 OK
29   PowerDeliveryReq             → PowerDeliveryRes             OK
30–32 CurrentDemandReq × 3        → CurrentDemandRes             OK
33   PowerDeliveryReq             → PowerDeliveryRes             OK
34   WeldingDetectionReq          → WeldingDetectionRes          OK
35   SessionStopReq               → SessionStopRes               OK
```

Two authorization polls instead of the seven of the previous run, because this time the authorization
comes from the plug-in event, the way it does on a real charger.

And it was a charge, not a walkthrough. From their side:

```
EVSE IEC DC power supply set: 400V/2A, requested was 400V/2A.     ← PreCharge
EVSE IEC DC power supply set: 400V/120A, requested was 400V/120A. ← PowerDelivery
EVSE IEC Charger state: PrepareCharging->Charging
EVSE IEC Isolation measurement Ok R_F 900000.
EVSE IEC DC power supply OFF
```

Their `CurrentDemandRes` carries their EVSE ID `DE*PNX*E12345*1` and meter id `DC_POWERMETER`; on the
run before this one, re-plugging showed the next transaction starting at `0.00053 kWh` — the simulated
energy from the previous session had actually been metered.

## How the hardware simulation is driven

`EvseManager::cable_check()` closes the contactor and waits ~5 s for the board-support module to report
it closed. In the SIL that report comes because the simulated car walks the CP line A→B→C. A V2G peer
over TCP has no CP line, so the previous run got `CableCheckRes = FAILED`, correctly.

[`sil-car.sh`](../../../tools/interop-everest/sil-car.sh) publishes two strings on their own external
MQTT interface — `everest_external/nodered/1/carsim/cmd/…`, which takes a bare command string, no
envelope:

| When | Topic | Payload |
|---|---|---|
| at start | `…/execute_charging_session` | `sleep 2;iso_wait_slac_matched;iso_wait_pwm_is_running;sleep 600` |
| on `Start_CableCheck` | `…/modify_charging_session` | `draw_power_fixed 0,0;sleep 600` |

The trigger for the second one is the same variable the authorization script watches: `EvseV2G`
publishes `Start_CableCheck` on `everest/iso15118_charger/charger/var` the moment it enters the phase.

**Three things about their `JsCarSimulator` make those exact strings non-obvious, and each cost a run
to find:**

1. **When a command list runs out, the module resets to defaults — and the default state is
   `unplugged`.** The first attempt ended `…;iso_wait_pwm_is_running`, and one tick after the last
   command completed their log read `SLAC UNMATCHED`, `CAR IEC Event BCDtoEF`, `CarUnplugged`. The car
   pulled out from under the session it had just enabled. Every sequence has to end in a long `sleep`.
2. **`execute_charging_session` is refused while a list is still running** — *"Execution of charging
   session simulation already running, cannot start new one"* — and it resets the simulation when it is
   accepted. `modify_charging_session` replaces the list **without** resetting, which is what a
   mid-session step needs. To start over: send `unplug`, wait for the list to drain, then execute.
3. **`cp C` does nothing.** The command sets `simdata_setting.cp_voltage`, and the state machine
   rewrites that field from `mod.state` on every 250 ms tick — so a plugged-in car goes straight back
   to 9 V. The state that sets 6 V *unconditionally* is `charging_fixed`, reached by
   `draw_power_fixed 0,0`; their own comment calls it "a break the rules mode to test the charging
   implementation", which is exactly what this is. The polite route, `iso_wait_pwr_ready`, waits on
   their `PyEvJosev` — the module we deliberately pointed at `lo` so that the V2G session would be ours.

**The MQTT token is no longer needed on this path.** With the car plugged in, `EvseManager` opens a
transaction and their own `DummyTokenProvider` authorizes it, because the plug event that
[`mqtt-authorize.sh`](../../../tools/interop-everest/mqtt-authorize.sh) was standing in for now really
happens. Both scripts stay: one authorizes a session with no hardware at all, the other supplies the
hardware. They answer different questions, and the first is still the shorter path to a station that
only needs to talk.

## What this proves, and what it does not

**Correction, found while preparing the -20 run: this *was* an EXI result.** Every plan for this
counterparty, this file included, said `EvseV2G` sits on cbV2G — the encoder our vector corpus is
generated from — so that byte agreement with it would be agreement with ourselves. That is true of
`everest-core` today. It is **not** true of the image all three runs used:

```
$ ldd /workspace/dist/libexec/everest/modules/EvseV2G/EvseV2G
        libopenv2g.so.1 => /workspace/dist/lib/libopenv2g.so.1
$ find /workspace -iname '*cbv2g*' -o -iname '*cbexigen*'
        (nothing)
```

`ghcr.io/everest/everest-demo/manager:main` is **everest-core 2023.10.0, built 2023-12-05** — its
`release.json` lists `OpenV2G 2023.3.0` and no libcbv2g, and the module links `libopenv2g.so.1` with
245 ISO/DIN/appHand symbols in it. EVerest moved `EvseV2G` onto libcbv2g later; the `:main` tag was
simply never rebuilt. It surfaced while looking for `Evse15118D20` for the -20 run — see
[below](#the--20-run-this-was-meant-to-be).

The tag moves, so the runs are pinned by digest instead:

```
ghcr.io/everest/everest-demo/manager@sha256:89799fb3302309c5337ab40c85af7e573d65ff2decda6315c2c1eb644c722681
```

That a moving tag silently decided what our results meant is the lesson worth more than the finding:
**an image tag is not a version.** Every future run write-up records the digest.

So the codec on the other end was **OpenV2G** — a hand-written C codec from the original ISO 15118
reference work, a different codebase from chargebyte's cbexigen generator by different authors. Which
means all 36 of our messages were decoded and acted on correctly by an EXI implementation that shares
no lineage with our corpus, and all 36 of theirs were decoded by ours. Not a byte diff — we never
encoded the same content with both and compared octets — but a working independent decoder in **both
directions**, which is the same kind of evidence Josev's EXIficient gives and which nothing in the plan
expected from this counterparty.

The lineage column in the counterparty table is therefore right about `everest-core` HEAD and wrong
about what we ran, in the direction that makes the runs worth more rather than less. Corrected there
and in the harness README.

**What it also proves is the whole shape of a DC charge**, which is precisely the class a corpus of
single messages cannot hold: that our EVCC's phase order, its poll-until-`Finished` loops, its
transitions out of CableCheck into PreCharge, its `PowerDelivery`/`CurrentDemand`/`PowerDelivery`
bracket and its shutdown are the ones a real charger implementation expects — and that a station
written by other people, driving a simulated power supply and an isolation monitor, follows our car
through all of it without a single non-OK code. The flow comparison against our own recorded Josev
session says the two routes are identical, message for message. Two independent state machines now
agree on the route, where before there was one.

`session.trace.json` **is** checked in here, unlike the truncated recordings of the two runs before it:
36 exchanges, strictly alternating, a complete session from a foreign station. Promoting it into
`Vectors/` is still a separate decision — a foreign station's bytes in the corpus changes what the
corpus means — but for the first time there is a candidate worth the discussion.

## The -20 run this was meant to be

The next step after a complete -2 charge was the same against **`Evse15118D20`**, and it did not happen.
The reason is the finding above, from the other end:

```
$ docker exec everest sh -c "ls /workspace/dist/libexec/everest/modules/ | grep -iE '15118|d20|mux'"
(nothing)
$ docker exec everest sh -c "ls /workspace/dist/etc/everest/ | grep d20"
(nothing)
```

**everest-core 2023.10.0 has no `Evse15118D20`, no `IsoMux`, no `config-sil-dc-d20.yaml` and no
`config-sil-mcs.yaml`.** All four are things this harness's README lists as targets, taken from
`everest-core` HEAD — and none of them are in the image the harness has been running. The -20 charger
did not exist yet when that image was built.

Newer tags do exist (`2025.10.0-patches`, `2025.6.x-dt-esdp`, `2025.3.x-dt-esp`, …), so `:main` being
three years stale is a property of that tag rather than of the project. Pulling `2025.10.0-patches`
(4.28 GB compressed, amd64) failed twice on **disk**: it does not fit in colima's default 20 GB VM, even
after freeing the old image — the extraction ran out of space partway through
`libexec/everest/modules/OCPP201`, and the layer contents show why (it carries a build tree, `ccache`
included). Growing the VM disk is the next move and it is a decision about somebody's laptop, not a
technical unknown.

So the -20 run is **not blocked by anything in the protocol stack**, ours or theirs. It needs a bigger
disk and one more pull, and then the same recipe: `sil-car.sh` for the hardware, `V2G_INTEROP_PROTOCOL=20`,
and `Evse15118D20`'s `tls_negotiation_strategy` set so a first run can be plaintext.

## The second-session crash, now beyond doubt

The previous run recorded that `EvseV2G` segfaults on the second V2G session in a process. The obvious
objection was that the first session had ended badly — the EV hung up after `FAILED`. It had not been
the cause:

```
CAR ISO V2G PaymentServiceSelectionReq
[CRIT] Module iso15118_charger (pid: 1660) exited with status: 139. Terminating all modules.
```

That is the **fifth** reproduction, and this time the first session was a complete, successful charge
ending in `SessionStopRes`, followed by a proper `unplug`, a fresh plug-in, SLAC re-matched and a new
transaction opened. The second V2G session still dies at the same line. So the crash is about the
second session, full stop — not about how the first one ended.

For a charger in the field that would be one car per process — and it never was one, for anybody
running a current EVerest. **Not in the 2025.10 release:** the same two-session procedure against that
image's `EvseV2G` produced two complete charges and no crash, so the defect belongs to everest-core
2023.10.0 and nothing needs reporting.
See [`../2026-08-03-everest-iso20-dc-full-charge/notes.md`](../2026-08-03-everest-iso20-dc-full-charge/notes.md).

The discipline still stands, though, and so does the reason for it: a station that answers the first
car and dies on the second is invisible to a one-session harness, whichever build it lives in.

## How to reproduce

Setup exactly as in [`../2026-08-02-everest-iso2-dc-mqtt-auth/notes.md`](../2026-08-02-everest-iso2-dc-mqtt-auth/notes.md)
— same image, same config, same `socat` relay — with `sil-car.sh` in place of `mqtt-authorize.sh`:

```bash
docker cp ../../tools/interop-everest/sil-car.sh mqtt:/tmp/
docker exec -d mqtt sh -c "/tmp/sil-car.sh > /tmp/sil-car.log 2>&1"

# wait for the plug-in to take before connecting
until docker exec everest sh -c "grep -q 'Set PWM On' /tmp/everest.log"; do sleep 2; done

V2G_INTEROP_SECC=127.0.0.1:15130 V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RECORD=/tmp/everest-full \
V2G_INTEROP_SCENARIO=$PWD/Vanaheimr.V2G.Simulation.Tests/Vectors/Session.iso2-dc-eim.trace.json \
  dotnet test Vanaheimr.V2G.Simulation.Tests -c Release \
    --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

Restart the manager before each run — the second session crashes it. The artifacts in this directory
are from a verification run driven entirely by the checked-in script, not by hand.

## Next

- **-20 against `Evse15118D20`** (`config-sil-dc-d20.yaml`). The same two hardware steps should apply;
  the message set is the one this stack has the most to say about, and no complete -20 session against
  a foreign station exists yet.
- **AC** (`config-sil.yaml`) — a different CP/PWM story, so the plug-in sequence will need
  `iec_wait_pwr_ready` rather than the 5 % HLC mode.
- **TLS** (`config-sil-dc-tls.yaml`) with `tls_key_logging: true`, now that there is a full plaintext
  session to compare against.
- **`IsoMux`**, then **`config-sil-mcs.yaml`** — the first live counterpart our MCS support has ever
  had, and now within reach rather than hypothetical.
- **The reverse direction** (`PyEvJosev` → our SECC), which needs SDP on a shared link and is the one
  the relay cannot cover.
