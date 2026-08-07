# 2026-08-07 — two Porsche Taycan captures: 40 watts short, and a third car that polls twice

The remaining AC material in tux-evse's `trace-logs/`, through the pipeline the VW run established:
their `pcap-iso15118` → [`scenario-relax.py`](../../../tools/interop-tux-evse/scenario-relax.py) →
their injector → our SECC. Two captures of a **Porsche Taycan 4S**, one per side of the car, each run
folded (`--compact=basic`) and unfolded (`--compact=none`).

| | |
|---|---|
| Counterparty | [tux-evse/iso15118-simulator-rs](https://github.com/tux-evse/iso15118-simulator-rs) `main` @ `fc51088`, source build |
| Ours | conformance suite @ `0a6d2e6`, `EVSimulatorApp` @ `d7a5ee4` |
| Direction | `←SECC`, ISO 15118-2 **AC**, EIM, plain TCP |
| Outcome | **One finding against us** — our AC schedule was 40 W below what a 3×16 A charge point offers, so a real car's profile was refused (fixed the same day, see below) — and the sequence-guard fix confirmed on a second and third real route |

Artifacts: `{driverside,otherside}.{basic,none}.{flow.md,frames.log}`, `charging-profiles.txt`,
`our-station.*.log`, `their-injector.*.log`.

## Finding — `FAILED_ChargingProfileInvalid`, by forty watts

Both folded runs get seven exchanges and stop at the same place:

```
5  ChargeParameterDiscoveryReq → ChargeParameterDiscoveryRes  OK
6  PowerDeliveryReq            → PowerDeliveryRes             FAILED_ChargingProfileInvalid
```

Their injector's view of the same moment: `received:"charging_profile_invalid" expected:"ok"`.

Our station is applying [V2G2-761] exactly as written — every `ChargingProfile` entry must stay within
the `PMax` active at its start time — and the arithmetic is not close:

```
Porsche Taycan 4S, both sides   profile entry start=0   power_max = 11,040 W
our station (Secc2.PlainSchedule)  one tuple            PMax      = 11,000 W
```

**11,040 W is not an odd number: it is 3 × 230 V × 16 A**, the ubiquitous European three-phase AC
charge point. 11,000 W is the round number people say out loud. Our simulator picked the round one, so
every car captured at a real 16 A charger overshoots our offer by 0.4 % and is refused at
`PowerDelivery` — the last message before charging would have begun.

Nothing here is a protocol error on either side. The station may offer what it likes and must then
enforce it; the car may ask for what its charger offered. But for a station whose *purpose* is interop
testing, a rounded PMax manufactures a failure no real charger would produce, and it does so at the
worst possible moment — after everything else has gone right.

The VW capture in the [earlier run](../2026-08-06-tux-head-reverse/notes.md) asked for 4,100 W and
passed the same check, which is why this waited for the second and third car to surface.

### Fixed the same day — and what the fix moved

`Secc2` now offers 11,040 W, in the plain schedule **and** in tuple 1 of the tariff offer: a car that
is not price-aware picks tuple 1 and would have met the same wall there. A regression test pins both
halves — the Taycan's exact figure is accepted, one watt more is still refused, or raising the offer
would only have made the enforcement decorative.

The recorded corpus moved with it, and the diff is worth reading because it is small and entirely
predictable: two frames per -2 trace (the offer in `ChargeParameterDiscoveryRes`, the profile in
`PowerDeliveryReq`, in all five -2 sessions), and the AC energies **183 / 366 / 549 → 184 / 368 /
552 Wh** on the wire and in the OCPP record. Everything else that moved is randomised ECDSA.

Two things surfaced behind it, neither caused by this change:

- **`Bridge.events.json` had been stale since the EVSETimeStamp corpus move.** It is generated from
  these traces, but it lives in the app and nothing in the offline conformance run replays it, so the
  app's own `EveryEventIsUnchanged` went red unnoticed on 2026-08-06. Same for all six vendored demo
  traces under `app/src/vendor/traces/`. Both regenerated here.
- **The ports lost their only per-sample-rounding check.** 11,040 / 60 is exactly 184 Wh, so the -2 AC
  trace no longer distinguishes rounding per sample from integrating and rounding once — which is
  precisely what the Kotlin and Swift trace-test comments were built around (183.33 → 183 three times,
  not 550). C# still checks the rule head-on; neither port has a metering test of its own. Written
  into both comments rather than quietly dropped.

### Re-run against their injector — the same captures, the same day

The rig back up, the fixture rebuilt at 11,040 W, and all four scenarios replayed — folded first, then
unfolded. Artifacts carry `.fixed.` in the name and sit beside the ones that found the fault, so before
and after read together.

Both sessions now run to the end — **10 exchanges, every response `OK`**, where before they stopped at
seven:

```
5  ChargeParameterDiscoveryReq → ChargeParameterDiscoveryRes  OK
6  PowerDeliveryReq            → PowerDeliveryRes             OK    ← was FAILED_ChargingProfileInvalid
7  ChargingStatusReq           → ChargingStatusRes            OK    ← the charge loop, first time on AC
8  PowerDeliveryReq            → PowerDeliveryRes             OK
9  SessionStopReq              → SessionStopRes               OK
```

Their injector's own TAP output agrees, on both captures, with nothing to interpret:

```
1..12 # porsche-taycan-4s-driverside-ac-iso2:1:0
ok 0007 - iso2:power_delivery_req(pkg:78/1)      # Checked     ← the transaction that failed
ok 0008 - iso2:charging_status_req(pkg:86/247)   # Checked
ok 0010 - iso2:session_stop_req(pkg:1340/1)      # Checked
```

Two things fall out of it that were not the point.

**Their spin did not happen.** 20 KB of injector log against 2.4 GB. The loop needs a closed socket
*and* a queue of unplayed transactions; a session that ends properly has neither. That is a cleaner
confirmation of the [diagnosis we sent them](../../reports/tux-evse-spin.md) than the reproduction was
— it shows the trigger by removing it.

**`charging_status_req` is now in `TuxEvseScenario.Vocabulary`**, and it got there the way that table's
rule demands: *their* tools produced the spelling. Their `pcap-iso15118` emitted it converting these
captures, and their injector printed it back in the TAP line above. Until this run no AC session had
ever reached the charge loop against us, so the verb had never been seen — and while it was missing,
the flow comparison counted our own `ChargingStatusReq` as a divergence. Both reports now end with
**"The order matches the declared flow exactly."**

#### Unfolded, re-run too — and unchanged, which is the answer

Both `--compact=none` scenarios were replayed against the same fixed station, and both end exactly
where they did before: **6 exchanges, `AuthorizationRes(FAILED_SequenceError)` at the second poll**,
their injector reporting `received:"sequence_error" expected:"ok"`. Nothing else was possible — the
session dies four messages before `PowerDelivery`, so a schedule fix cannot reach it. Worth running
rather than reasoning about: "the fix changed nothing here" is a claim, and now it is a measurement.

The widened verb table does change what the report *says*, and for the better. The charge loop the
session never reached is now named among what it missed:

```
-   ChargeParameterDiscoveryReq   (in the scenario, never on the wire)
-   PowerDeliveryReq              (in the scenario, never on the wire)
-   ChargingStatusReq             (in the scenario, never on the wire)   ← invisible before
-   PowerDeliveryReq              (in the scenario, never on the wire)
-   SessionStopReq                (in the scenario, never on the wire)
```

**And a rig hazard, found the hard way.** Their binder spins as before — 394 MB and 389 MB under a 20 s
cap, the same ~20 MB/s as the first run's 2.4 GB under 120 s. What is new is that **`timeout`'s SIGTERM
stops the spin but does not end the process**: the driverside binder sat there holding the runner open
for ten minutes until it was SIGKILLed by hand. `run-injector.sh` now passes `timeout -k 5`. This is
worth adding to [the spin report](../../reports/tux-evse-spin.md) before it goes to IoT.bzh — a binder
that ignores SIGTERM is a second, independent problem from the loop itself.

## The sequence guard, on two more real routes

Unfolded, both captures show the pattern the VW showed: **the car sends `AuthorizationReq` twice**,
because the charger it was recorded against answered the first with `EVSEProcessing=Ongoing`. Ours
answers `Finished` at once, so the second poll is out of sequence here.

That is now three real cars — a VW, and a Taycan from both sides — with the same shape, which makes it
a property of captured sessions rather than a quirk of one file. And the
[fix from 2026-08-06](../2026-08-06-tux-head-reverse/notes.md) holds on all of them:

```
6 request frame(s), 6 response frame(s)          ← the refusal is answered, not swallowed
```

Both unfolded runs end with `AuthorizationRes(FAILED_SequenceError)` on the wire, their injector
reporting `received:"sequence_error" expected:"ok"`, and our fixture naming it through
`SeccOutcome.SequenceErrorAt` — the harness reporting its own refusal, which before 2026-08-06 was an
exception nobody outside the process could read.

## Their spin, a fourth time — and faster

Once our station has ended the session, their injector still has 250-odd transactions to play and goes
into the loop [reported to them](../../reports/tux-evse-spin.md): **2.4 GB in about two minutes** on the
driverside run, 619 MB in the 30 s the otherside run was allowed. That is an order of magnitude beyond
the 365 MB / 11 s of the isolated reproduction, because here the socket is closed *and* a long scenario
is still queued. Excerpted rather than kept: `their-injector.none.excerpt.log`.

## How to reproduce

```bash
# convert both captures, both compaction modes, and relax the expects
pcap-iso15118 --pcap_in=afb-test/trace-logs/porsche-taycan-4s-driverside-ac-iso2.pcap \
              --json_out=porsche-driverside.json --compact=basic
./scenario-relax.py porsche-driverside.json porsche-driverside-relaxed.json --autorun

# our station: AC this time
V2G_INTEROP_LISTEN=55000 V2G_INTEROP_SDP=evse-veth V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=ac \
V2G_INTEROP_RECORD=/tmp/porsche V2G_INTEROP_SCENARIO=$PWD/porsche-driverside-relaxed.json \
  dotnet test -c Release ISO15118ConformanceTests.Simulation \
    --filter "FullyQualifiedName~TuxEvseInteropTests.TheirInjector"

# theirs, in the namespace, capped — see the spin above
sudo ip netns exec tuxev bash run-injector.sh porsche-driverside-relaxed.json 60 /tmp/inj.log
```

## What is left in their trace-logs

Only `tesla-3-din.pcap`, and it is not a scheduling question: nothing in this project speaks DIN 70121.
It remains the only DIN material this project has seen, and the only unused capture they ship.
