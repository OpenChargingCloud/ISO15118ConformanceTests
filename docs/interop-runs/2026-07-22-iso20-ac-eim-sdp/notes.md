# Interop run — ISO 15118-20 **AC**, live over plain TCP + `--sdp`: Josev EVCC → our SECC

- **Date:** 2026-07-22
- **Our side:** `Vanaheimr.V2G.Simulation.Cli secc --listen 55000 --protocol 20 --mode ac --sdp --interface eth0`
  (plain TCP, NoTLS), .NET 10 under WSL — our `Secc20Ac` state machine + the WWCP `SECC_SDPServer`.
- **Josev:** SwitchEV/iso15118 EVCC, docker host-mode, `evcc_config_ac.json` (`ISO_15118_20_AC`, `useTls=false`).
- **Outcome:** ✅ full **-20 AC** session to completion — `✓ Session complete in 13988 ms`, Josev EVCC exited 0.

## Flow (all live, over the wire)

```
SDP  : Josev SDPRequest [Security: NO_TLS] → our SECC SDPResponse [fe80::…%eth0:55000, NO_TLS]   (no shim)
SAP  → SessionSetup → AuthorizationSetup (we offer PnC+EIM)
Auth : Josev selects PnC and signs; our SECC → challenge OK, digest OK, signature OK (grammar=xmldsig-standalone)
Disc : ServiceDiscovery → ServiceID 1 (AC) → ServiceSelection
AC   : ACChargeParameterDiscovery → PowerDelivery(Start) → ACChargeLoop (×N) → SessionStop
```

This validates three things at once:

1. **Our `-20 AC` state machine live** against an independent stack (Josev) — `ACChargeParameterDiscovery` +
   `ACChargeLoop` exchange correctly, no content bugs, session completes.
2. **Plaintext `--sdp` discovery end-to-end without the shim** — Josev's EVCC (useTls=false) requests NoTLS and
   our SECC answers (the fix from `docs/interop-runs/2026-07-22-iso20-dc-sdp-noshim/`; here confirmed live in
   the AC path).
3. **PnC signature verify over the standalone-xmldsig grammar** — again `signature OK … grammar=xmldsig-standalone`,
   this time over plaintext TCP (Josev does PnC even without TLS).

Logs: [`our-secc-ac.log`](our-secc-ac.log), [`josev-evcc-ac-session.log`](josev-evcc-ac-session.log).
Reproduce: `tools/interop-josev/reverse-ac-sdp.sh`.

## WPT / ACDP — not runnable against Josev

Live WPT and ACDP runs are **not possible** with Josev as the oracle: Josev implements only AC and DC session
state machines (`iso15118_20_states.py` has AC/DC states only; the only EVCC configs are `evcc_config_{ac,dc}[_bpt].json`).
It defines the WPT/ACDP *namespaces and payload types* but no session logic. Our own sim is symmetric — the
`Vanaheimr.V2G.Exi.Iso15118_20.WPT` / `.ACDP` projects are **codec-only** (byte-exact vs cbV2G, cross-checked in
CI), with no `Evcc20Wpt`/`Secc20Wpt` state machines. A live WPT/ACDP run would require implementing full session
state machines on **both** sides; there is no third-party -20 WPT/ACDP implementation available to interop against.
WPT/ACDP therefore remain **codec-validated (record mode) only** — see `Vanaheimr.V2G.Exi.Tests` WPT/ACDP vectors.
