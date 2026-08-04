# Task: EV↔EVSE simulation — SDP, TCP/TLS, state machines, interop (Phase 5)

## Context

You're working in the repo `D:\Coding\OpenChargingCloud\Vanaheimr.V2G.Exi` — a .NET 10 library
for ISO 15118 EXI. State after Phase 0–4:

- EXI primitives complete (value tables, signed/binary/boolean).
- Generated codecs, byte-validated against cbV2G: AppProtocol (SAP),
  `WWCP_ISO15118_2` (all 17 message pairs),
  `WWCP_ISO15118_20.{CommonMessages,DC,AC}`.
- XMLDSig over EXI fragments (-2: P-256/SHA-256; -20: secp521r1/SHA-512).
- V2GTP layer with a payload-type dispatcher (SAP / -2 / -20 sets).
- `tools/cbv2g-ref/` (libcbv2g harness, pinned), a vector-driven NUnit suite.

Read before starting: `README.md`, `docs/`, the V2GTP dispatcher, and the public
APIs of the generated codec assemblies.

## Preconditions (check these first)

Phases 0–4 complete (in particular: -2 and -20 codecs vector-validated,
dispatcher in place). If anything is missing: stop and report.

## Goal

A simulated charging session between an EVCC (EV side) and an SECC
(EVSE side) runs to completion — first our two sides against each other,
then in interop against an independent stack (Josev). This proves that
codec, V2GTP, discovery, sequencing, and timing all work together.

**Scope boundaries (binding):**
- NO SLAC/PLC (the layer below; the simulation runs over plain TCP/IP).
- Identification: EIM (External Identification Means). Plug & Charge contract
  certificates are a stretch goal, not a DoD criterion.
- No pause/resume, no renegotiation, no smart-charging detail — happy path only.

## Steps

### 1. New project `Vanaheimr.V2G.Simulation` (+ CLI)

- A library with three layers, cleanly separated and individually testable:
  a) **Transport**: SDP client/server (UDP), TCP listener/client, optional TLS
     (`SslStream`), V2GTP framing (reuse the Phase 4 dispatcher).
  b) **Session**: request/response handling, SessionID management,
     sequence timeouts (defaults from the spec's timing tables, configurable),
     ResponseCode evaluation.
  c) **State machines**: EVCC and SECC as explicit state enums with
     transition tables (no implicit async spaghetti) — every transition
     individually unit-testable.
- Plus `Vanaheimr.V2G.Simulation.Cli` with `evcc` and `secc` subcommands
  (parameters: interface/address, protocol choice -2/-20, AC/DC, TLS on/off,
  log directory).
- Mock "charging physics" behind interfaces (`IEvBattery`, `IEvsePowerSupply`):
  PreCharge/CableCheck/ChargeLoop must converge after n iterations,
  so tests terminate deterministically.

### 2. SDP (SECC Discovery Protocol)

- UDP, IPv6; SDP request/response as V2GTP frames with the SDP payload types.
  Take the exact payload-type IDs, ports, and the security/transport-protocol
  byte layout from the spec or libcbv2g/Josev — don't guess.
- For tests: configurable interface + port; loopback must work.
  Link-local multicast on Windows is finicky (needs an interface index) —
  implement it, but tests may fall back to loopback/unicast.

### 3. Session flow (happy paths)

- **-2 AC (EIM):** SAP → SessionSetup → ServiceDiscovery → PaymentServiceSelection
  → Authorization (poll until OK) → ChargeParameterDiscovery → PowerDelivery(Start)
  → ChargingStatus loop → PowerDelivery(Stop) → SessionStop.
- **-2 DC (EIM):** … → ChargeParameterDiscovery → CableCheck loop → PreCharge loop
  → PowerDelivery(Start) → CurrentDemand loop → PowerDelivery(Stop)
  → WeldingDetection → SessionStop.
- **-20 DC:** SAP → SessionSetup → AuthorizationSetup → Authorization →
  ServiceDiscovery → ServiceDetail → ServiceSelection → DC_ChargeParameterDiscovery
  → ScheduleExchange → DC_CableCheck → DC_PreCharge → PowerDelivery(Start)
  → DC_ChargeLoop → PowerDelivery(Stop) → DC_WeldingDetection → SessionStop.
- **-20 AC** analogous, with AC_ChargeParameterDiscovery/AC_ChargeLoop.
- Timeout/error paths minimal: a sequence timeout cleanly aborts the session,
  a FAILED ResponseCode ends it with a clear diagnosis. Nothing more.

### 4. TLS

- -2: TLS optional (the session must also run without TLS — Josev can do that for tests).
- -20: TLS is expected; implement server-side TLS with self-signed
  test certificates (check in under `Tests/TestData/`, clearly marked "test only").
  Mutual TLS: a documented gap, not a DoD criterion.
- Document the spec's cipher-suite requirements; whatever Schannel/.NET can't
  provide, record as a known deviation instead of forcing it.

### 5. Logging + capture as a vector source

- Every sent/received frame is logged in a structured way: hex + decoded
  message + timestamp.
- **Record mode**: received EXI streams are saved as vector candidates under
  `Tests/Vectors/captured/` (same JSON format, source noted).
  Frames from Josev are independently generated conformance vectors — the most
  valuable kind there is. Prepare a curated adoption path into the regular vector files.

### 6. Tests in two tiers

- **Tier 1 (CI, standard `dotnet test`):** in-process E2E — our EVCC against
  our SECC over loopback TCP (or an in-memory duplex stream): all four happy
  paths (-2 AC, -2 DC, -20 AC, -20 DC) run to SessionStop;
  assertions on the state sequence and final ResponseCodes. In addition,
  unit tests for SDP framing and individual state transitions (incl. timeout).
- **Tier 2 (opt-in, via env var/test category `Interop`):** against
  **Josev** (SwitchEV/iso15118, or the EVerest fork ext-switchev-iso15118):
  - our EVCC ↔ Josev SECC and Josev EVCC ↔ our SECC,
  - first -2 AC EIM without TLS, then -2 DC, then -20.
  - Setup: WSL2 or Docker (Josev needs Python + a JRE for its EXI codec);
    put setup scripts + a README with a pinned Josev version under
    `tools/interop-josev/`. These tests do NOT run in the standard CI run.
  - Every successful interop run: check in the full frame log as an artifact
    (`docs/interop-runs/<date>-<scenario>/`).

## Guardrails

- Codec/generator code is only touched in this phase if an interop run proves
  a concrete wire diff — then, as always: analyze the diff, fix the root
  cause, check in a vector as a regression test.
- `dotnet test -c Release` (tier 1) stays runnable without Python/Java/Docker/network
  beyond loopback.
- Keep state machines synchronously testable: time via an injectable
  clock/timer abstraction, never a hardcoded `Task.Delay`.
- All existing tests stay green. Small commits, only on a green build.
- Security: never present self-signed test certificates and test keys as
  production-ready; never check in real certificates.

## Definition of Done

1. Four in-process E2E happy paths (-2 AC/DC, -20 AC/DC) green in the standard test run.
2. SDP discovery works (unit tests + used in the E2E path).
3. TLS variant for -2 and -20 runnable with test certificates (at least one
   E2E test per protocol with TLS).
4. Interop documented: at least -2 AC EIM successful in BOTH directions against
   Josev@<version>, frame logs checked in as artifacts;
   -2 DC and -20 interop results (including partial successes) honestly documented.
5. Record mode delivers Josev frames as vector candidates; at least one
   curated adoption into the regular vector files.
6. CLI (`evcc`/`secc`) documented in the README; a simulation architecture chapter.
7. Closing report: codec/sequence discrepancies found, timing findings,
   known gaps (mutual TLS, PnC, WPT/ACDP).
