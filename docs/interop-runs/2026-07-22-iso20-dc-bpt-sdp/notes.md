# Interop run — ISO 15118-20 **DC bidirectional (DC_BPT)**, live via `--sdp`: Josev EVCC → our SECC

- **Date:** 2026-07-22
- **Our side:** `Vanaheimr.V2G.Simulation.Cli secc --listen 55000 --protocol 20 --mode dc --sdp --interface eth0`
  (plain TCP), now with **bidirectional support** in `Secc20Dc`.
- **Josev:** SwitchEV/iso15118 EVCC, docker host-mode, `evcc_config_dc_bpt.json` (`supportedEnergyServices: ["DC_BPT"]`).
- **Outcome:** ✅ full **DC_BPT** session to `SessionStop` — `✓ Session complete in 21542 ms`, EVCC exited 0.

## What it took

A first probe against the charge-only SECC failed immediately: Josev's DC_BPT EVCC authenticated (PnC verified),
received our `ServiceDiscoveryRes` offering only `ServiceID: 2` (unidirectional DC), and aborted with
`SessionStop, Reason: WrongServiceID` — it wanted **DC_BPT (service id 6)**. So our SECC needed bidirectional
support (not just a config change):

- **Advertise both services.** `Secc20Base` now advertises a *list* of energy-transfer service ids; `Secc20Dc`
  offers `{2 (DC), 6 (DC_BPT)}`, `Secc20Ac` offers `{1 (AC), 5 (AC_BPT)}`. An EV picks the one it wants.
- **Respond in kind, per message.** The -20 CPD/charge-loop energy-transfer-mode and control-mode are
  *polymorphic* (the `BPT_*` types derive from the unidirectional ones; the codec already dispatches them via a
  2-bit discriminator). So `HandleChargeParameterDiscovery` / `HandleChargeLoop` detect a `BPT_*` request and
  reply with the matching `BPT_*` response (charge **and** discharge limits) — no CLI flag; the direction is
  driven by what the EV sends.

## Flow (all live)

```
SDP(NoTLS, no shim) → SAP → SessionSetup → AuthorizationSetup → Authorization (PnC verified)
ServiceDiscovery: Service:[{ID:2},{ID:6}] → EV selects ServiceID 6 (DC_BPT) → ServiceDetail → ServiceSelection
ScheduleExchange → DC_CableCheck → DC_PreCharge → PowerDelivery(Start)
→ DC_ChargeLoop (BPT_Scheduled control mode, discharge params exchanged) → PowerDelivery(Stop)
→ DC_WeldingDetection → SessionStop(Terminate)
```

The AC bidirectional run is the mirror image: [`2026-07-22-iso20-ac-bpt-sdp`](../2026-07-22-iso20-ac-bpt-sdp/)
(Josev `evcc_config_ac_bpt.json`, selects **ServiceID 5 (AC_BPT)**, full AC charge loop with discharge, to
`SessionStop`). Both are backward-compatible: the unidirectional DC/AC runs still pass (the EV just selects
service 2/1 and sends the non-BPT modes).

CI coverage: `Secc20DcTransitionTests.DcBptSession_OffersBothServices_AndAnswersBptCpdAndChargeLoop`,
`Secc20AcBptTests.AcBptSession_OffersBothServices_AndAnswersBptCpd`.

Logs: [`our-secc-dc-bpt.log`](our-secc-dc-bpt.log), [`josev-evcc-dc-bpt-session.log`](josev-evcc-dc-bpt-session.log).
Reproduce: `tools/interop-josev/reverse-bpt-sdp.sh`.
