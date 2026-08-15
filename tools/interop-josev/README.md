# Josev interop (Tier 2)

Interop between **our** EVCC/SECC and **Josev** (an independent Python ISO 15118 stack). This is the
first cross-validation of our codecs, V2GTP framing, and session sequencing against a stack that does
*not* share our lineage (cbV2G / EXIficient) — the most valuable conformance signal we can get short of
real hardware.

These runs are **opt-in and never part of the offline CI**. The automated hook is the
`[Explicit] [Category("Interop")]` fixture `ISO15118ConformanceTests.Simulation/Interop/JosevInteropTests.cs`,
gated on env vars — `dotnet test -c Release` skips it entirely.

Josev is **not vendored** here (Python + a JRE, big); you bring it up per the steps below.

> **Josev's particular value among the counterparties:** its EXI comes from EXIficient, which shares no
> lineage with our cbV2G-generated corpus, so a byte disagreement here is a genuinely independent
> finding. The same is true of eVDriveFlow (OpenEXI) for -20 — see
> [`../interop-evdriveflow/README.md`](../interop-evdriveflow/README.md) — and not true of the
> cbexigen-based stacks, [`../interop-tux-evse/README.md`](../interop-tux-evse/README.md).

> Set `V2G_INTEROP_RECORD=<dir>` on any of these runs and the fixture writes the session out — raw
> octets, a frame log, and a replayable `SessionTrace`. See `docs/interop-runs/README.md`.

## What Josev is

- **SwitchEV/iso15118** — <https://github.com/SwitchEV/iso15118> (Apache-2.0, -2 and -20).
- EVerest fork (more actively maintained) — <https://github.com/EVerest/ext-switchev-iso15118>.

Josev needs **Python 3.10+** and a **JRE** (its EXI codec is the Java `EXICodec.jar`). Easiest via its
own Docker setup; otherwise a venv under WSL2.

> **Pinned version (first run):** SwitchEV/iso15118 @ `d645255` ("Pydantic upgrade to v2", #455),
> validated 2026-07-21 — see [`docs/interop-runs/2026-07-21-iso2-ac-eim-notls/`](../../docs/interop-runs/2026-07-21-iso2-ac-eim-notls/).

> **Default: build Josev on Debian trixie.** Josev's `template.Dockerfile` pins the EOL
> `python:3.10.0-buster`, whose apt repos 404 (so `apt install default-jre` fails). The default here is to
> rebase it onto a current Debian — [`prepare-josev.sh`](prepare-josev.sh) does this: it rewrites both
> `FROM python:3.10.0-buster` lines to `python:3.10-trixie`, generates the secc/evcc Dockerfiles, and makes
> the test certs, so `docker compose build` just works. The interop only depends on Python 3.10.x + Josev's
> `poetry.lock`, not the Debian release, so the rebase is safe (validated: the -2 DC run below used it).
>
> <details><summary>Fallback: keep EOL buster (minimal deviation from Josev's pin)</summary>
>
> Repoint apt at the archive before installing the JRE:
> ```
> sed -i 's|http://deb.debian.org/debian|http://archive.debian.org/debian|g; \
>         s|http://security.debian.org/debian-security|http://archive.debian.org/debian-security|g; \
>         /buster-updates/d' /etc/apt/sources.list
> apt-get -o Acquire::Check-Valid-Until=false update && apt-get install -y default-jre
> ```
> </details>
>
> Note: the Makefile calls `docker-compose` (v1); with Compose v2 drive `docker compose` directly —
> `prepare-josev.sh` already does the Dockerfile-templating + `create_certs.sh` steps `make build` would.

> **Capture EXI without live networking (record mode).** Set `MESSAGE_LOG_EXI=True` in `.env.dev.docker`
> and run Josev's own EVCC↔SECC session; Josev then logs every message's raw EXI hex. Feed those bytes
> into our codec (`JosevCapturedFramesTests`) — same conformance signal as live interop, no SDP/IPv6
> bridging needed.

## Setup (choose one)

### Docker (default)
Clone at the pinned commit, prepare it (trixie rebase — see the note above), build, and run:
```bash
git clone https://github.com/SwitchEV/iso15118 && cd iso15118 && git checkout d645255
../prepare-josev.sh .                    # rebase to trixie + generate Dockerfiles + certs
docker compose build
docker compose -f docker-compose.yml -f docker-compose.dev.yml up   # Josev EVCC <-> SECC session
```
(Josev's own `make build && make run-secc` assumes `docker-compose` v1 and the EOL buster base; the two
lines above replace it for Compose v2 + a current Debian.)

> **Compose names the images after the directory it built in**, so a clone at `~/josev-src` yields
> `josev-src-evcc:latest`, not the `iso15118-evcc:latest` every scenario script here expects. Tag it
> rather than renaming the clone — `docker tag josev-src-evcc:latest iso15118-evcc:latest` — since the
> run notes name the checkout by its commit and not by its path.

> **One PKI serves both protocols, and that is theirs, not ours.** Every certificate path in Josev
> resolves under `iso15118_2/certs/` whatever the session is, hard-coded, with their own *"TODO: Make
> filepath flexible"* above it (`iso15118/shared/security.py:1445`). `create_certs.sh -v iso-20` writes a
> second tree that nothing reads. So `-v iso-2` is the one that matters, and a `-20` run against a `-20`
> tree fails for a reason that looks like a counterparty defect.

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

Work up in difficulty; capture each result (all three below are **done** — see `docs/interop-runs/`):

1. ✅ **-2 AC, EIM, no TLS** — the simplest full session.
2. ✅ **-2 DC, EIM, no TLS**.
3. ✅ **-20 DC, Plug & Charge, no TLS** — captured in **record mode**. Josev's `-20 DC` example config sets
   `useTls:false`, and record mode logs the *plaintext* EXI (TLS only wraps the transport), so no TLS backend
   is needed to cross-validate the -20 message encoding. Point `EVCC_CONFIG_PATH` at the venv copy
   (`/venv/lib/python3.10/site-packages/iso15118/shared/examples/evcc/iso15118_20/evcc_config_dc.json` — the
   image's working-dir tree only carries the -2 examples), keep `SECC_ENFORCE_TLS=False`, run the session.
   **All 30/30 frames round-trip byte-exact** (the signed `AuthorizationReq` initially exposed the xmldsig
   `Transforms` generator gap, since fixed). See `docs/interop-runs/2026-07-21-iso20-dc-pnc-notls/`.

Everything past record mode is **also done** — complete live sessions in both directions, plain TCP and
TLS, EIM and Plug & Charge, all four -20 energy modes, both control modes, plus cert-install,
pause/resume, renegotiation and signed tariffs. One scenario script per feature block (each with a
matching write-up under `docs/interop-runs/2026-07-22-*/`):

| Script | Scenario |
|---|---|
| `reverse-dynamic-sdp.sh` | -20 Dynamic control mode (DC / DC_BPT / AC_BPT), Josev EVCC → our SECC |
| `live-evcc-pnc-tls.sh` | -20 forward PnC over TLS: our signed AuthorizationReq verified by Josev |
| `pnc-chain-setup.sh` | the prerequisites the two reverse PnC scripts assume: `/tmp/secc.p12`, the -20 EVCC config, and the four trust-root directories the arms and controls differ by |
| `reverse-iso2-pnc-tls-sdp.sh` / `live-evcc-iso2-pnc-tls.sh` | -2 Plug & Charge over TLS, both directions |
| `reverse-certinstall-sdp.sh` / `certinstall-probe.py` | -20 CertificateInstallation (contract provisioning) |
| `live-evcc-pause-resume.sh` | Pause → reconnect → `OK_OldSessionJoined` resume, forward |
| `reverse-renegotiate-sdp.sh` / `live-evcc-renegotiate.sh` | Renegotiation (-2 both ways, -20 to Josev's limit) |
| `reverse-tariff-sdp.sh` / `live-evcc-tariff.sh` / `live-evcc-tariff-verify.sh` | Signed tariffs: our signed -2/-20 offers consumed by Josev; our EVCC verifying Josev's real MO-Sub-CA2-signed SalesTariff |

## Running

### Our EVCC → Josev SECC
1. Start Josev SECC (see setup); note its `host:port`.
2. Point our EVCC at it, either with the vehicle program:
   ```bash
   dotnet run --project ../../libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EVCC -- \
       --connect <josev-host:port> --protocol 2 --mode ac
   ```
   or via the interop test:
   ```bash
   V2G_INTEROP_SECC=<josev-host:port> V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=ac \
     dotnet test ../../ISO15118ConformanceTests.Simulation -c Release --filter TestCategory=Interop
   ```

### Josev EVCC → our SECC
1. Start our SECC (CLI `secc --listen <port> ...`, or the test with `V2G_INTEROP_LISTEN=<port>`).
2. Point a Josev EVCC at `host:<port>`.

The interop test env vars: `V2G_INTEROP_SECC=host:port`, `V2G_INTEROP_LISTEN=port`,
`V2G_INTEROP_PROTOCOL=2|20` (default 2), `V2G_INTEROP_MODE=ac|dc` (default ac), `V2G_INTEROP_TLS=1`
(EVCC accepts any server cert — dev only).

### Validating what their car signs with, not just what it signed

The reverse Plug & Charge scripts go through the **station CLI**, so the anchor is `--trust-roots
<file|dir>` rather than the fixture's `V2G_INTEROP_CONTRACT_ROOTS`. Without it the station prints *chain
not checked — no `--trust-roots`*, which is the state every Josev PnC run was recorded in until
2026-08-15; with it, the two arms of a proper measurement are:

| Protocol | The arm | The control |
|---|---|---|
| `-2`, unilateral TLS 1.2 | `moRootCACert.pem` | `v2gRootCACert.pem` |
| `-20`, mutual TLS 1.3 | a **directory** with `oemRootCACert.pem` **and** `moRootCACert.pem` | the same directory minus the MO root |

The `-20` control keeps the OEM root in **both** arms on purpose: their car presents an OEM-rooted client
certificate (`security.py:209`), so dropping that would change the handshake instead of the contract
check — and the arms must differ in one thing. Their PKI has three genuinely separate self-signed roots,
V2G, MO and OEM, which is what makes the controls sharp.

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
  suites/curves with Josev's config (see the CharIN TLS guide notes in the app's
  [`docs/pki-model.md`](../../libs/EVSimulatorApp/docs/pki-model.md)).
- **Timing.** These use real wall-clock delays (`TaskAsyncDelay`), unlike the loopback tests — Josev's
  timeouts are real; keep the per-message timeout generous.
