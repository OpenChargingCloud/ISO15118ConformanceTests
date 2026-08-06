# 2026-08-06 — **BPT without MCS**: DC_BPT complete ×2, and the service their SIL had been offering all along

**Two complete bidirectional DC sessions under service 6, Scheduled and Dynamic, and AC_BPT negotiated up
to their known contactor wall.** The last `▢` in EVerest's column, and it was unreachable from this
repository rather than from their station.

```
Energy transfer service: 6 (DC_BPT).           ← ours
Requested info about ServiceID: 6              ← theirs
Selected DC_BPT service parameters: control mode: Scheduled, mobility needs mode: ProvidedByEvcc
CAR ISO EV selected service: DC_BPT
Max discharge current 200.000000A
```

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), native build in WSL2 |
| Their station | `Evse15118D20` + `EvseManager` (`bpt_channel: Unified`, `bpt_generator_mode: GridFollowing`), `DCSupplySimulator` |
| Ours | `Evcc20Dc` / `Evcc20Ac` with `PreferBidirectionalService`, via `EverestInteropTests` |
| Configs | `config-d20-ours.yaml`, `config-ac20-ours.yaml` — **unchanged**; both already advertised BPT |
| Command | `V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc V2G_INTEROP_BPT_FIRST=1 dotnet test --filter …OurEvcc…` |

| Run | Service | Control mode | Exchanges | Outcome |
|---|---|---|---|---|
| `dc-bpt.scheduled` | **6** DC_BPT | Scheduled | 71 | ✅ `SessionStop`, every code `OK` |
| `dc-bpt.dynamic` | **6** DC_BPT | Dynamic | 60 | ✅ `SessionStop`, every code `OK` |
| `ac-bpt` | **5** AC_BPT | Scheduled | 10 | ⛔ `PowerDeliveryRes` → `FAILED_ContactorError` |

## Finding 1 — the service was there the whole time; our EV could not ask for it

Neither config was touched for this run. `EvseManager` appends the `*_BPT` entry whenever its power supply
reports itself bidirectional, and `DCSupplySimulator` defaults to exactly that — so **every -20 DC run this
project has ever made against EVerest saw a catalogue containing service 6, and took service 2.**

The reason is ours. `Evcc20Base.PreferredEnergyServiceIds` lists the unidirectional entry first, and the
only way to rank the other way was a harness-local `McsBptFirstEvcc` subclass of `Evcc20Mcs` with the list
written out reversed. That reached MCS_BPT and nothing else: the AC and DC rankings live on `Evcc20Base`,
and `Evcc20Ac` is `sealed`, so the same trick did not generalise — and `RunEvccAsync` refused
`mcsBptFirst` outright unless `mcs` was also set. Services 5 and 6 were unreachable from this repository
altogether, which is why the matrix cell said `▢` next to a station that was advertising them.

Replaced by `Evcc20Base.PreferBidirectionalService`, a stable reorder of whatever ranking the subclass
states, so one flag covers all three catalogues and the probe subclass is gone. It sits beside
`PreferDynamicControlMode`, which is the same kind of thing: a choice the vehicle makes among what the
station offers.

Worth naming the shape, because it is now the third time: a knob written for the case in front of us,
narrower than the thing it models, and the narrowness hid a gap rather than causing a failure. The other
two were `PreferredEnergyServiceIds` being a set rather than a ranking, and `RunSeccAsync` returning a
bare `Boolean`.

## Finding 2 — their station decodes our discharge limits

`Max discharge current 200.000000A`, read back from our `BPT_DC_CPDReqEnergyTransferModeType`. That is the
DC envelope's own figure, so unlike the MCS_BPT run it says nothing about megawatts — what it does say is
that the bidirectional request path works on the ordinary DC catalogue too, and not only under service 9
where it was first proven.

Their `EvseManager` also logs `bpt_active false` throughout, which is not a contradiction: the session is
BPT-*capable* and currently importing. Nothing in this run discharges — their SIL is a source, and
`GridFollowing` with no export request is a charge.

## Finding 3 — AC_BPT negotiates, then meets the wall that was already known, with a name now

Their station answered `ServiceDetailReq` for **5**, logged `EV selected service: AC_BPT`, and accepted our
`AC_ChargeParameterDiscoveryReq` — so the AC bidirectional catalogue entry and our `BPT_AC_*` request types
are both fine. Then:

```
Waiting for contactor is closed
CAR ISO AC HLC Close contactor
→ PowerDeliveryRes: FAILED_ContactorError
```

This is the known -20 AC bound (`docs/roadmap.md`: *"their -20 AC expects their own EV module to close the
contactor"*), and the run sharpens it in two ways: it now reaches `PowerDelivery` rather than stopping at
`ScheduleExchange`, and the refusal has an explicit response code instead of a stall.

Tried twice with different car-simulator sequences — `draw_power_fixed 0,0` (CP held at state C from the
plug-in) and `iec_wait_pwr_ready` followed by `draw_power_regulated 16,3`. Identical result, which is the
evidence for the diagnosis: the contactor confirmation their `EvseManager` waits on comes from their EV
module, and in this topology that module is on `lo` with no session. Driving the CP line is not enough,
exactly as the roadmap concluded.

## Running it

The relay is gone. `dotnet test` runs **inside WSL** now, so the fixture connects straight to
`[fe80::…%eth0]:50000` and the `socat` hop the earlier forward runs needed is unnecessary. Otherwise the
ritual is unchanged: manager, `sil-car.sh` with `CP_AT_PLUGIN=1`, wait for `SLAC MATCHED`, then a **fresh
multicast SDP probe per session** — `Evse15118D20` creates its TCP server when the probe arrives, so a
second session against a stale port gets an RST.

## Artifacts

`dc-bpt.scheduled.{flow.md,frames.log,trace.json}`, `dc-bpt.dynamic.{…}`, `ac-bpt.{flow.md,frames.log}`,
and their station logs for both configs. Both DC runs are EIM and unsigned, so both became corpus traces.
The AC run has none: the session ended on a `FAILED_*` response, which is exactly the case
`SessionTrace.Build` refuses and the frame log exists for.
