# 2026-11-26/27 — EVerest 2026.10.0, forward: our car against the released station

**What this run is:** the forward half of the matrix, our EVCC against their stations, repeated on the
**`2026.10.0` release** (`74845c57`). Earlier the same day the
[release run](../2026-11-26-everest-2026.10.0-release/notes.md) re-ran only the reverse arms (their EV
against our SECC) and the PaymentDetails probe. The forward rows of the matrix last ran against
**2026.02.1** ([2026-08-05](../2026-08-05-everest-2026021-matrix/notes.md) and the August runs after
it). This run puts every one of them in front of the release.

**The headline:** `-2` holds without exception. **Every Scheduled `-20` session failed**, 16 of 16, at
`PowerDeliveryRes = FAILED_EVPowerProfileInvalid`. The fault was **ours**: our car anchored its
`EVPowerProfile` at `TimeAnchor` 0, which is 1970. 2026.10.0 is the first station here that checks the
profile against the offer, and it was right to refuse it. The car was fixed the same night
(stack `3d99345`, below). With the fix all ten DC, MCS and IsoMux Scheduled arms complete. `-20` AC
then meets a second wall that is new in the release: the station waits for the contactor to *open* at
`PowerDelivery(Stop)`.

| | |
|---|---|
| Counterparty | EVerest **2026.10.0** (`74845c578`), `~/everest/dist-2610`, 22 modules, built 2026-11-26 |
| Their stations | `EvseV2G` (`-2`), `Evse15118D20` (`-20`, MCS), `IsoMux` (both, plain and TLS) |
| Ours, before the fix | WWCP_ISO15118 `1287d9c`, EVSimulatorApp `ddc37cf` |
| Ours, after the fix | WWCP_ISO15118 `3d99345` (branch `evcc20-power-profile-anchor`), same tree as the run |
| Fixture | `EverestInteropTests.OurEvcc_AgainstTheirEvseV2G_RunsToCompletion`, from `~/i15118` in WSL, straight to their link-local endpoint |
| Driven by | [`scripts/fwd-2610.sh`](scripts/fwd-2610.sh), one arm per manager start. The `sil-car.sh` car goes in **after** the manager every time, a fresh multicast SDP probe runs per session, and `carsim-on-trigger.sh` handles `-20` AC |
| Configs | [`configs/`](configs/): the 2026-08 `config-*-ours.yaml` with one change. The release has no `PyEvJosev`, so the parked idle car is `Ev15118` on `lo`, plaintext, `supported_protocols` set ([`convert-fwd-2610.py`](scripts/convert-fwd-2610.py)) |
| TLS material | the release's own test PKI: trust anchor `V2G_ROOT_CA`, our client chain `VEHICLE_LEAF ← VehicleSubCA2 ← VehicleSubCA1`, PnC contract `MO_CERT_CHAIN.p12`. Nothing was minted |

Every session's flow is in [`sessions/`](sessions/), and the one-line verdicts are in
[`results.txt`](results.txt), produced by [`sum-2610.py`](scripts/sum-2610.py).

## The matrix

| Scenario | 2026.02.1 (August) | **2026.10.0, before the fix** | **after the fix** |
|---|---|---|---|
| `-2` DC EIM, two sessions in one `EvseV2G` process | ✅ ×2 | ✅ **×2** (45/43 pairs), every response `OK` | — |
| `-2` AC EIM | ✅ ×2 | ✅ **×2** (13/13) | — |
| `-2` AC over TLS 1.2 | ✅ | ✅ | — |
| `-2` via IsoMux, plain and TLS 1.2 | ✅ | ✅ both | — |
| `-20` DC Scheduled, ×2 | ✅ | ⛔ `FAILED_EVPowerProfileInvalid` ×2 | ✅ **×2** (71/59) |
| `-20` DC Dynamic | ✅ | ✅ ×4 | — |
| `-20` DC_BPT | ✅ | ⛔ Scheduled · ✅ Dynamic | ✅ Scheduled |
| `-20` DC over mutual TLS 1.3, ×2 | ✅ | ⛔ Scheduled ×2 · ✅ Dynamic | ✅ **×2** |
| MCS (service 8) | ✅ | ⛔ Scheduled · ✅ Dynamic | ✅ |
| MCS_BPT (service 9) | ✅ | ⛔ Scheduled · ✅ Dynamic | ✅ |
| IsoMux `-20` / both, `-2` first | ✅ (routed `-20`) | ⛔ Scheduled · ✅ Dynamic | ✅ both |
| IsoMux over TLS, both-offer | ✅ (TLS 1.2, routed `-20`) | ⛔ Scheduled · ✅ Dynamic | ✅ |
| IsoMux over TLS, `-20` only | alert 70 | `AuthenticationException`: unchanged, TLS 1.2 only | — |
| `-20` AC, AC_BPT (plain, TLS 1.3) | ✅ (window trigger) | ⛔ `FAILED_EVPowerProfileInvalid` | ⛔ **`FAILED_ContactorError` at `PowerDelivery(Stop)`**, after 3 loops |
| `-2` PnC over TLS 1.2 | stalls in `Ongoing`, then `FAILED` | **same**, 369 pairs, `FAILED` | — |
| `-2` PnC without TLS | Contract stripped | **same**: 60 s of `Ongoing` | — |

`manager alive at end: 1` on every arm: no crash, no zombie loop.

## Finding 1 — our `EVPowerProfile` was anchored in 1970, and 2026.10.0 is the first to check

All three EVCCs, C#, Kotlin and Swift, built the same constant profile for `PowerDeliveryReq(Start)`:
`TimeAnchor` 0 and one 3600 s entry at 10 kW. The station's check is new in the release. 2026.02.2 does
not contain `FAILED_EVPowerProfileInvalid` at all.
`lib/everest/iso15118/src/iso15118/d20/state/power_delivery.cpp` now walks every profile entry against
every slot of the selected tuple. Time that no slot covers counts as zero power.

The requirement text (FDIS) supports the station on every point:

- **`[V2G20-1861]`**: `TimeAnchor` is the moment the first entry becomes active. Table 103 encodes it in
  **microseconds** of SECC time. A `PowerSchedule`'s anchor (Table 112) is in **milliseconds**, and
  their code divides by exactly those two factors.
- **`[V2G20-1529]`, `[V2G20-1536]`**: SECC time is the station's clock, defined by the `TimeStamp` of
  its `SessionSetupRes` header.
- **`[V2G20-1559]`**: no entry may exceed the selected schedule at any time. **`[V2G20-1811]`**: before
  the first and after the last schedule entry the power is zero. **`[V2G20-478]`**: a violation draws
  `FAILED_EVPowerProfileInvalid`.

A profile active in 1970 therefore asks for 10 kW where the schedule allows zero. Dynamic mode carries
no schedule (their check returns early for it), which is why every Dynamic arm completed and every
Scheduled one did not. **Not a finding against EVerest. It is the first station to hold us to a rule we
broke.**

**The fix** (stack `3d99345`, ports and corpus in EVSimulatorApp `e1247de`, both on
`evcc20-power-profile-anchor`) is `Iso20EvPowerProfile`. The anchor is the station's latest response
`TimeStamp` in µs, taken from the response rather than from our clock, so a recorded session stays a
pure function of its responses. The entries follow the first tuple's charging schedule from that moment,
each capped at the lower of 10 kW and the slot. Time before the schedule gets a zero entry, nothing runs
past its end, and a schedule that has already ended leaves one zero entry. The same session corpus is
replayed byte for byte by C# (136/136) and Kotlin (18/18). Swift is covered only by the macOS CI.

**And after the fix**, every DC, MCS and IsoMux Scheduled arm completes, 10 of 10. Their log for the
first of them, [`sessions/fix-d20.their-side.log`](sessions/fix-d20.their-side.log), shows
`PowerDeliveryRes` going straight into the charge loop. The failed one before it,
[`sessions/d20.their-side.log`](sessions/d20.their-side.log), stops there.

## Finding 2 — our own SECC offers its schedule at `TimeAnchor` 0 too

`Secc20Base.ScheduleExchange` answers a Scheduled car with
`PowerSchedule(TimeAnchor: 0, 3600 s at 100 W)`, plus a `PriceLevelSchedule` at 0 as well. By
`[V2G20-1811]` that schedule ended in 1970. So the corrected car, replayed against our own corpus,
projects **zero power**: one 0-W entry, the only compliant answer to a schedule that has already ended.
Nothing of theirs found this; the car's fix exposed it. A station that anchors its schedule at the
present is the station-side half of Finding 1, and it is left for its own change. Their `Ev15118`
evidently does not check the schedule's anchor: the reverse arms of the release run completed against
this very offer.

## Finding 3 — `-20` AC now waits for the contactor to open at `PowerDelivery(Stop)`

With the profile fixed, `-20` AC passes `PowerDelivery(Start)`. The window trigger still works,
`PowerOn` arrives 0,8 s into the 3 s close window, and three `AC_ChargeLoop` pairs follow. Then
`PowerDelivery(Stop)` draws `FAILED_ContactorError`:

```
iso15118_charge :: Waiting for contactor is opened
evse_manager    :: CAR ISO AC HLC Open contactor
iso15118_charge :: AC contactor is not opened within 1400ms
```

([`sessions/fix-ac20-trigger.their-side.log`](sessions/fix-ac20-trigger.their-side.log)). This is
new in 2026.10.0. On 2026.02.1 the same arm reached `SessionStop`
([2026-08-13](../2026-08-13-everest-d20-ac-contactor-window/notes.md)). It mirrors the close wait
`carsim-on-trigger.sh` was built for: the release now waits for an *open* event as well, and the SIL car
is still holding CP at state C (`draw_power_fixed 0,0`), so their IEC layer never opens the relay. It is
the same in 7 of 7 AC sessions that got this far: four Scheduled after the fix (AC ×2, AC_BPT, AC over
TLS 1.3) and three Dynamic before it.

**Not yet decided whose this is.** A car is expected to stop drawing current when it stops the
session, so the likely fix is on the rig side: a second trigger that drops CP to state B on
`Waiting for contactor is opened`. That was not tried here. Until it is, this is a rig limitation and
not a finding against EVerest. The 1400 ms bound itself (`#3380` made the close timeout configurable)
was not read against the requirement text.

## What this run did not do

- **Swift was not built**: no toolchain on this machine. The Swift port of the fix is proven only once
  the EVSimulatorApp PR's macOS job is green.
- **The CP drop for Finding 3** was not attempted.
- **PnC is as blocked as in August**: no validator behind their SIL. `AuthorizationReq` stays at
  `Ongoing` until `FAILED` over TLS, and Contract is stripped without TLS. Not re-investigated.
- The 13 still-present findings of the [release sweep](../2026-11-26-everest-2026.10.0-release/notes.md)
  were not driven on the wire. This run asked only whether the matrix holds.

## Next

- Merge the stack PR (`evcc20-power-profile-anchor`, WWCP_ISO15118), then the EVSimulatorApp PR, then
  bump the pointers.
- Anchor `Secc20Base`'s offered schedule at the present (Finding 2). It changes the corpus again.
- Try a state-B trigger on `Waiting for contactor is opened` for `-20` AC (Finding 3).
