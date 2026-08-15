# 2026-08-15 — the renegotiation re-run: our wall is gone, and theirs is a different one

[The filing](../../reports/everest-evsev2g-renegotiation-cablecheck.md) was withdrawn hours earlier when
its own document gate turned out to refute it: a DC renegotiation returns through `CableCheck` and
`PreCharge`, `EvseV2G` implements that, and **our** car was the one skipping them. This is the arm that
proves the fix on the wire — against the same binary, the same config and the same car the 2026-08-11
session used.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `EvseV2G`, `config-dc2-ours.yaml`, DC over plain TCP |
| Ours | `Evcc2` with the isolation sequence restored — merged as `e60c73a` |
| Outcome | **the `FAILED_SequenceError` is gone**; the session now dies four messages later, inside their `EvseManager` |

## Two arms, one variable, three minutes apart

| arm | after the renegotiated `ChargeParameterDiscoveryRes (OK)` | their station | exchanges |
|---|---|---|---|
| **control** — the pre-fix car | `PowerDeliveryReq(Start)` | **`FAILED_SequenceError`** | 41 |
| **fixed** | `CableCheckReq` | **accepted** — 4 × `CableCheckRes (OK)`, then `FAILED` | 46 |

The control is the point of this run. `V2G_INTEROP_RENEG_SKIP_ISOLATION=1` reproduces the car we shipped
until this morning, and their station answers exactly what it answered on 2026-08-11 — their own log
saying so:

```
iso15118_charge :: Failed response code detected for message "Power Delivery", error: Sequence Error
```

**A control taken four days apart is a weaker control than one taken three minutes apart**, and this
project had been relying on the weak one to call a station defective.

## What the fixed arm reached, and where it stopped

`PowerDelivery(Renegotiate)` `OK` → `ChargeParameterDiscovery` `OK` → **`CableCheck` accepted** — the
message that was unreachable this morning — and then their own cable check fails:

```
evse_manager :: EVSE ISO Start cable check...
evse_manager :: Cancel cable check wait below voltage
evse_manager :: Voltage did not drop below 60V within timeout, sending CableCheck Finished(false) anyway
evse_manager :: Error raised, type: evse_manager/MREC11CableCheckFault, sub_type: Self test failed
evse_manager :: Error raised, type: evse_manager/Inoperative
evse_manager :: Initiating error shutdown
```

Their `EvseManager` begins a cable check by waiting for the DC link to fall below 60 V. During a
renegotiation it does not: the contactor is closed and the supply is still at the charge-loop setpoint.
So the isolation self-test times out, raises `MREC11CableCheckFault`, and the station goes `Inoperative`.

**This is the physical objection the withdrawn report made — and it was aimed at the wrong thing.** The
report used it to argue that the standard cannot mean what its DC state table says. What it actually
describes is the gap between the sequence the standard requires and the state the link is in when that
sequence runs; the 2019 *ISO 15118 Manual* names exactly that gap for the 2014 edition and says the
second edition was expected to address it.

## Not filed, and this time the reason is written down before the report is

**Two candidate owners, and the likelier one is us.** Our `CableCheckReq` carries `EVReady: true`
(`Evcc2.EvStatus()`) — a car announcing it is ready to charge while asking for an isolation test — and
our EV neither stops its demand nor models a contactor it could open. A station cannot bring the link
below 60 V while the car it is talking to says it is ready and the loop's setpoint stands. Their side may
also be at fault: `wait_powersupply_DC_below_voltage` logs *"Cancel cable check wait below voltage"*
twice before the timeout, which is not obviously the behaviour of code that asked its supply to ramp
down.

**The arm that settles it** is cheap and is not this run: send `EVReady = false` (and a zero demand) in
the renegotiated `CableCheckReq`, and watch whether their supply ramps down. If it does, the second wall
was ours as well and the fix is another half of this morning's; if it does not, there is a filing to
write about `EvseManager`'s cable-check path — and it will be a *different* filing from the withdrawn
one, aimed at the module that actually decides.

Writing that here rather than opening a report is the whole lesson of the day, applied the same evening.

## Rig notes, because two of these cost time

- **The socat relay was the first failure**, not their station: `TCP-LISTEN:15118 → TCP6:[fe80::…]:61341`
  accepted our connection and then EOF'd at the SAP exchange. `dotnet test` runs **inside** WSL here, so
  the relay is unnecessary — connecting straight to `[fe80::…%eth0]:61341` worked on the first attempt.
  The relay exists for runs driven from Windows and had been copied into a recipe that does not need it.
- **The session before the fixed arm left the station `Inoperative`**, so the control arm got a fresh
  `manager`. A station that has raised `MREC11CableCheckFault` does not serve the next session; restart
  before every arm rather than between arms that happen to fail.
- Their `-2` TCP server keeps one port across sessions (`61341` here) — unlike `Evse15118D20`, no
  SDP-per-session dance was needed, though the probe is still the honest way to read the endpoint.

## Artifacts

[`frames.fixed.log`](frames.fixed.log) · [`frames.control.log`](frames.control.log) — our side of both
arms. [`their-station.fixed.log`](their-station.fixed.log) ·
[`their-station.control.log`](their-station.control.log) — their own lines, colour codes stripped.

Offline gate: **1 413 green**, four assemblies, exit code 0.

## Reproduce

```bash
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-dc2-ours.yaml
CP_AT_PLUGIN=1 bash ~/everest/sil-car.sh        # wait for "Set PWM On"
bash ~/everest/sdp-probe.sh eth0                # reads the endpoint; no relay needed inside WSL

V2G_INTEROP_SECC='[fe80::…%eth0]:61341' V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RENEGOTIATE=1 \
  dotnet test -c Release --filter "FullyQualifiedName~EverestInteropTests.OurEvcc_AgainstTheirEvseV2G"
```

Add `V2G_INTEROP_RENEG_SKIP_ISOLATION=1` for the control, and restart the manager in between.

## Next

- **The `EVReady = false` arm**, above. It decides whether the second wall is ours or theirs, and until
  it is run this project has no business filing anything about their cable check.
