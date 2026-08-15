# 2026-08-15 — Dynamic control mode in reverse, and three sessions a frame count cannot tell apart

**Their `PyEvJosev` ran our station's Dynamic control mode — plain and over mutual TLS 1.3 — and the
control arm ran Scheduled against the same rig with one environment variable removed.** All three
sessions are **identical in every count**: 53 exchanges, 33 charge loops, one CableCheck, two PreCharges,
five WeldingDetections. The only difference is the mode, and until this run our station had no way to
report it.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `PyEvJosev` |
| Config | [`config-dc20-dynamic-reverse-ours.yaml`](config-dc20-dynamic-reverse-ours.yaml) — the DC_BPT reverse config with `supported_d20_energy_services: DC_BPT` → **`DC`**, so the run's only variable is the control mode |
| Ours | `EverestInteropTests.TheirPyEvJosev_AgainstOurSecc_RunsToCompletion`, `V2G_INTEROP_DYNAMIC=1` |
| Outcome | **Dynamic ×2** (plain, TLS) and **Scheduled ×1** (control) — all three complete, all responses `OK` |

## The three arms

| arm | our offer | transport | req+res | loops | **their control mode** |
|---|---|---|---:|---:|---|
| plain | Dynamic first | TCP | 106 | 33 | **Dynamic** |
| tls | Dynamic first | mutual TLS 1.3 | 106 | 33 | **Dynamic** |
| **control** | **Scheduled first** | TCP | 106 | 33 | **Scheduled** |

That table is the finding. `[V2G20-2656]` has the SECC support both modes, so our station **always
advertises both** and `PreferDynamicControlMode` only decides which parameter set comes *first* in
`ServiceDetailRes`. An EV is free to take either — and one that takes the wrong one completes to
`SessionStop`, answers `OK` to everything, and produces a session indistinguishable from the right one
afterwards. Our station answers in kind (`[V2G20-1600]`) either way, so not even the response types
diverge in a way any count would show.

**The control arm is what makes the other two mean anything.** It is the same rig with
`V2G_INTEROP_DYNAMIC` removed, and their EV switched with the offer — which also measures something this
repository had been asserting from source: `Secc20Base`'s own documentation says the order *"only decides
which one an EV that simply takes the first offered set (e.g. Josev) actually runs"*. **Their EV is
exactly that**, now measured rather than inferred, and in the direction where they do the choosing.

## What had to be added, and where it belonged

The station branched on `req.Dynamic_SEReqControlMode is not null` in `ScheduleExchange` and threw the
answer away. So:

```csharp
// Secc20Base — what the car chose, recorded before it is answered.
EvControlModeIsDynamic = req.Dynamic_SEReqControlMode is not null;
```

`null` until `ScheduleExchange` arrives, which matters as much as the two values: a session that died
earlier must not read as *Scheduled*. `SeccOutcome` carries it, the fixture prints it, and asserts it when
the run claims Dynamic.

**This is the fourth instance in three days of one shape** — *a value our own side already held that no
caller could reach* — after the reverse fixture's defaulted power mode (08-13), the interop TLS callback's
discarded peer chain and the reverse fixture's unused TLS options (08-14). The pattern is worth naming
because the four were found the same way and none of them by a test: **narrow one input until the two
outcomes stop being the same**.

The regression is
[`Secc20DynamicModeTests.TheStationRecordsWhichControlModeTheCarChose`](../../../ISO15118ConformanceTests.Simulation/StateMachines/Secc20DynamicModeTests.cs)
— all three values, including the null. Its first version failed for a reason worth keeping: the request
headers were built before `OpenSession`, so they carried a SessionID `[V2G20-460]` refuses and never
reached `ScheduleExchange` at all. The property read `null` and the test was right to say so.

## Reproduce

```bash
sed 's/supported_d20_energy_services: DC_BPT$/supported_d20_energy_services: DC/' \
    config-dc20bpt-reverse-ours.yaml > config-dc20-dynamic-reverse-ours.yaml
```

```bash
V2G_INTEROP_LISTEN=15118 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc \
V2G_INTEROP_DYNAMIC=1 V2G_INTEROP_CHARGELOOP=20000 \
  dotnet test -c Release --artifacts-path ~/wsl-artifacts \
              --filter FullyQualifiedName~EverestInteropTests.TheirPyEvJosev &
until ss -lnt | grep -q ':15118 '; do sleep 1; done

~/everest/dist/bin/manager --config ~/everest/configs-ours/config-dc20-dynamic-reverse-ours.yaml
```

**Drop `V2G_INTEROP_DYNAMIC=1` for the control**, and add
`V2G_INTEROP_TLS_SERVER=~/everest/tlsac/secc.p12:123456 V2G_INTEROP_TLS_REQUIRE_CLIENT=1` for the TLS arm
after [`tls-pki-setup.sh`](../../../tools/interop-everest/tls-pki-setup.sh). `V2G_INTEROP_CHARGELOOP=20000`
is there for the reason [the forty-seventh filing](../../reports/josev-iso20-evcc-charge-loop-pacing.md)
exists, and **a run that used it is not a passing charge-loop conformance result.**

## Artifacts

[`plain/`](plain/), [`tls/`](tls/) and [`control/`](control/) — flow, frames, both octet streams, both
sides' logs. No `trace.json`: their EV signs the `AuthorizationReq` with a key that is theirs, so
`SessionTrace.Build` refuses the recording rather than substitute the signature and verify nothing.

## Next

- **Dynamic in reverse over AC**, which is now the same environment variable against the AC reverse
  config — worth it because AC and DC take different code paths through `ScheduleExchange`'s answer.
- A reverse **`-2`** run over TLS 1.2, still one environment variable.
