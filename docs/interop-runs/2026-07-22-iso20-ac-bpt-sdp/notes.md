# Interop run — ISO 15118-20 **AC bidirectional (AC_BPT)**, live via `--sdp`: Josev EVCC → our SECC

- **Date:** 2026-07-22
- **Our side:** `secc --protocol 20 --mode ac --sdp --interface eth0` (plain TCP), bidirectional `Secc20Ac`.
- **Josev:** EVCC, docker host-mode, `evcc_config_ac_bpt.json` (`supportedEnergyServices: ["AC_BPT"]`).
- **Outcome:** ✅ full **AC_BPT** session to `SessionStop` — `✓ Session complete in 18620 ms`, EVCC exited 0.

The mirror of the DC_BPT run — see [`2026-07-22-iso20-dc-bpt-sdp`](../2026-07-22-iso20-dc-bpt-sdp/) for the
full write-up of the bidirectional support that was added. Here Josev's AC_BPT EVCC finds our advertised
`Service:[{ID:1},{ID:5}]`, selects **ServiceID 5 (AC_BPT)**, and runs
`ServiceSelection → ScheduleExchange → ACChargeParameterDiscovery (BPT, discharge power) → PowerDelivery(Start)
→ ACChargeLoop (BPT_Scheduled control mode) → SessionStop`.

CI coverage: `Secc20AcBptTests.AcBptSession_OffersBothServices_AndAnswersBptCpd`.
Logs: [`our-secc-ac-bpt.log`](our-secc-ac-bpt.log), [`josev-evcc-ac-bpt-session.log`](josev-evcc-ac-bpt-session.log).
