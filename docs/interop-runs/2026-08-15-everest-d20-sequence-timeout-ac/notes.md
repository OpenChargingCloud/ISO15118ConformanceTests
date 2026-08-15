# 2026-08-15 — the sequence timeout, now measured on the AC path too

[The filing](../../reports/everest-d20-sequence-timeout.md) measured the `-20` **DC** charge loop on
2026-08-11 and left one box open: *"decide whether the AC path deserves its own measurement"*. The
constant is shared, so DC settles AC by construction — but **by construction is not by measurement**,
and Table 216 is a different requirement from Table 217. This runs the AC arm.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `Evse15118D20` / `libiso15118`, `config-ac20-ours.yaml`, plain TCP |
| Ours | `-20` AC EIM, service 1, with `V2G_INTEROP_SILENT=90` in the silent arm |
| Outcome | **60,016 s** where `[V2G20-1500]` and Table 216 give the SECC **0,5 s** |

## The two arms

| arm | our EV | outcome |
|---|---|---|
| **control** | charges normally | full session, `SessionStopReq`/`Res` |
| **silent** | stops sending after a charge loop, holds the connection open | their station ended the session **60,016 s** later |

From their log, the two lines that decide it:

```
15:01:18.793896 [INFO] evse_manager:Ev  :: EVSE ISO V2G AcChargeLoopRes
15:02:18.809863 [ERRO] iso15118_charge  :: Sequence Timeout 40secs is reached. Stopping the session
```

**60,015967 s** between their last `AcChargeLoopRes` and their own timeout verdict. Allowed: 0,5 s. The
DC arm four days ago was 60,0025 s, so the two agree to within 14 ms — which is what a shared constant
should look like, now shown rather than assumed.

The control matters here exactly as it did for DC: it shows the car reaches and completes the AC charge
loop, so the silent arm is not a car that never got there. Both arms negotiated `Authorization: eim` and
**energy transfer service 1 (AC)**.

The wrong log line reproduces unchanged on the AC path: *"Sequence Timeout 40secs"*, where the constant
is 60 s and the allowance is 0,5 s. It names neither, in either protocol.

## What this adds, honestly

**Not much, and that is the point of running it.** The finding was already sound: one constant, one call
site, and Tables 216 and 217 both overridden by it. What the run buys is that
`everest-d20-sequence-timeout.md` no longer says *"the AC table is violated by construction"* — it says
the AC table was violated on the wire, with a number, at a timestamp, in their own log. A reviewer who
would have asked for it now does not have to.

It also closes the box the cheapest way available: same rig, one variable (`V2G_INTEROP_MODE=ac`), one
extra config, twenty minutes.

## What the run cost, which was not the measurement

Three attempts, all ours, and all three are recorded because each is a trap this harness has met before
and will meet again:

1. **`sh` is dash.** `sdp-probe.sh` builds its datagram with `printf '\x01\xfe…'`, which dash does not
   understand; their station received the datagram and answered *"Sdp server received an unexpected
   payload"*. Run it with `bash`. This is [the recorded trap](../../../tools/interop-everest/README.md)
   and it still cost a pass.
2. **`FAILED_ContactorError` on the first proper attempt** — because the rig started the SIL car with
   `CP_AT_PLUGIN=1`, and their `-20` AC `PowerDelivery` waits for a contactor *event* that nothing
   re-reads if it arrived earlier. That is their own filed finding
   ([`everest-d20-ac-contactor-edge`](../../reports/everest-d20-ac-contactor-edge.md)) and **not** the
   one being measured, so the rig has to avoid it: hold the car at state B and raise CP inside the
   window with [`carsim-on-trigger.sh`](../../../tools/interop-everest/carsim-on-trigger.sh). Measuring
   one defect through another is how a run note ends up describing the wrong thing.
3. **`exit 127` from a shell subtlety.** `${SILENT:+V2G_INTEROP_SILENT=$SILENT} dotnet test …` does not
   set a variable: an assignment produced by an expansion is a command word, so the run died before
   reaching `dotnet`. `export` first, then call.

Their station also dropped four session logs into the repository root over the week
(`260810_*.yaml`, `260815_*.yaml`) — already covered by `.gitignore` since 2026-08-10, removed here.

## Reproduce

```bash
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-ac20-ours.yaml &
CP_AT_PLUGIN=0 bash tools/interop-everest/sil-car.sh &
bash tools/interop-everest/carsim-on-trigger.sh --watch <their log> &
bash tools/interop-everest/sdp-probe.sh eth0          # bash, not sh
```

```bash
export V2G_INTEROP_SECC='[<their-addr>%eth0]:<port>' V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac
export V2G_INTEROP_SILENT=90        # omit for the control arm
dotnet test -c Release --artifacts-path ~/wsl-artifacts \
  --logger "console;verbosity=detailed" \
  --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

## Artifacts

[`control/`](control/) and [`silent/`](silent/) — their manager's log and our EVCC's output per arm,
ANSI colour stripped and nothing else changed.

Offline gate: **1 405 green**, four assemblies, exit code 0. No code changed in this run.

## Next

- **Nothing.** [`everest-d20-sequence-timeout.md`](../../reports/everest-d20-sequence-timeout.md) now has
  one box left and it is *post under your own name*.
