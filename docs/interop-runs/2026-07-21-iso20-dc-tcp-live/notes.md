# Interop run — ISO 15118-20 DC, **live over-the-wire**, our EVCC ↔ Josev SECC (plain TCP)

- **Date:** 2026-07-21
- **Josev:** SwitchEV/iso15118 @ `d645255`, host-mode container on WSL2 `eth0`, `SECC_ENFORCE_TLS=False`
  (plain TCP, no TLS), `-20 DC`, PROTOCOLS include `ISO_15118_20_DC`
- **Our side:** `Vanaheimr.V2G.Simulation.Cli` (`evcc`), built on Windows, run under WSL's .NET 10 so both
  ends share one network stack; real IPv6 link-local sockets
- **Outcome:** ✅ a **live session over a real socket** — SDP discovery → TCP → SAP → SessionSetup →
  AuthorizationSetup → Authorization (EIM) → ServiceDiscovery → ServiceDetail → ServiceSelection. This is
  the first end-to-end run of our stack against an independent one over the wire (record mode validated the
  codec; this validates SDP + V2GTP framing + the session state machine + timing, live). It surfaced **three
  genuine conformance bugs record mode could not** (all now fixed), then stopped at a documented
  simulator-fidelity gap. Full SECC-side log: [`secc-session.log`](secc-session.log).

## Why this was reachable now

Josev is IPv6-only with an SDP-discovered, per-request **dynamic** TCP port — not same-host testable from
Windows. But WSL2 ships .NET 10, so our CLI runs *inside* WSL alongside a **host-mode** Josev SECC: one
network namespace, real `fe80::…%eth0` sockets, working IPv6 multicast. SDP is driven by a tiny helper that
multicasts the `SDP_Request` and hands the discovered `[addr%eth0]:port` to `evcc --connect` (our WWCP SDP
*client* has a separate multicast interface-binding bug — the raw request proved the fabric works).

## Conformance bugs found and fixed (live-only — record mode can't see these)

Record mode compares the EXI **payload**; live interop is the first thing to exercise the V2GTP **header**,
the SDP exchange, and cross-stack **session-state** rules. Three real bugs, each masked in our loopback
tests because both our EVCC and SECC were lenient/consistent in the same wrong way:

1. **V2GTP SAP payload type `0x8000` → `0x8001`.** Josev rejected our SAP frame: *"UNKNOWN does not support
   payload type 32768"*. The SupportedAppProtocol handshake uses payload id **`0x8001`** — the same id the
   DIN/-2 messages use (ISO 15118-20 §A / libcbv2g `V2GTP20_SAP_PAYLOAD_ID` / Josev) — distinguished by
   session phase, not payload type. Our `PayloadType_AppProtocol` was a bogus distinct `0x8000`. Fixed: SAP
   now frames/decodes `0x8001` explicitly (the payload-type dispatcher handles only post-SAP messages).
2. **SAP `-20` ProtocolNamespace `…:CommonMessages` → `…:DC`.** Josev then replied `Failed_NoNegotiation`.
   The -20 SAP offer must carry the mode-specific namespace (`…-20:DC` / `…-20:AC`), not `…-20:CommonMessages`
   (Josev's own -20 DC EVCC offers `…-20:DC`). Fixed: `SapHandshake` is now mode-aware.
3. **EVCC didn't adopt the SECC-assigned SessionID.** Josev then rejected `AuthorizationSetupReq`:
   *"session ID 0000000000000000 does not match 696B0AA4510828D6"*. Per ISO 15118-20 §7.9.2.4 every request
   after `SessionSetupRes` must carry the SECC-assigned SessionID; our EVCC kept sending the all-zero opener.
   Our loopback SECC never checked. Fixed: the -20 EVCC adopts `SessionSetupRes.Header.SessionID`.

With all three fixed, the live session runs cleanly through the whole handshake + setup + auth + discovery.

## Update — dynamic service negotiation (two more EVCC fixes)

Two follow-on EVCC fixes push the live session much deeper — through the whole DC energy-transfer setup:

4. **Dynamic service negotiation.** The EVCC hardcoded energy-transfer `ServiceID=1`/`ParameterSetID=1`
   (Josev's DC catalog offers neither → `FAILED_ServiceIDInvalid` / *"not offered by SECC"*). It now parses
   `ServiceDiscoveryRes.EnergyTransferServiceList` and picks the service matching its mode (DC → id 2/6),
   then parses `ServiceDetailRes` and picks a Scheduled-control-mode parameter set. Against Josev it now
   selects `ServiceID=2` and Josev replies `ServiceSelectionRes: OK`. (Our loopback SECC advertises exactly
   the old fixed ids, which masked this.)
5. **`MaximumSupportingPoints` out of range.** Our `ScheduleExchangeReq` sent `1`, below the schema minimum
   of `12` (range [12, 1024]; the wire value biases by 12, so 1 underflows to 1025). Josev rejects it; our
   SECC didn't validate the range. Fixed to `12`.

With those, the live session now runs cleanly through **ServiceSelection → DC_ChargeParameterDiscovery →
ScheduleExchange → DC_CableCheck → DC_PreCharge → PowerDelivery** — essentially the entire DC setup.

## Where it stops now (open — EVCC session-content fidelity, not a codec/framing bug)

At **PowerDelivery**: `PowerDeliveryReq` with `ChargeProgress=Start` requires a populated `EVPowerProfile`
(a nested power-schedule structure derived from the chosen schedule); our EVCC sends it as absent, which
Josev rejects. Completing a full live DC charge loop needs the EVCC to build a spec-valid `EVPowerProfile`
(and, likely, further per-message value fidelity through ChargeLoop → WeldingDetection → SessionStop). This
is a continuing EVCC state-machine enhancement — the -20 DC **message codecs** for all of these are already
byte-exact vs both cbV2G and Josev (record mode), so it is purely EVCC-side session *content*, not the wire.

## Reproduce

1. `docker compose -f docker-compose-host-mode.yml -f docker-compose.livetest.yml up secc redis`
   (host mode, `.env.dev.docker` with `SECC_ENFORCE_TLS=False`, `REDIS_HOST=localhost`).
2. In WSL: run `tools/interop-josev/live-evcc-tcp.sh` — it SDP-discovers the SECC and immediately runs
   `dotnet …Cli.dll evcc --connect "[<addr>%eth0]:<port>" --protocol 20 --mode dc`.

## Next

- EVCC `EVPowerProfile` construction (and any further per-message value fidelity) to drive a full live DC
  charge loop through ChargeLoop → WeldingDetection → SessionStop.
- Fix the WWCP `EVCC_SDPClient` multicast interface binding so `evcc --sdp --interface eth0` works directly.
- Milestone B: the same over **TLS 1.3** via the BouncyCastle backend (Josev with `SECC_ENFORCE_TLS=True`).
