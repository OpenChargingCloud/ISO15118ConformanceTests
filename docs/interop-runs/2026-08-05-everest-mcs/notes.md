# 2026-08-05 — **MCS against a live counterpart**, for the first time

**Our megawatt service ids were accepted by somebody else's stack.** Three ISO 15118-20 MCS sessions ran
from our EVCC to everest-core **2026.02.1**'s `Evse15118D20` — two Scheduled, one Dynamic — each complete
to `SessionStop` with every response `OK`. Their own log names the result:

```
Requested info about ServiceID: 8
Selected MCS service parameters: control mode: Scheduled, mobility needs mode: ProvidedByEvcc
CAR ISO EV selected service: MCS
```

Until today MCS was the one thing in this stack with **no external oracle at all**: service ids 8 / 9 and
the `McsConnector` values were read off EVerest's `libiso15118` headers, and `Secc20McsTests` is our own
two sides agreeing with each other. everest-core 2026.02.1 is the first release to ship
`config/config-sil-mcs.yaml`, which the [2026.02.1 matrix run](../2026-08-05-everest-2026021-matrix/notes.md)
called "the single most valuable next piece of work this run surfaced". This is that work.

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), native build — the same tree as the matrix run |
| Their MCS side | `Evse15118D20` on in-tree libiso15118 **v0.9.1**; `EvseManager` with `connector_type: cMCS` |
| Ours | `Vanaheimr.V2G.Exi` @ `65f60d7`, `Evcc20Mcs` |
| Machine | WSL2 Debian 13 on Windows 11; station in WSL, our EVCC on Windows through an IPv4 TCP relay |
| Driven by | [`sil-car.sh`](../../../tools/interop-everest/sil-car.sh) `CP_AT_PLUGIN=1` + [`sdp-probe.sh`](../../../tools/interop-everest/sdp-probe.sh), both under **bash** |
| Config | [`config-mcs-ours.yaml`](config-mcs-ours.yaml) — their `config-sil-mcs.yaml` plus five lines |
| Fixture | `V2G_INTEROP_MODE=mcs`, the arm added for this run |

## The matrix

| Scenario | Result |
|---|---|
| MCS, Scheduled, session 1 | **complete** (60 exchanges), every response `OK`, service **8** |
| MCS, Scheduled, session 2 | **complete** (57), service **8** — the second session in the same process is fine |
| MCS, Dynamic | **complete** (57), service **8**, `control mode: Dynamic` confirmed on their side |
| service id agreement | **8 = MCS confirmed by their decoder**, not merely accepted as a number |
| MCS_BPT (9) | advertised by them, **not selected** — see finding 3 |

The route is DC's, exactly as the design says it should be:
`ServiceDiscovery → ServiceDetail → ServiceSelection → DC_ChargeParameterDiscovery → ScheduleExchange →
DC_CableCheck ×43 → DC_PreCharge → PowerDelivery → DC_ChargeLoop ×3 → PowerDelivery → DC_WeldingDetection
→ SessionStop`. No codec work was needed for MCS, and this run is the first evidence that the claim
survives contact.

## Finding 1 — what was actually proven, and what was not

**Proven: the catalogue.** Their `Evse15118D20` did not treat 8 as an opaque unsigned short. It answered
`ServiceDetailReq` for it, logged `Selected MCS service parameters`, and told its `EvseManager`
`EV selected service: MCS`. That is two independent implementations agreeing that 8 means MCS, which is
precisely the claim `Secc20Mcs`'s own comment could not make.

**Not proven: the power envelope.** Their MCS SIL is electrically an ordinary charger. The
`DCSupplySimulator` and the two `EnergyNode`s are configured exactly as in the plain -20 DC config, so the
station's HLC limits came out as `22080W/200A` — the same numbers the [-20 DC run](../2026-08-05-everest-2026021-matrix/iso20-dc.s1.flow.md)
produced hours earlier, from the same 3 × 32 A fuse limit. Nothing megawatt-scale crossed the wire in
either direction. **The MCS service id was validated; the MCS power envelope was not**, and their SIL as it
ships cannot validate it.

## Finding 2 — `Evcc20Mcs` is only half the mirror of `Secc20Mcs`

The most useful thing the run surfaced, and only a live counterparty could have: their station logged what
our megawatt truck asked for.

```
Received EV maximum limits: {
    "dc_ev_maximum_current_limit": 200.0,
    "dc_ev_maximum_power_limit": 50000.0,
    "dc_ev_maximum_voltage_limit": 500.0
}
```

50 kW, 200 A, 500 V — an ordinary DC car's envelope, declared **under an MCS service**. The cause is a
one-sided abstraction in the app:

- `Secc20Mcs` overrides `MaxPower` / `MaxCurrent` / `MaxVoltage` / `MinVoltage`, because `Secc20Dc` exposes
  them as `virtual` (3.75 MW / 3000 A / 1250 V).
- `Evcc20Mcs` overrides `PreferredEnergyServiceIds` and **nothing else**, because the EV-side limits in
  `Evcc20Dc` are not virtual — they are literals in the `DC_ChargeParameterDiscoveryReq` and the
  `ChargeLoop` request.

Their station accepted it without complaint (it clamps to its own 22 kW regardless, and warned ten times
that the EV was ignoring the reduced limit — a warning the plain -20 DC run produces ten times too, so it
is not an MCS finding). Nothing failed. But a megawatt truck that asks for MCS and then declares a 50 kW
battery is not the vehicle the service id claims, and the next counterparty may well check.

**Follow-up, in the app, not here:** make the EV-side charge parameters virtual on `Evcc20Dc` the way the
station-side ones already are on `Secc20Dc`, and give `Evcc20Mcs` the megawatt envelope to match. The
harness cannot fix this — `docs/roadmap.md`'s MCS row and `Evcc20Mcs`'s own summary both live in the app.

## Finding 3 — they advertise MCS_BPT too, and our EVCC never sees it

`EvseManager` pushes `MCS_BPT` whenever the power supply reports itself bidirectional
(`EvseManager.cpp:560-576`), and `DCSupplySimulator`'s `bidirectional` **defaults to true** with nothing in
their MCS config overriding it — so the catalogue carried both **8** and **9**. Our EVCC took 8 because
`Evcc20Mcs.PreferredEnergyServiceIds` is `{ 8, 9 }` in that order and the first match wins.

So MCS_BPT went untested here. **It has since been tested, and neither half of that last sentence was
right** — see [`2026-08-05-everest-mcs-bpt`](../2026-08-05-everest-mcs-bpt/notes.md). An EVCC preferring
`{ 9, 8 }` selects 8 anyway, because `Evcc20Base.SelectEnergyTransferService` follows the station's order
and treats our list as a filter; and once 9 *is* selected, their station refuses the session at
`DC_ChargeParameterDiscoveryRes` with `FAILED_WrongChargeParameter`, because our EVCC has no
bidirectional request path to go with the service it asked for. Worth knowing alongside it that their side
reports `bpt_active false` throughout — advertising the service and running bidirectionally are not the
same thing in their SIL either.

## Finding 4 — their MCS config is Dynamic-only out of the box, like their d20 one

Same shape as [finding 3 of the matrix run](../2026-08-05-everest-2026021-matrix/notes.md): stock
`config-sil-mcs.yaml` sets neither control mode on `Evse15118D20`, and the module's defaults are
`supported_dynamic_mode: true` / `supported_scheduled_mode: false`. A Scheduled EVCC against their **stock**
MCS station fails service selection. [`config-mcs-ours.yaml`](config-mcs-ours.yaml) re-enables both, which
is what let two of the three sessions above run Scheduled.

## The config deltas

Their `config-sil-mcs.yaml`, five lines changed, nothing of theirs patched:

```diff
   iso15118_charger:
     config_module:
-      device: auto
+      device: eth0                                    # the interface our relay reaches
+      tls_negotiation_strategy: ACCEPT_CLIENT_OFFER   # explicit; our EVCC offers plain TCP
+      supported_scheduled_mode: true                  # finding 4
+      supported_dynamic_mode: true
   iso15118_car:
     config_module:
-      device: auto
+      device: lo                                      # park their car so it cannot answer our SDP
```

`connector_type: cMCS` on their `EvseManager` — the line that makes this an MCS station at all — is theirs,
untouched.

## Running it again

The per-session ritual is `Evse15118D20`'s, unchanged from the matrix run: its TCP server exists for
**exactly one connection**, so every session needs replug → fresh multicast SDP probe → re-point the relay.

```bash
V2G_INTEROP_SECC=127.0.0.1:15200 V2G_INTEROP_MODE=mcs V2G_INTEROP_RECORD=/tmp/mcs-run \
  dotnet test ISO15118ConformanceTests.Simulation -c Release \
    --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

`V2G_INTEROP_MODE=mcs` implies -20 and refuses a contradicting `V2G_INTEROP_PROTOCOL`; add
`V2G_INTEROP_DYNAMIC=1` for the Dynamic arm. The fixture asserts the negotiated service id is 8 or 9,
because `Evcc20Mcs` falls back to a plain DC service by design and a fallback that completed would
otherwise be filed as an MCS result.

## Artifacts

`mcs-scheduled.s1.*`, `mcs-scheduled.s2.*`, `mcs-dynamic.*` (each `flow.md` / `frames.log` / `trace.json`),
`their-charger.mcs.log`, and `config-mcs-ours.yaml`.
