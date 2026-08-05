# 2026-08-05 — **MCS_BPT (service 9)**: selected, and then refused for the right reason

**Their station rejected our bidirectional session in one exchange, and it was right to.** Our EVCC
selected **MCS_BPT (9)** from everest-core **2026.02.1**'s catalogue, sent an ordinary unidirectional
`DC_ChargeParameterDiscoveryReq` under it, and got:

```
| 7 | DC_ChargeParameterDiscoveryReq | DC_ChargeParameterDiscoveryRes | FAILED_WrongChargeParameter |
```

That is the answer the [MCS run](../2026-08-05-everest-mcs/notes.md) left open — "MCS_BPT is one line of
environment away" — and the answer is **no, it is a piece of work away**, on our side. Two findings came
out of getting there, and the first one is about us.

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), native build, same station process as the MCS run |
| Config | [`config-mcs-ours.yaml`](../2026-08-05-everest-mcs/config-mcs-ours.yaml), unchanged |
| Ours | `Vanaheimr.V2G.Exi` @ `65f60d7`, `McsBptFirstEvcc` (harness-local probe over `Evcc20Dc`) |
| Fixture | `V2G_INTEROP_MODE=mcs V2G_INTEROP_MCS_FIRST=9` |

## Finding 1 — `PreferredEnergyServiceIds` was a set, not a ranking *(fixed; run repeated)*

The probe was written first as the obvious thing: list `{ 9, 8 }` instead of `Evcc20Mcs`'s `{ 8, 9 }`. It
**changed nothing** — the session negotiated 8 again ([`mcs-bpt-order-only.flow.md`](mcs-bpt-order-only.flow.md)),
and the fixture's assertion caught it rather than filing a plain MCS session as a BPT one.

The station was not at fault: its `ServiceDiscoveryRes` carried **both**, in its own order —

```
[4] ServiceDiscoveryRes energy-transfer services: 8 (freeService=False), 9 (freeService=False)
```

— decoded from the recorded frame. The cause is ours, in `Evcc20Base.SelectEnergyTransferService`:

```csharp
var match = offered.FirstOrDefault(s => preferred.Contains(s.ServiceID))
         ?? offered.FirstOrDefault(s => drivable.Contains(s.ServiceID));
```

It walks the **station's** list and takes the first entry we happen to accept. So
`PreferredEnergyServiceIds` — whose own summary says *"best first"* — never ranks anything; the station's
order decides, and our list only filters. `Evcc20Mcs`'s `{ 8, 9 }` and a hypothetical `{ 9, 8 }` are the
same object to this code.

**This is the shape of finding 2 of the [IsoMux run](../2026-08-03-everest-isomux-both/), pointed the
other way.** There, EVerest's mux ignores the SAP `Priority` we send and routes on "mentions -20
anywhere"; here, our EVCC ignores its own stated ranking and follows what the station happens to list
first. Both were found by the same experiment — reverse the ranking and see whether anything moves — and
neither is visible to a run that only ever offers things in one order.

### Fixed in the app, and the run repeated

`SelectEnergyTransferService` now walks *our* list and asks whether the station offers each id, for the
preferred set and then for the drivable fallback:

```csharp
var match = FirstOffered(preferred) ?? FirstOffered(drivable);

ServiceType? FirstOffered(IReadOnlyList<ushort> ranked)
{
    foreach (var id in ranked)
        if (offered.FirstOrDefault(s => s.ServiceID == id) is { } found)
            return found;
    return null;
}
```

The probe went back to stating a full ranking, `{ 9, 8 }` — 8 still in the list, so a station without
MCS_BPT is still usable — and the run was repeated against the same station:

```
Requested info about ServiceID: 9
Selected MCS_BPT service parameters: control mode: Scheduled, mobility needs mode: ProvidedByEvcc
CAR ISO EV selected service: MCS_BPT
```

([`mcs-bpt-ranked.flow.md`](mcs-bpt-ranked.flow.md), [`their-charger.mcs-bpt-ranked.log`](their-charger.mcs-bpt-ranked.log).)
So the EV's ranking now decides. A plain MCS run was repeated alongside it as a regression check —
`Evcc20Mcs`'s `{ 8, 9 }` against the same catalogue still selects 8 and still completes, 60 exchanges,
every response `OK`.

**The offline suite reproduces the defect too**, which it could not before: `Secc20Mcs` advertises
`{ 8, 9 }` in that order — the same shape as EVerest's catalogue — so
`Secc20McsTests.Evcc_RankingDecides_NotTheStationsCatalogueOrder` selects 8 without the fix and 9 with it.
Verified in both directions by reverting the fix and re-running. That is the guard this class of defect
was missing: every loopback test until now had our EVCC's first preference also first in our SECC's
catalogue, so the two rules were indistinguishable.

## Finding 2 — their station enforces the service/parameter coupling

Once 9 is selected — by narrowing the set before the fix, by ranking after it; the rest of the session is
identical either way — the negotiation is unambiguous on both sides:

```
Requested info about ServiceID: 9
Selected MCS_BPT service parameters: control mode: Scheduled, mobility needs mode: ProvidedByEvcc
CAR ISO EV selected service: MCS_BPT
```

Then our EVCC sent what `Evcc20Dc` always sends — a plain `DC_CPDReqEnergyTransferModeType`, charge-only,
which their side logged as

```
Received EV maximum limits: {
    "dc_ev_maximum_current_limit": 200.0,
    "dc_ev_maximum_power_limit": 50000.0,
    "dc_ev_maximum_voltage_limit": 500.0
}
```

— no discharge limits anywhere — and the response was `FAILED_WrongChargeParameter`. Eight exchanges, and
the session ends there.

**This is a correct refusal and a useful one.** Selecting `MCS_BPT` and then declaring charge-only
parameters is not a bidirectional session; ISO 15118-20 carries the direction in the polymorphic
`BPT_*` charge-parameter and control-mode types, and their station checks that the type matches the
service. It is the first external confirmation this project has that the coupling is enforced at all —
our own `Secc20Dc` implements the mirror of it ("respond in kind, per message", from the
[DC_BPT run](../2026-07-22-iso20-dc-bpt-sdp/notes.md)) but has never been told by anyone else that the
rule binds the EV too.

Worth noting for anyone reading their logs: their side logs the exchange at `INFO` and **does not log the
failure**. The `FAILED_WrongChargeParameter` is visible only on the wire — our EVCC decoded it and
aborted; from `their-charger.mcs-bpt.log` alone the session merely stops and the TCP connection closes 5 s
later.

## What this bounds

Our MCS support, as of `65f60d7`, is **unidirectional only**, and now demonstrably so:

- `Secc20Mcs` advertises `{ 8, 9 }` and `Secc20Dc` answers a `BPT_*` request in kind — so **our station**
  can serve a bidirectional MCS EV. Untested against a live BPT EV; EVerest's own car in
  `config-sil-mcs.yaml` is configured `supported_d20_energy_services: MCS`, not MCS_BPT.
- **Our EVCC cannot drive one.** No `Evcc20*` builds a `BPT_DC_CPDReqEnergyTransferModeType` or a
  `BPT_*_DC_CLReqControlModeType`; the bidirectional work was done from the station side, where "the
  direction is driven by what the EV sends" — and what our EV sends is charge-only.

So the honest matrix row is: *MCS_BPT selected and refused; a forward MCS_BPT session needs a BPT request
path in the app first.* That path is the follow-up this run exists to justify, and it is a real feature,
not a flag.

**And one asymmetry the fix's regression test exposed on the way past:** the same exchange EVerest refuses,
*our* station accepts. `Secc20Dc.HandleChargeParameterDiscovery` answers in kind — BPT request, BPT
response; plain request, plain response — and never checks the request against the service the session
selected. So `Evcc_RankingDecides_NotTheStationsCatalogueOrder` runs a whole loopback session that
negotiates MCS_BPT and then charges one way under it, exactly the session EVerest killed at exchange 8.
Not a wire defect and not what that test asserts, but our SECC is lenient where a second implementation is
strict, and that is worth a check of its own.

## Reproducing

Same per-session ritual as the MCS run (replug → multicast SDP probe → re-point relay; their TCP server is
one-per-SDP), then:

```bash
V2G_INTEROP_SECC=127.0.0.1:15200 V2G_INTEROP_MODE=mcs V2G_INTEROP_MCS_FIRST=9 \
V2G_INTEROP_RECORD=/tmp/mcs-bpt \
  dotnet test ISO15118ConformanceTests.Simulation -c Release \
    --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

The fixture asserts the negotiated service is exactly **9** when `V2G_INTEROP_MCS_FIRST=9`, so a run that
falls back to 8 fails loudly instead of passing as an MCS result. The run above fails too, at
`DC_ChargeParameterDiscoveryRes` — **that failure is the finding**, not a broken harness.

## Artifacts

Two captures of the same scenario, before and after the finding-1 fix:

| Prefix | What it is |
|---|---|
| `mcs-bpt-order-only.flow.md` | the probe listing `{ 9, 8 }` **before** the fix — negotiates 8; finding 1 |
| `mcs-bpt.*` | the probe narrowed to `{ 9 }`, the workaround that reached finding 2 |
| `mcs-bpt-ranked.*` | the probe listing `{ 9, 8 }` **after** the fix — negotiates 9, same refusal |

Each of the latter two as `flow.md` / `frames.log` / `trace.json`, plus `their-charger.mcs-bpt.log` and
`their-charger.mcs-bpt-ranked.log`. The raw octets are not kept, as everywhere under `interop-runs/`:
every frame's hex is in `frames.log` beside its decoded name.

A `trace.json` **was** built, which is worth a sentence: the session is refused but not malformed — eight
requests, eight responses, strictly alternating, the last one carrying `FAILED_WrongChargeParameter`. So
this is a replayable corpus-shaped capture of a refusal, which is a more useful artifact than the
`trace-not-built.txt` an aborted session usually leaves behind.
