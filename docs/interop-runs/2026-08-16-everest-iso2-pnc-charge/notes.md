# 2026-08-16 — the first ISO 15118-2 Plug & Charge session in this project that actually charged

**81 charge-loop iterations, 30,16 kWh, 20 % → 70 %, over TLS 1.2 against EVerest's `EvseV2G`, in 96
seconds of wall clock.** Every earlier `-2` PnC run ended in `CableCheck`, and the difference turned out
to be neither PnC nor their station.

| | |
|---|---|
| Counterparty | everest-core **2026.02.1** (`b61bb12`), `config-dc2-pnc-validator-ours.yaml`, our contract validator standing in for the CSMS |
| Session | `-2` **DC**, forward (our EVCC → their `EvseV2G`), **TLS 1.2**, contract authorization |
| Credential | **theirs**: `everest-aux/certs/client/mo/MO_CERT_CHAIN.p12`, eMAID `UKSWI123456789A` |
| Result | `Passed`, `Authorization: pnc-signed`, `target 70 % reached` |

## What was missing, and it was ours

The [2026-08-13 run](../2026-08-13-everest-contract-validator/notes.md) reached `AuthorizationRes = OK`
and then stopped in `CableCheck` — *"408 frames, stops for want of hardware"*. That sentence was right
and it hid a cause worth writing down.

**The SIL car must be plugged in *after* the manager, not before.** A `sil-car.sh` left over from an
earlier station is subscribed to a broker whose station has since restarted: its plug-in was consumed by
the dead one, so the CP line never reaches state C, the contactor never closes, and `EvseManager` ends
`CableCheck` with

```
MREC11CableCheckFault :: Voltage did not drop below 60V within timeout
Error raised, type: evse_manager/Inoperative
```

which then poisons every following arm as well. **The only symptom on our side is a 60 s `CableCheck`
timeout; the only symptom on theirs is an empty car log** — the arm script now warns when the car
published nothing, because that is the one line that would have saved the first attempt today.

## The three arms

| arm | battery | interval | wall clock | outcome |
|---|---|---|---|---|
| `baseline` | none | default | **8 s** | `Passed` — the fixed three iterations |
| `rate` | 20 → 25 %, 60 kWh | 200 ms | **9 s** | `3,00 kWh in 7 iterations` — the measurement the next arm is sized from |
| `charge` | 20 → **70 %**, 60 kWh | 1 000 ms | **96 s** | **81 iterations, 30,16 kWh, target reached** |

`rate` exists so the last row is arithmetic rather than a guess: **429 Wh per iteration** (≈25,7 kW at
their DC supply's declared limits) and ~134 ms per `CurrentDemand` pair on the wire. 30 kWh ÷ 429 Wh ≈ 70
iterations × (134 ms + 1 s) ≈ 79 s of loop plus ~8 s around it. Measured: 81 iterations, 96 s — the
estimate was 13 % short because the pacing slightly lowers the rate the station reports back.

## The two clocks, which are not the same clock

One charge-loop iteration stands for **one minute of simulated charging**
(`Metering.ChargeLoopSample.Period`), and on the wire it costs an exchange plus whatever interval the run
sets. So the session above is *81 simulated minutes* of charging in *96 real seconds*. Both numbers are
in the battery's own line and neither is the other:

```
Battery: 70.3 % of 60.0 kWh (started at 20.0 %, 30.16 kWh delivered)
         after 81 min simulated in 81 iteration(s) — target 70 % reached.
```

Pulling them apart is what `V2G_INTEROP_CHARGE_INTERVAL` is for. It is deliberately **not**
`Evcc2.PollInterval`, which also paces the authorization poll, `ChargeParameterDiscovery` and
`CableCheck` — those intervals are *measured against* in this project, and raising the shared constant to
make a session last longer would silently move the yardstick under
[`josev-iso20-evcc-charge-loop-pacing`](../../reports/josev-iso20-evcc-charge-loop-pacing.md) and every
finding like it.

## What their station did

- **One backend call per session.** Their `EvseManager` asked the validator exactly once, at
  `Authorization`, and the 81 iterations that followed needed no further verdict
  ([`their-token-calls.jsonl`](their-token-calls.jsonl) — the validator's cumulative log; today's four
  `PlugAndCharge` calls are the last four lines, all `Accepted`).
- **The charge loop is theirs to pace and they did not pace it**: `CurrentDemandRes` came back in ~100 ms
  throughout, so the 1 s between requests is ours alone.
- `PowerDelivery(Stop)` → `powersupply_dc: Set mode: Off` → `WeldingDetection` → `SessionStop`, clean on
  both sides.

## What this does *not* say

- **Nothing new about their conformance.** This is the same station, the same messages and the same
  verdicts as 2026-08-13; what changed is that the session got past a rig fault of ours and ran the loop.
- **The contract is still not validated by anything.** Our validator returns `Accepted` because we tell
  it to. Their `DummyTokenValidator` returns a constant from its config; the *documented* PnC setup puts
  a CSMS there ([their tutorial](https://everest.github.io/latest/tutorials/plug-and-charge.html) uses
  `run-sil-ocpp201-pnc.sh` and an external CSMS), which is the role our arm stands in for. So the matrix
  cell stays `◐`, and for the same reason as before.
- **The battery is linear.** No constant-voltage taper below the 80 % knee, no losses, no temperature.
  20 → 70 % stays under the knee, so this run never exercises the taper at all; a run that ends "at
  100 %" would be reporting arithmetic and not a charging curve.

## Artifacts

[`ours.charge.log`](ours.charge.log) · [`ours.rate.log`](ours.rate.log) ·
[`ours.baseline.log`](ours.baseline.log) — the three arms.
[`flow.charge.md`](flow.charge.md) — the session's message flow.
[`their-station.log`](their-station.log) · [`their-token-calls.jsonl`](their-token-calls.jsonl).

## Next

- The `-20` charge loop has the same three-iteration default and no battery reaches it yet; the knob is
  `-2`-only so far.
- `everest-evsev2g-certificate-update`'s last technical box is **not** what it says it is: their state
  after `SelectedPaymentOption = Contract` is `WAIT_FOR_PAYMENTDETAILS_CERTINST_CERTUPD` and admits
  `CertificateUpdateReq` whatever parameter set was selected, so the wall in front of it is our car
  coupling *Update → set 2*. That is an instrument to build, not a station to wait for.
