# Interop run — ISO 15118-20 DC, **live SDP discovery via `--sdp` (no responder shim)**: Josev EVCC → our SECC

- **Date:** 2026-07-22
- **Our side:** `Vanaheimr.V2G.Simulation.Cli secc … --sdp --interface eth0 …` — our own WWCP `SECC_SDPServer`,
  **not** the Python `sdp-responder.py` shim used by the earlier reverse runs.
- **Josev:** SwitchEV/iso15118 EVCC, docker host-mode, `-20 DC`, TLS 1.3, PnC.
- **Outcome:** ✅ Josev's EVCC SDP-discovers our SECC and the full PnC-over-TLS session completes to
  `SessionStop`:

  ```
  SDP: advertising [fe80::215:5dff:fe46:863f%2]:55000 (TLS) on eth0...
  # Josev: Sending SDPRequest: [Security: TLS, Protocol: TCP]
  # Josev: SDPResponse received: [ IP address: fe80::215:5dff:fe46:863f, Port: 55000, Security: TLS, Transport: TCP ]
  Plug & Charge: … signature OK (…, grammar=xmldsig-standalone).
  ✓ Session complete in 21513 ms.
  ```

## Root cause — it was never a multicast binding bug

The earlier runs used a Python SDP responder shim and a note called this a "WWCP EVCC/SECC SDP multicast
interface-binding bug". Reproduced cleanly (only our WWCP `SECC_SDPServer` bound to UDP 15118 — earlier tests
were **contaminated by stale `sdp-responder.py` processes still holding `*:15118`**, which answered the probes
and masked the real behaviour), the binding is fine:

- The WWCP `SECC_SDPServer` binds `[::]:15118`, joins `FF02::1` on the interface, and answers a `SDP_Request`
  with the correct endpoint and security byte (a plaintext SECC correctly advertises `0x10` = NoTLS).

The real defect was a **policy default**: `SECC_SDPServerOptions.RejectNoTlsRequests` defaults to `true`
(TLS-deployment/CRA-oriented). The CLI never overrode it, so a **plaintext** SECC (`--tls-backend none`,
offering NoTLS) *silently dropped* a plaintext EVCC's `SDP_Request` — the request that a plain-TCP Josev EVCC
sends. That looked like "no SDP response" → mistaken for a multicast/binding fault, and the shim (which always
answers) hid it. Clean isolated probe of a plaintext SECC, before/after the fix:

```
before:  [TLS-request 0x00] RESPONSE security=0x10   [NoTLS-request 0x10] TIMEOUT   (dropped)
after:   [TLS-request 0x00] RESPONSE security=0x10   [NoTLS-request 0x10] RESPONSE security=0x10
```

## Fix (in the CLI, not the submodule)

`Program.BuildSeccSdpOptions(iface, port, noTls)` now sets `RejectNoTlsRequests = !noTls` — reject no-TLS
downgrade requests only when we ourselves are a TLS SECC; a plaintext SECC answers plaintext requests.
Guarded by `Vanaheimr.V2G.Simulation.Tests/Discovery/SeccSdpOptionsTests`. Also fixed a cosmetic
`…%2%2` scope doubling in the advertise log (re-derive the scoped display address once).

The Python `tools/interop-josev/sdp-responder.py` stays as a generic pentest/fallback helper, but `--sdp` no
longer needs it.

## Reproduce

`tools/interop-josev/reverse-pnc-tls-sdp.sh` — same as `reverse-pnc-tls.sh` but our SECC uses
`--sdp --interface eth0` and there is **no** Python responder. (Kill any stale `sdp-responder.py` first:
`pkill -f sdp-responder`.)
