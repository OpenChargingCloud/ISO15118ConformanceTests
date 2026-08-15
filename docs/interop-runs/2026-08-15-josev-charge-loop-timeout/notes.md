# 2026-08-15 — Josev's charge-loop timeout, measured — after two of our own knobs turned out not to reach it

[The filing](../../reports/josev-iso20-charge-loop-timeout.md) is a source finding and says so on its
first checklist line: *"Run it. This is a source finding: their SECC was **not** brought up for it."*
This runs it. Their own log names the value.

| | |
|---|---|
| Counterparty | [SwitchEV/iso15118](https://github.com/SwitchEV/iso15118) **`d645255`**, their SECC in host mode, plain TCP |
| Ours | our EVCC through `JosevInteropTests`, `V2G_INTEROP_SILENT=90` |
| Outcome | **60,06 s** on both energy modes, where Tables 216/217 give the SECC **0,5 s** |

## The measurement

Four arms — a control and a silent arm per energy mode. The control charges normally to `SessionStop`;
the silent arm stops sending after a charge-loop response and holds the connection open.

```
DC   13:20:16,012  Sent DC_ChargeLoopRes
     13:21:16,073  Reason: TimeoutError occurred. Waited for 60.0 s after sending last message
                   Session ended in DCChargeLoop

AC   13:23:59,758  Sent AC_ChargeLoopRes
     13:24:59,818  Reason: TimeoutError occurred. Waited for 60.0 s after sending last message
                   Session ended in ACChargeLoop
```

**60,061 s** and **60,060 s**. Table 217 (`[V2G20-1502]`) and Table 216 (`[V2G20-1500]`) both allow
**0,5 s** after a charge-loop response.

**Their log names the number it waited on** — *"Waited for 60.0 s"* — which makes this a stronger piece
of evidence than the equivalent EVerest measurement, where the interval had to be taken between two
timestamps. Here their own code prints the value the report says is wrong, in the state the report says
it is wrong in: `DCChargeLoop` and `ACChargeLoop`.

Both energy modes matter because the suggested fix is two lines, one per charge-loop state. Both are now
measured rather than one measured and the other inferred from a shared constant.

## Two of ours, found by trying to run it

**The first silent run produced a complete, successful charging session.** Both sides logged a textbook
charge to `SessionStop`. Written up unexamined, that is *"their station does not time out"* — the exact
opposite of the truth.

`JosevInteropTests` reached **four** of the eleven parameters its EVerest twin passes to the shared EVCC
driver. `silentInChargeLoop` was one of the seven it dropped, so `V2G_INTEROP_SILENT=90` was read out of
the environment by `InteropEnvironment` and then went nowhere. **The seventh instance in a week of the
same shape**, and the first where the ignored value belonged to the caller rather than to our own state
machine: the previous six were a value our side already held that no call site could reach.

**A knob that is ignored is worse than a knob that is missing, because the run still produces a number.**
So the fix is not only the wiring. `InteropEnvironment` now records every variable it consults, and every
interop fixture ends by naming any `V2G_INTEROP_*` variable that was set for the run and read by nothing:

```
WARNING: set for this run and read by nothing — V2G_INTEROP_SILENT. Whatever they were meant to
change did not change, so do not write the run up as though they had.
```

A warning, not a failure: a leftover variable in a shell is ordinary, and a run that aborts on one would
be worse than the problem.

**Then the AC arm found the eighth, and the new guard does not catch it.** `-20 AC` was answered
`Failed_NoNegotiation`: three of the four fixtures called `SapHandshake.RunEvccSideAsync` without the
power mode, so the `-20` offer always named the **DC** namespace. `V2G_INTEROP_MODE` *was* read — by
`ProtocolAndMode`, for the session — and then dropped one hop short of the handshake.

That is worth stating plainly rather than hiding behind the fix: **the guard catches "asked for and
nobody looked", not "looked at and dropped on the way".** It would not have caught this one. Fixed in
`JosevInteropTests`, `EvDriveFlowInteropTests` and `TuxEvseInteropTests`; `EverestInteropTests` already
passed it, which is why every `-20 AC` run in this repository so far is an EVerest run.

## What this settles

**Settled:** their `-20` SECC waits 60 s after a charge-loop response, on both AC and DC, with their own
log naming the value. The filing's first checklist line is closed and its central claim is now measured
on the wire against upstream `d645255` rather than read out of it.

**Not settled, and unchanged:** the smaller half of that report — the `-20` states logging a timeout they
do not wait on — is still a source reading. It is cosmetic and the report says so.

**Not claimed:** that this is exotic. Two independent `-20` implementations flatten the same per-message
override, and ours is the third; `Secc20Base` takes one sequence timeout for every phase and is recorded
as ours to fix.

## Reproduce

```bash
docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine
docker run -d --rm --name josev-secc --network host \
    -e NETWORK_INTERFACE=eth0 -e SECC_ENFORCE_TLS=False -e PROTOCOLS=ISO_15118_20_DC \
    -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=INFO iso15118-secc:latest
bash tools/interop-everest/sdp-probe.sh eth0     # bash, not sh — and SDP is SDP, whoever answers it
```

Their SECC's own log names only the **UDP** discovery port (15118); the TCP port is chosen per session
and appears only in the SDP response, so it is asked for rather than read.

```bash
export V2G_INTEROP_SECC='[<their-addr>%eth0]:<port>' V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc
export V2G_INTEROP_SILENT=90        # omit for the control arm
dotnet test -c Release --artifacts-path ~/wsl-artifacts \
  --logger "console;verbosity=detailed" \
  --filter "FullyQualifiedName~JosevInteropTests.OurEvcc"
```

`PROTOCOLS=ISO_15118_20_AC` and `V2G_INTEROP_MODE=ac` for the AC pair.

## Artifacts

[`control-dc/`](control-dc/), [`silent-dc/`](silent-dc/), [`control-ac/`](control-ac/),
[`silent-ac/`](silent-ac/) — their SECC's log and our EVCC's output per arm.

Offline gate: **1 405 green**, four assemblies, exit code 0.

## Next

- **Nothing here.** [`josev-iso20-charge-loop-timeout.md`](../../reports/josev-iso20-charge-loop-timeout.md)
  has two boxes left, *decide issue or PR* and *post under your own name*, both a person's.
- Worth knowing for whoever runs the next one: **no `-20 AC` forward run against Josev, eVDriveFlow or
  tux-evse has ever negotiated**, so any AC cell in those columns that looks untested may simply never
  have been reachable. That is a question for the matrix, not a finding, and it is not answered here.
