# 2026-08-06 — **MCS_BPT, complete** — and the megawatt envelope finally crosses the wire

**Both bounds the MCS runs left standing are gone.** Two ISO 15118-20 sessions ran from our EVCC to
everest-core **2026.02.1**'s `Evse15118D20` under service **9 (MCS_BPT)** — 61 and 58 exchanges, every
response `OK`, both to `SessionStop`. The exchange that ended the [2026-08-05 attempt](../2026-08-05-everest-mcs-bpt/notes.md)
is now green:

| | 2026-08-05 | today |
|---|---|---|
| `[7] DC_ChargeParameterDiscoveryRes` | **`FAILED_WrongChargeParameter`** | **`OK`** |

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), native build |
| Config | [`config-mcs-ours.yaml`](../2026-08-05-everest-mcs/config-mcs-ours.yaml), unchanged |
| Ours | `Vanaheimr.V2G.Exi` @ `e8144c7`, `McsBptFirstEvcc` over `Evcc20Dc` |
| Fixture | `V2G_INTEROP_MODE=mcs V2G_INTEROP_MCS_FIRST=9` |

## Finding 1 — the refusal is gone, and their side says why

The app's bidirectional request path (`EnergyTransferService`, `Evcc20Base.BidirectionalService`,
`Evcc20Dc`'s `BPT_*` arms) means the EV now asks in kind on the direction axis. Their station read the
discharge half and logged it — a line that did not exist in any earlier run:

```
Requested info about ServiceID: 9
Selected MCS_BPT service parameters: control mode: Scheduled, mobility needs mode: ProvidedByEvcc
CAR ISO EV selected service: MCS_BPT
Max discharge current 3000.000000A
```

So the service/parameter coupling that refused us on 2026-08-05 is now satisfied from our side, confirmed
by the same implementation that enforced it. That closes the finding both ways: they check it, and we
now pass the check.

## Finding 2 — 3.75 MW, read back by a foreign decoder

The [first MCS run](../2026-08-05-everest-mcs/notes.md) said plainly that it validated the catalogue and
not the envelope. It does now:

```
Received EV maximum limits: {
    "dc_ev_maximum_current_limit": 3000.0,
    "dc_ev_maximum_power_limit": 3750000.0,
    "dc_ev_maximum_voltage_limit": 1250.0
}
```

3.75 MW at 3000 A and 1250 V, decoded and reported by their `EvseManager`, with 3000 A on the discharge
half as well. `RationalNumberType` is `(sbyte exponent, short value)` and the megawatt figures need the
exponent — this is the first evidence that a second implementation reads those exponents the way we
write them.

**What it still does not prove:** their SIL clamps to its own 22 kW whatever is declared
(`Change HLC Limits: 22080W/…`, `bpt_active false` throughout). So the megawatt *declaration* is
externally validated; megawatt *power*, and actual reverse flow, are not — and cannot be against this
counterpart.

## Finding 3 — the probe under-declared, and only the wire showed it

Session 1 is kept deliberately, because it is a defect of ours that no offline test could have caught.
Their log for that session reads:

```
dc_ev_maximum_power_limit: 50000.0      ← under service 9
Max discharge current 200.000000A
```

50 kW under an MCS_BPT service — **exactly the defect the app had just fixed for `Evcc20Mcs`**, reappearing
in the harness's own probe. `McsBptFirstEvcc` derives from `Evcc20Dc` rather than `Evcc20Mcs`, because the
latter is `sealed`, so it inherited the ordinary DC envelope while asking for a megawatt service. Fixed
here by repeating `Evcc20Mcs`'s four properties in the probe; session 2 is the result.

The duplication is the price of `sealed`, and the better home is the app — either unseal `Evcc20Mcs`, or
let it rank the bidirectional service first so a probe is not needed at all. Worth noting how the defect
survived: the app fix was verified by a loopback test against our own SECC, which clamps nothing and
reports nothing, so a wrong envelope there is invisible. It took a station that logs what it received.

## Reproducing

Per-session ritual as ever (`Evse15118D20`'s TCP server is one-per-SDP: replug → multicast probe →
re-point relay), then:

```bash
V2G_INTEROP_SECC=127.0.0.1:15200 V2G_INTEROP_MODE=mcs V2G_INTEROP_MCS_FIRST=9 \
V2G_INTEROP_RECORD=/tmp/mcs-bpt \
  dotnet test ISO15118ConformanceTests.Simulation -c Release \
    --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

## Artifacts

| Prefix | What it is |
|---|---|
| `mcs-bpt.dc-envelope.*` | session 1 — complete, but declaring the DC envelope; finding 3 |
| `mcs-bpt.megawatt.*` | session 2 — the same session with `Evcc20Mcs`'s envelope, 3.75 MW on the wire |

Each as `flow.md` / `frames.log` / `trace.json`, plus `their-charger.mcs-bpt.log` covering both.
