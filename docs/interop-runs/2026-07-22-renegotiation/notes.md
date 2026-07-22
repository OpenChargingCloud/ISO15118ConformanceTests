# Interop run — **Renegotiation** (-2 [V2G2-841] and -20 ServiceRenegotiation [V2G20-1477]), live vs Josev

- **Date:** 2026-07-22
- **Scope:** the last item of the feature-gap list — mid-session renegotiation, SECC-triggered and
  EV-initiated, in every direction Josev supports.

## -2, reverse — SECC-triggered ✅ (full cycle)

Our SECC (`--renegotiate`) puts `EVSENotification.ReNegotiation` into its first `ChargingStatusRes`;
the Josev EVCC reacts exactly per spec:

```
our SECC : -2 Renegotiation cycles: 1.   ✓ Session complete in 14273 ms.
Josev    : 2× ChargeParameterDiscoveryReq, 4× PowerDeliveryReq (Start/Renegotiate/Start/Stop), SessionStop
```

## -2, forward — EV-initiated ✅ (full cycle)

Our EVCC (`--renegotiate`) opens `PowerDeliveryReq(Renegotiate)` after the first charging-status cycle;
Josev's SECC handles it and the session completes:

```
our EVCC : renegotiations: 1 … ✓ Session complete in 2394 ms.
Josev    : 2× ChargeParameterDiscoveryReq received, 4× PowerDeliveryReq received
```

## -20, reverse — SECC-triggered ⚠️ (maximum reachable; the continuation is Josev's gap)

Our SECC (`--renegotiate`) puts `EvseNotification.ServiceRenegotiation` into the first ChargeLoopRes
`EVSEStatus`. The Josev **AC** EVCC reacts correctly — `PowerDelivery(Stop)` + a real
**`SessionStopReq(ChargingSession=ServiceRenegotiation)`** — and our SECC answers OK **without ending the
session**, re-entering ServiceDiscovery. Josev then closes the TCP link anyway: its EVCC posts the
session-terminating `StopNotification` *before* evaluating the renegotiation flags, so the
`next_state = ServiceDiscovery` it also sets never runs. Two more Josev gaps for the record:

1. **-20 DC**: its stop path detours through `DCWeldingDetection`, whose state builds the SessionStopReq
   with a hardcoded `Terminate` — the `SERVICE_RENEGOTIATION` in `charging_session_stop_v20` is dropped
   (first -20 attempt of this run showed a plain Terminate on the wire).
2. **-20 AC**: the `SessionStopReq(ServiceRenegotiation)` reaches the wire (verified live), but after our
   `SessionStopRes(OK)` the EVCC logs "The data link will terminate" and drops the connection instead of
   continuing at ServiceDiscovery.

Our own full -20 renegotiation cycle — notification → stop → `SessionStopReq(ServiceRenegotiation)` →
re-entry at ServiceDiscovery → complete second round → final Terminate — is covered by
`Secc20DynamicModeTests.ServiceRenegotiation_ReentersServiceDiscovery_AndCompletes` (which mirrors the
exact message train a live Josev EVCC produces, plus the continuation Josev cannot do).

## What was added on our side

- `Secc2`: `RequestRenegotiation` (one-shot `EVSENotification.ReNegotiation` in the charging-status
  response), the `PowerDeliveryReq(Renegotiate)` arm (→ ChargeParams; post-renegotiation CPD hands to
  PowerOn — no second CableCheck, as a real EV skips it), `Renegotiations` counter.
- `Evcc2`: reactive (notification) **and** proactive (`Renegotiate` option) renegotiation for AC and DC
  loops.
- `Secc20Base`: `RequestRenegotiation` → one-shot `ServiceRenegotiation` EVSEStatus in the DC/AC
  charge-loop res; `SessionStopReq(ServiceRenegotiation)` re-enters ServiceDiscovery instead of
  terminating; `ServiceRenegotiationSupported: true` advertised.
- CLI: `secc --renegotiate` / `evcc --renegotiate`.

CI: `Iso2LoopbackTests.DcSession_SeccTriggeredRenegotiation_RunsToCompletion`,
`Iso2LoopbackTests.AcSession_EvInitiatedRenegotiation_RunsToCompletion`,
`Secc20DynamicModeTests.ServiceRenegotiation_ReentersServiceDiscovery_AndCompletes`.
Scripts: [`reverse-renegotiate-sdp.sh`](../../../tools/interop-josev/reverse-renegotiate-sdp.sh) (`2|20`),
[`live-evcc-renegotiate.sh`](../../../tools/interop-josev/live-evcc-renegotiate.sh).
Logs: `{secc,evcc}-reneg-2.log`, `{secc,evcc}-reneg-20.log`, `{josev-secc,our-evcc}-reneg.log`.
