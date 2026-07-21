# Interop run — ISO 15118-20 DC, **live over-the-wire TLS, reverse direction**: Josev EVCC → our SECC

- **Date:** 2026-07-21
- **Our side:** `Vanaheimr.V2G.Simulation.Cli` (`secc --listen 55000 --protocol 20 --mode dc --tls-backend dotnet
  --server-cert secc.p12 --server-cert-pass 12345 --require-client-cert`), run under WSL's .NET 10, the **.NET
  `SslStream`** backend, listening `[::]:55000` (IPv6 dual-stack).
- **Josev:** SwitchEV/iso15118 @ `d645255`, EVCC only, host-mode container on WSL2 `eth0`, `-20 DC`,
  `ENABLE_TLS_1_3=True` and the EVCC config's `useTls=true` (so it SDP-requests TLS and presents its OEM client
  cert).
- **Certificates (P-256, Josev's PKI):** our SECC presents Josev's **CPO server chain** (SECC leaf + CPO
  Sub-CAs, built into `secc.p12`, pw `12345`) so Josev's EVCC validates it against the V2G root; our SECC
  requires + (dev) accepts Josev's OEM client cert. Both transmit their intermediate chains over the wire (see
  the forward run's client-chain fix — the server side got the same `SslStreamCertificateContext` treatment).
- **SDP:** our SECC-side responder advertises `[fe80::…%eth0]:55000` with **security = TLS**; the WWCP SDP
  components' multicast interface-binding bug stays isolated, as in the plain reverse run.
- **Outcome:** ✅ a **complete** -20 DC charge session over **mutual TLS 1.3**, end to end to SessionStop.
  Josev's EVCC exited **code 0**; our SECC logged **"✓ Session complete in 36974 ms."** Poll counts (all
  answered in place by our SECC's self-looping phases):

  | Josev EVCC sent          | count |
  |--------------------------|-------|
  | `DC_CableCheckReq`       | 1     |
  | `DC_PreChargeReq`        | 4     |
  | **`PowerDeliveryReq(Start)`** | **2** |
  | `DC_ChargeLoopReq`       | 10    |
  | `DC_WeldingDetectionReq` | 5     |
  | `SessionStopReq`         | 1     |

  Full EVCC log: [`josev-evcc-tls13-mutual-session.log`](josev-evcc-tls13-mutual-session.log).

## The real bug this run caught — `PowerDelivery(Start)` is a poll phase

The first attempt crashed our SECC: *"Unable to cast … `PowerDeliveryReq` to … `DC_ChargeLoopReq`"*. A real EV
(Josev) **repeats `PowerDeliveryReq(ChargeProgress=Start)` with `EVProcessing=Ongoing`** until it begins the
charge loop, but our SECC advanced to `Charging` after the *first* one and then mis-cast the second
`PowerDeliveryReq` as a charge-loop request. Our own loopback EVCC sends `PowerDelivery(Start)` exactly once
(`EVProcessing=Finished`), which masked it — the same class of gap the DC poll phases had.

**Fix:** `PowerOn` is now a self-looping poll phase alongside CableCheck/PreCharge/WeldingDetection — the SECC
answers each `PowerDeliveryReq(Start)` and stays put, and the pre-switch loop advances to `Charging` (without
consuming) once the first charge-loop message arrives. `IsPollFor(PowerOn, PowerDeliveryReq{Start})` lives in
the base (it's a nameable CommonMessages request); `Secc20Dc` falls back to the base for it. Covered by the
extended `Secc20DcTransitionTests` (three `PowerDeliveryReq(Start)` polls). Loopback E2E unchanged.

## Networking / CLI additions (shared with the forward TLS run)

- `TlsOptions.ServerCertificateChain` + server-side `SslStreamCertificateContext` in `TcpV2GListener` — so our
  SECC transmits its CPO intermediates (a root-only EVCC needs them), mirroring the client-side fix.
- CLI SECC: `--server-cert <pfx> [--server-cert-pass <pw>] [--require-client-cert]` (.NET backend). The
  self-signed dev cert is used only when no `--server-cert` is supplied.
- The SDP responder (`tools/interop-josev/sdp-responder.py`) now takes an optional `tls|notls` arg.

## Reproduce

1. Build the CLI (`dotnet build -c Release`).
2. Build `secc.p12` from Josev's SECC leaf + key + CPO Sub-CAs (pw 12345, venv `iso15118_2/certs`).
3. Start our SECC (`secc … --tls-backend dotnet --server-cert secc.p12 --server-cert-pass 12345
   --require-client-cert`) + the SDP responder advertising **TLS**.
4. Run Josev's EVCC host-mode with `ENABLE_TLS_1_3=True` and a config with `useTls=true`.

## Remaining

- Live -20 **Plug & Charge** over TLS (contract-cert auth) rather than EIM.
- The strict secp521r1 TLS profile stays a loopback-only proof — Josev is P-256 (see the forward run's notes).
