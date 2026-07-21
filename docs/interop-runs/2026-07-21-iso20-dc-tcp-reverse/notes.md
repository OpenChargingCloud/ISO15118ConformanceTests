# Interop run — ISO 15118-20 DC, **live over-the-wire, reverse direction**: Josev EVCC → our SECC (plain TCP)

- **Date:** 2026-07-21
- **Our side:** `Vanaheimr.V2G.Simulation.Cli` (`secc --listen 55000 --protocol 20 --mode dc`), run under WSL's
  .NET 10, listening on `[::]:55000` (IPv6 dual-stack).
- **Josev:** SwitchEV/iso15118 @ `d645255`, EVCC only, host-mode container on WSL2 `eth0`, `-20 DC`, no TLS.
- **SDP:** a minimal SECC-side SDP responder (`scratchpad`/`tools`) advertises our SECC's `fe80::…%eth0:55000`
  to Josev's EVCC — the WWCP `EVCC/SECC` SDP components have a separate multicast interface-binding bug
  (tracked), so the responder isolates that from the session interop, exactly as the forward run did.
- **Outcome:** ✅ the reverse direction **works** through SDP → SAP → SessionSetup → AuthorizationSetup →
  Authorization → Service{Discovery,Detail,Selection} → DC_ChargeParameterDiscovery → ScheduleExchange →
  DC_CableCheck → DC_PreCharge (first poll). It surfaced **three more real SECC bugs** (all fixed) that only
  a strictly-validating EVCC catches, then reached a genuine SECC state-machine gap (the DC precharge poll
  loop). Full EVCC-side log: [`evcc-session.log`](evcc-session.log).

## Networking

Our SECC bound `IPAddress.Any` (IPv4) — Josev connects over IPv6 link-local, so nothing arrived. Fixed:
`TcpV2GListener` now binds `[::]` as **dual-stack** (accepts IPv4 loopback tests and IPv6 EVCCs alike), and
the CLI SECC binds `IPv6Any`.

## SECC conformance bugs found and fixed (mirror of the forward run, now on our SECC's content)

Josev's EVCC strictly validates every SECC response; our SECC's placeholder content was too thin. Each was
masked in our loopback E2E because our own EVCC was equally lenient/minimal.

1. **ServiceDiscovery advertised the wrong ServiceID.** Our SECC offered energy-transfer `ServiceID=1` (AC)
   for a DC session → Josev's EVCC aborted with *"WrongServiceID"*. Our SECC is now mode-aware (DC→2, AC→1),
   mirroring the EVCC's dynamic selection.
2. **ServiceDetail lacked a `ControlMode` parameter.** Josev's EVCC aborted with *"Control mode parameter
   missing"*. Our SECC's parameter set now includes Connector / ControlMode(=Scheduled) / MobilityNeedsMode /
   Pricing.
3. **ScheduleExchange's ChargingSchedule had no price schedule.** A `ChargingSchedule` must carry a
   `PriceLevelSchedule` or `AbsolutePriceSchedule` (Josev rejected the tuple otherwise). Our SECC now includes
   a compact `PriceLevelSchedule`.

With those, the reverse session runs through the whole setup and into the DC precharge sequence.

## Where it stops (open — SECC DC poll-loop sequencing)

At **DC_PreCharge**: a real EVCC (Josev) *polls* `DC_CableCheckReq`/`DC_PreChargeReq` (`EVProcessing=Ongoing`)
repeatedly until precharge completes, then proceeds to `PowerDelivery`. Our SECC's DC state machine advances
after a **single** CableCheck and a single PreCharge (it handled Josev's first PreCharge fine, then rejected
the second: *"DC_PreChargeReq not allowed in phase PowerOn"*). Making the SECC self-loop the CableCheck /
PreCharge / WeldingDetection phases (until the next-phase message) is a genuine state-machine enhancement —
the base `Secc20Base` can't distinguish the DC message types today (they live in a separate, colliding
namespace, so the phases match on a wildcard and advance once). This, plus any remaining per-message content
fidelity through PowerDelivery → ChargeLoop → WeldingDetection → SessionStop, is the continuing work to a
full reverse charge loop. (Our loopback SECC works because our own EVCC sends exactly one CableCheck/PreCharge.)

Note also: our SECC's sequence guard rejects a `SessionStopReq` mid-session (it errors instead of answering),
so when Josev aborts early our SECC logs a `FAILED_SequenceError` rather than a graceful stop — a small
robustness follow-up (a SECC should accept `SessionStop` in any phase).

## Reproduce

1. Build the CLI (`dotnet build -c Release`).
2. Start our SECC: `dotnet …Cli.dll secc --listen 55000 --protocol 20 --mode dc` (WSL).
3. Start the SDP responder advertising `[<eth0-link-local>%eth0]:55000` (see the forward run's tooling).
4. Run Josev's EVCC (host mode, -20 DC, no TLS, no Josev SECC): the reverse `docker-compose` override in the
   Josev clone brings up just `evcc` + `redis`.

## Next

- SECC DC poll-loop self-looping (CableCheck/PreCharge/WeldingDetection) → a full reverse DC charge loop.
- Accept `SessionStop` in any phase (graceful abort handling).
- Then the same over **TLS 1.3** (Josev `SECC_ENFORCE_TLS=True`, our BouncyCastle backend).
