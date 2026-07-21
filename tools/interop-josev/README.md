# Josev interop (Tier 2)

Interop between **our** EVCC/SECC and **Josev** (an independent Python ISO 15118 stack). This is the
first cross-validation of our codecs, V2GTP framing, and session sequencing against a stack that does
*not* share our lineage (cbV2G / EXIficient) — the most valuable conformance signal we can get short of
real hardware.

These runs are **opt-in and never part of the offline CI**. The automated hook is the
`[Explicit] [Category("Interop")]` fixture `Vanaheimr.V2G.Simulation.Tests/Interop/JosevInteropTests.cs`,
gated on env vars — `dotnet test -c Release` skips it entirely.

Josev is **not vendored** here (Python + a JRE, big); you bring it up per the steps below.

## What Josev is

- **SwitchEV/iso15118** — <https://github.com/SwitchEV/iso15118> (Apache-2.0, -2 and -20).
- EVerest fork (more actively maintained) — <https://github.com/EVerest/ext-switchev-iso15118>.

Josev needs **Python 3.10+** and a **JRE** (its EXI codec is the Java `EXICodec.jar`). Easiest via its
own Docker setup; otherwise a venv under WSL2.

> **Pinned version (first run):** SwitchEV/iso15118 @ `d645255` ("Pydantic upgrade to v2", #455),
> validated 2026-07-21 — see [`docs/interop-runs/2026-07-21-iso2-ac-eim-notls/`](../../docs/interop-runs/2026-07-21-iso2-ac-eim-notls/).

> **Known setup fix — EOL Debian buster.** Josev's `template.Dockerfile` pins `python:3.10.0-buster`;
> buster is EOL so its apt repos 404 and `apt install default-jre` fails. Before that install, repoint
> apt at the archive:
> ```
> sed -i 's|http://deb.debian.org/debian|http://archive.debian.org/debian|g; \
>         s|http://security.debian.org/debian-security|http://archive.debian.org/debian-security|g; \
>         /buster-updates/d' /etc/apt/sources.list
> apt-get -o Acquire::Check-Valid-Until=false update && apt-get install -y default-jre
> ```
> Also: the Makefile calls `docker-compose` (v1); with Compose v2 drive `docker compose` directly and
> replicate the Makefile's Dockerfile-templating + `create_certs.sh` steps by hand.

> **Capture EXI without live networking (record mode).** Set `MESSAGE_LOG_EXI=True` in `.env.dev.docker`
> and run Josev's own EVCC↔SECC session; Josev then logs every message's raw EXI hex. Feed those bytes
> into our codec (`JosevCapturedFramesTests`) — same conformance signal as live interop, no SDP/IPv6
> bridging needed.

## Setup (choose one)

### Docker (recommended)
Josev ships a compose setup. Clone at the pinned commit and start the SECC:
```bash
git clone https://github.com/SwitchEV/iso15118 && cd iso15118 && git checkout <sha>
# follow its README; typically:
make build && make run-secc      # or run-evcc
```

### WSL2 venv
```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -e .                 # + a JRE on PATH for EXICodec.jar
```

### Josev config that matters for interop
- **Identification: EIM** (External Identification Means) — start here; Plug & Charge (contract certs)
  is a later scenario.
- **TLS off** for the first scenario (`-2` allows it; Josev can serve plain TCP for tests).
- **Interface / port**: note the TCP port Josev's SECC listens on after SDP. Our test connects to it
  directly (host:port), bypassing SDP; make sure Josev accepts that (or run SDP — see "Known friction").

## Scenario order

Work up in difficulty; capture each result:

1. **-2 AC, EIM, no TLS** — the simplest full session. Get this green first.
2. **-2 DC, EIM, no TLS**.
3. **-20** (AC then DC) — needs TLS 1.3; use our **BouncyCastle** backend for the -20-faithful
   secp521r1 profile (Schannel can't). Mutual TLS + Vehicle/Contract certs per `docs/pki-model.md`.

## Running

### Our EVCC → Josev SECC
1. Start Josev SECC (see setup); note its `host:port`.
2. Point our EVCC at it, either via the CLI:
   ```bash
   dotnet run --project ../../Vanaheimr.V2G.Simulation.Cli -- \
       evcc --connect <josev-host:port> --protocol 2 --mode ac
   ```
   or via the interop test:
   ```bash
   V2G_INTEROP_SECC=<josev-host:port> V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=ac \
     dotnet test ../../Vanaheimr.V2G.Simulation.Tests -c Release --filter TestCategory=Interop
   ```

### Josev EVCC → our SECC
1. Start our SECC (CLI `secc --listen <port> ...`, or the test with `V2G_INTEROP_LISTEN=<port>`).
2. Point a Josev EVCC at `host:<port>`.

The interop test env vars: `V2G_INTEROP_SECC=host:port`, `V2G_INTEROP_LISTEN=port`,
`V2G_INTEROP_PROTOCOL=2|20` (default 2), `V2G_INTEROP_MODE=ac|dc` (default ac), `V2G_INTEROP_TLS=1`
(EVCC accepts any server cert — dev only).

Helper wrappers: [`run-our-evcc.sh`](run-our-evcc.sh), [`run-our-secc.sh`](run-our-secc.sh).

## Capture successful runs

For every green scenario, save the full frame log (hex + decoded + timestamp) under
`docs/interop-runs/<yyyy-mm-dd>-<scenario>/` and note the Josev commit. Frames captured from Josev are
independently-generated conformance vectors — the highest-value kind (see `docs/interop-runs/README.md`
for the record-mode adoption path into the regular vector files).

## Known friction (expect these first)

- **String value tables.** Our encoder is miss-only (byte-identical to cbV2G); Josev/EXIficient *may*
  emit table hits. Our decoder handles hits (`ExiStringTable`), but confirm — this is a classic interop
  gap and exactly what these runs surface.
- **SDP vs direct connect.** Our test connects to a fixed `host:port`. If Josev insists on the SDP
  discovery first, run our `SdpSeccDiscovery` against Josev's SDP server (real IPv6 interface) instead
  of hard-coding the endpoint.
- **-20 TLS profile.** Mutual TLS 1.3 + secp521r1/Ed448 — use our BouncyCastle backend; align cipher
  suites/curves with Josev's config (see the CharIN TLS guide notes in `docs/pki-model.md`).
- **Timing.** These use real wall-clock delays (`TaskAsyncDelay`), unlike the loopback tests — Josev's
  timeouts are real; keep the per-message timeout generous.
