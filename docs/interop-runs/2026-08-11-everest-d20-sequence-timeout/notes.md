# 2026-08-11 — 60 s of silence in a charge loop that allows 0,5 s

The third filing of the day, and the second found by reading before measuring. The
[sequence-error probe](../2026-08-11-everest-iso2-sequence-error/notes.md) had listed the timeout half of
`[V2G2-537]`/`[V2G2-538]` as *"we measured **that** an answer comes, not how fast"*. This is the `-20`
version of that question, and the answer is a number.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), `Evse15118D20` / `libiso15118`, `config-d20-ours.yaml`, plain TCP, DC |
| Ours | `Evcc20Base.GoSilentInChargeLoop` — new, and the reason this was measurable at all |
| Outcome | **`V2G_SECC_Sequence_Timeout` is one 60 s constant for every message type**, where Tables 216/217 give the SECC 0,5 s in the charge loop. Filed: [`everest-d20-sequence-timeout.md`](../../reports/everest-d20-sequence-timeout.md) |
| Artifacts | [`their-charger.control.log`](their-charger.control.log) · [`their-charger.silent.log`](their-charger.silent.log) · [`ours.control.log`](ours.control.log) · [`ours.silent.log`](ours.silent.log) · [`silence-run.sh`](silence-run.sh) |

## The source, read first

```cpp
// include/iso15118/d20/timeout.hpp:30
constexpr auto TIMEOUT_SEQUENCE = 1000 * 60;
```

`grep` finds `start_timeout(…, TimeoutType::SEQUENCE, …)` **exactly once** in the whole library, in
`Session::send_response()`, always with that constant. The pairing around it is right: the next request
stops the timer, and `SupportedAppProtocolReq` is correctly exempted because no sequence timer runs
before it. Only the duration is fixed.

The standard's is not. Table 215 gives 60 s for *all other messages*; **Table 216** (`[V2G20-1500]`) and
**Table 217** (`[V2G20-1502]`) override it after `AC_ChargeLoopRes` and `DC_ChargeLoopRes` with
**0,5 s** — the phase in which the contactor is closed.

## What it took to measure, which is the second half

Nothing in this suite could make a car **go quiet without hanging up**. A car that closes the socket is
an EOF and says nothing about a timer; `V2G_SECC_Sequence_Timeout` is defined against silence on an open
connection. So `Evcc20Base.GoSilentInChargeLoop` was added: one charge-loop iteration, then no bytes at
all, the socket held open, and a clock on how long the station leaves the session standing.
`V2G_INTEROP_SILENT=<seconds>` reaches it from a run.

**Third time this month, and the third different question.** MeterInfo (2026-08-10) was a field our car
never set; the SessionID override (2026-08-11) was a header our car could not forge; this is a message
our car could not *withhold*. Each time the missing capability was ours and the unmeasured behaviour was
somebody else's.

## Two arms

| arm | our EV | outcome |
|---|---|---|
| **control** | charges normally | `Authorization: eim`, service 2 (DC), full session, `SessionStopReq`/`Res`, 24 s |
| **silent** | one loop iteration, then nothing, connection open | station ended the session **60,00 s** later |

Same negotiation in both, so the silent arm is a car that reached the charge loop and then stopped —
not one that never got there. That is what the control is for.

**Their own log carries the interval**, which is better than ours carrying it:

```
03:06:05.849686 [INFO] evse_manager:Ev :: EVSE ISO V2G DcChargeLoopRes
03:07:05.852171 [ERRO] iso15118_charge :: Sequence Timeout 40secs is reached. Stopping the session
```

**60,0025 s**, against 0,5 s allowed. 120×.

**Two numbers, and they measure different things.** Our EV recorded **65,04 s** from the moment it
stopped sending until the socket closed. Their timer fired at 60,00 s; the remaining ≈5 s is their
teardown between the verdict and the TCP close. Worth stating both rather than picking the tidier one.

## The log line, which names a third number

```
logf_error("Sequence Timeout 40secs is reached. …");
```

40 s is `V2G_EVCC_Sequence_Performance_Time` — Table 215, the **EV's** row. The constant is 60 and the
charge-loop allowance is 0,5, so the message names neither. It is cosmetic and it is in the filing as
cosmetic; what makes it worth a sentence is that it shows the table *was* read.

## Ours is the same shape, and that is in the report

`Secc20Base` takes a single `sequenceTimeout` in its constructor and applies it in every phase — the
same flat design, never measured by anyone. Recorded in [`open-work.md`](../../open-work.md); it does
not soften the filing, because the filing rests on a value on the wire rather than on a design opinion,
but leaving it out would have been the kind of asymmetry that makes a report easy to dismiss.

## Not tested here

- **The AC path.** The constant is shared, so the DC measurement settles both by construction — but
  by construction is not by measurement, and the same rig runs it with `V2G_INTEROP_MODE=ac`.
- **`V2G_SECC_Ongoing_Timeout` and the performance times.** Different timers, different tables, untouched.
- **Whether upstream `main` still has the constant.** 2026.02.1 was current on 2026-08-11; the local
  checkout is a shallow single-tag clone, so `git log HEAD..origin/main` cannot answer it — an explicit
  fetch is required, as the previous filing's checklist now says.

## Reproduce

```bash
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-d20-ours.yaml &
CP_AT_PLUGIN=1 bash ~/everest/sil-car.sh &
bash ~/everest/sdp-probe.sh eth0          # Evse15118D20 opens its TCP server only on SDP

V2G_INTEROP_SECC='[<their-addr>%eth0>]:<port>' V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc \
V2G_INTEROP_SILENT=90 \
  dotnet test -c Release ISO15118ConformanceTests.Simulation \
    --logger "console;verbosity=detailed" \
    --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

`--logger console;verbosity=detailed` is not optional: the measurement is printed through
`TestContext.Out` and is invisible at the default verbosity. The first run of this cost an extra pass.
`sdp-probe.sh` takes an **interface name**; passing anything else sends the request to `ff02::1` on a
non-existent scope and the endpoint parsed out of the failure is the multicast address, which then looks
like a station that accepts a connection and never answers. That cost a pass too.
