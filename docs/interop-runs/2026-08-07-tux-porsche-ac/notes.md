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
| Outcome | **One finding against us** — our AC schedule is 40 W below what a 3×16 A charge point offers, so a real car's profile is refused — and the sequence-guard fix confirmed on a second and third real route |

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

**Open**, and the fix is one constant in the app (`Secc2.PlainSchedule`, 11,000 → 11,040 W, or a value
with headroom). It moves the recorded corpus, since `ChargeParameterDiscoveryRes` carries the offer, so
it wants deciding rather than doing quietly.

The VW capture in the [earlier run](../2026-08-06-tux-head-reverse/notes.md) asked for 4,100 W and
passed the same check, which is why this waited for the second and third car to surface.

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
