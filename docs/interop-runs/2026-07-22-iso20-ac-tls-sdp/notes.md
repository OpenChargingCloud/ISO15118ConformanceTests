# Interop run — ISO 15118-20 **AC over mutual TLS 1.3**, live via `--sdp`: Josev EVCC → our SECC

- **Date:** 2026-07-22
- **Our side:** `Vanaheimr.V2G.Simulation.Cli secc --listen 55000 --protocol 20 --mode ac --sdp --interface eth0
  --tls-backend dotnet --server-cert secc.p12 --server-cert-pass 12345 --require-client-cert` (.NET SslStream,
  P-256 PKI) — our `Secc20Ac` state machine, WWCP `SECC_SDPServer`, mutual TLS.
- **Josev:** SwitchEV/iso15118 EVCC, docker host-mode, `-20 AC`, `ENABLE_TLS_1_3=True`, `useTls=true`.
- **Outcome:** ✅ full **-20 AC over TLS** session to completion — `✓ Session complete in 14042 ms`,
  Josev EVCC exited 0, clean `SessionStopReq`/`SessionStopRes(OK)`.

## Flow (all live, over mutual TLS 1.3)

```
SDP  : Josev SDPRequest [Security: TLS] → our SECC SDPResponse [fe80::…%eth0:55000, TLS]   (no shim)
TLS  : our SECC presents CN=SECCCert (+2 intermediates), requires client cert (mutual TLS 1.3)
Auth : PnC — challenge OK, digest OK, signature OK (grammar=xmldsig-standalone)
AC   : ACChargeParameterDiscovery → PowerDelivery(Start) → ACChargeLoop (×N)
       → PowerDelivery(Stop) → SessionStopReq[Terminate] → SessionStopRes(OK)
```

Completes the AC interop matrix: our `Secc20Ac` now interops with a real Josev EVCC over **plain TCP + EIM/PnC**
([`2026-07-22-iso20-ac-eim-sdp`](../2026-07-22-iso20-ac-eim-sdp/)) **and mutual TLS 1.3 + PnC** (this run) — the
same coverage the -20 DC runs have. No content bugs surfaced in either AC run.

Logs: [`our-secc-ac-tls.log`](our-secc-ac-tls.log), [`josev-evcc-ac-tls-session.log`](josev-evcc-ac-tls-session.log).
Reproduce: `tools/interop-josev/reverse-ac-tls-sdp.sh` (builds an AC `useTls=true` config, reuses `secc.p12`).
