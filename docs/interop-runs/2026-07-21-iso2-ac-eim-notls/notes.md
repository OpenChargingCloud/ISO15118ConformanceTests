# Interop run — ISO 15118-2 AC, EIM, no TLS

- **Date:** 2026-07-21
- **Josev:** SwitchEV/iso15118 @ `d645255` ("Pydantic upgrade to v2", #455), Docker, EXI codec `EXICodec.jar` 1.55
- **Our commit:** the tree at the time this file was added
- **Scenario:** Josev EVCC ↔ Josev SECC, ISO 15118-2, AC, EIM, `SECC_ENFORCE_TLS=False`
- **Outcome:** ✅ full session; our codec cross-validates Josev's EXI byte-for-byte (see below)

## What was done

Ran Josev's own default session (`docker compose … up`, `.env.dev.docker`) with `MESSAGE_LOG_EXI=True`
so Josev logs the raw EXI hex of every message. Captured the frames (`frames.log`) and fed the EXI bytes
into **our** codec — Josev encodes with EXIficient (Java), which shares no lineage with the cbV2G oracle
our vectors come from, so agreement is an independent conformance signal.

## Cross-validation result (checked in as a regression test)

`WWCP_ISO15118_EXI_Tests/Interop/JosevCapturedFramesTests.cs` (runs in normal CI, bytes baked in):

| Message | Josev EXI (EXIficient) | Our codec |
|---|---|---|
| SupportedAppProtocolReq | `8000ebab…040040` | decodes to the expected content **and re-encodes byte-identically** |
| SupportedAppProtocolRes | `80400040` | round-trips byte-identically |
| SessionSetupReq (EVCCID 5E929D736493) | `8098004011d0197a4a75cd924c00` | decodes + re-encodes byte-identically |

The two SupportedAppProtocol frames are additionally **byte-identical to our existing cbV2G vectors**
(`req_iso2_only` / `res_ok_with_schemaid`) — so on these messages: **our codec ≡ cbV2G ≡ EXIficient/Josev**.

## Setup notes / friction

- **EOL Debian buster.** Josev's `template.Dockerfile` pins `python:3.10.0-buster`; Debian buster is EOL,
  so its apt repos 404 and `apt install default-jre` fails. Fixed by pointing apt at `archive.debian.org`
  (and dropping `buster-updates`) before the install. A modern base image (bookworm/trixie) would avoid
  this but deviates more from Josev's pinned environment.
- **Makefile uses `docker-compose` (v1)**; this host has Compose v2, so drive it with `docker compose`
  directly (replicate the Makefile's Dockerfile-templating + cert-gen steps by hand).
- **Live our-EVCC ↔ Josev-SECC over the network** was *not* done: Josev discovers the SECC via SDP
  (IPv6 link-local multicast, dynamic port) on its internal Docker bridge — bridging that to a Windows
  .NET process is disproportionate. Record-mode (capture Josev's frames, validate with our codec) gives
  the same conformance signal without the networking. Live interop stays the `JosevInteropTests` opt-in
  hook for an environment where both stacks share an L2 network.

## Next scenarios

- -2 DC EIM (change `EVCC_CONFIG_PATH` to the DC EIM config), then -20 (TLS 1.3 — use our BouncyCastle
  backend). Capture + cross-validate each the same way.
