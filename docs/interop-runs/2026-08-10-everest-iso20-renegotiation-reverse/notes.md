# 2026-08-10 — EVerest's EV renegotiates against our station, and then hangs up

The live confirmation [`josev-iso20-renegotiation.md`](../../reports/josev-iso20-renegotiation.md) asked
for, at the commit it cites — and on the power mode the July run could not reach. Our SECC signalled
`ServiceRenegotiation` once mid-charge (`[V2G20-1477]`), their `PyEvJosev` EV **carried the value
through to the wire**, our station answered `OK` and stayed open, and their EV **closed the connection**.

Both halves of that report come out of this one session:

| | |
|---|---|
| **§2 is fixed in the fork, and this is the proof on the wire** | Upstream's `DCWeldingDetection` hardcodes `ChargingSession.TERMINATE`, so a **DC** EV can never ask. The fork's uses the variable — and here the DC stop path produced a `SessionStopReq` our station did **not** treat as a termination |
| **§1 reproduces, in DC** | Our `SessionStopRes(OK)` left the session open, as `[V2G20-1477]` requires. Their EV sent nothing further and dropped the TCP connection |

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), `PyEvJosev` wrapping `EVerest/ext-switchev-iso15118` @ **`26f7988`** |
| Their module | `PyEvJosev` on `eth0`, their own `Evse15118D20` parked on `lo`; `supported_d20_energy_services: DC`, plain TCP |
| Ours | `WWCP_ISO15118` SECC as the listener, advertising over SDP, `V2G_INTEROP_RENEG=1` — a switch that did not exist this morning |
| Outcome | **21 exchanges, every response `OK`, renegotiation asked for and answered — and their EV hung up instead of re-entering `ServiceDiscovery`** |
| Artifacts | [`flow.md`](flow.md) · [`frames.log`](frames.log) · [`our-secc.log`](our-secc.log) · [`their-side.log`](their-side.log) · [`config-reneg-reverse-ours.yaml`](config-reneg-reverse-ours.yaml) |

## The session

```
 0 SupportedAppProtocolReq → OK_SuccessfulNegotiation      12 PowerDeliveryReq       → OK
 1 SessionSetupReq         → OK_NewSessionEstablished      13 DC_ChargeLoopReq       → OK   ← our ServiceRenegotiation
 2 AuthorizationSetupReq   → OK                            14 PowerDeliveryReq       → OK   ← their EV stops the charge
 3 AuthorizationReq        → OK                            15 DC_WeldingDetectionReq → OK
 4 ServiceDiscoveryReq     → OK                            …  ×5
 5 ServiceDetailReq        → OK                            20 SessionStopReq         → OK   ← and then silence
 6 ServiceSelectionReq     → OK
 7 DC_ChargeParameterDiscoveryReq → OK
 8 ScheduleExchangeReq     → OK
 9 DC_CableCheckReq        → OK
10 DC_PreChargeReq ×2      → OK
```

Their EV reacted to the notification correctly and completely: it stopped power delivery, ran welding
detection — the DC stop path — and sent `SessionStopReq`. Everything up to the last message is what
`[V2G20-1477]` asks of an EV.

## How we know the request said `ServiceRenegotiation`

**By what our own station did with it**, which is a decode rather than an inference: `Secc20Base`
answers `SessionStopReq(Terminate)` by ending the session, and `SessionStopReq(ServiceRenegotiation)` by
answering `OK` and re-entering `ServiceDiscovery`. It did the second — it went back to waiting for a
request:

```
System.IO.InvalidDataException : V2GTP frame: connection closed before a full 8-byte header arrived.
  ---> System.IO.EndOfStreamException
     at Secc20Base.ReadFrame20Async(…)
     at Secc20Base.RunAsync(…)
```

A `Terminate` would have ended `RunAsync` cleanly with `IsDone`. The exception *is* the evidence: our
station was still listening because the EV had asked to renegotiate, and the EV was already gone.

The frame corroborates it. Same 18-byte payload as a `Terminate` request from our own corpus, differing
in the trailing octet that carries the `ChargingSession` enum:

```
Terminate (our -20 DC vector)   01fe8002 00000012 8094 04 0505860687078808 80f2d6ca062 28
this run                        01fe8002 00000012 8094 04 33d61351c49ecfdd 082f6e8d3062 48
                                                                                        ↑↑
```

## What it adds to the filing

- The July observation was **AC** against upstream `d645255`. This is **DC** against the fork
  `26f7988`, so the defect is not power-mode-specific and not confined to the tree we read.
- It is the first time §2's fix has been seen **working**: a DC `SessionStopReq` that is not a
  `Terminate`. Upstream cannot produce that frame at all.
- And it makes the consequence concrete from the *station's* side rather than the EV's log: a
  conformant SECC that honours `[V2G20-1477]` is left holding an open session and a closed socket.

The report's checklist item *"Re-run it before sending"* is ticked by this run.

## One thing it says about us

The run is recorded as a **failed test**. `Secc20Base` throws when the peer hangs up, so the fixture
reports the most informative session of the day as a red line. `SeccOutcome.SequenceErrorAt` exists
because the same thing happened once before with a refusal — *"a run that can only report whether it
finished cannot report what finished"* — and a peer that disconnects mid-renegotiation is the same shape
one level further out. Not fixed here; named so the next reader of that red line knows it is the
counterparty's ending, not ours.

## How it was run

```bash
mosquitto -d -p 1883
# 1. fixture FIRST — their EV probes once, shortly after the manager boots
V2G_INTEROP_LISTEN=15118 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RENEG=1 V2G_INTEROP_RECORD=<dir> \
  dotnet test -c Release --artifacts-path ~/wsl-artifacts \
              --filter "…EverestInteropTests.TheirPyEvJosev"     # inside WSL: SDP is multicast
# 2. their side second
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-reneg-reverse-ours.yaml
```

`config-reneg-reverse-ours.yaml` is the MCS reverse config with `supported_d20_energy_services: DC` and
no `connector_type: cMCS`.

**The rig note that cost the first attempt:** the recipe says *fixture first, station second*, and the
first run honoured it in the script and not in practice — a cold Linux build of the solution took longer
than the readiness window, so the manager started while `dotnet test` was still compiling and their EV
probed into silence. Wait on the **listening socket**, not on a timer, and give it minutes rather than
seconds the first time.
