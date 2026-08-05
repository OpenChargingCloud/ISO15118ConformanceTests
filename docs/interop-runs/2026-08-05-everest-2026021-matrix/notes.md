# 2026-08-05 — EVerest **2026.02.1**, the whole matrix again, from source

**The 2025.10.0 results hold on the current release — and both standing findings moved.** Every
scenario the 02/03.08 runs proved against the 2025.10 demo image was repeated against everest-core
**2026.02.1 built from source**: -2 DC, -20 DC Scheduled and Dynamic, IsoMux in all four offer shapes,
AC in both protocols, and -20 DC over mutual TLS 1.3. Same routes, same green, two exceptions worth
reading: the **unicast-SDP loop shutdown no longer reproduces**, and the **refused-TLS-handshake loop
shutdown still does** — so the draft report to EVerest stays valid but changes its lead trigger.

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), **native build** — no demo image |
| Their EXI | libcbv2g **v0.3.1** (in-tree `lib/everest/cbv2g`, statically linked); `Evse15118D20` on libiso15118 **v0.9.1** (in-tree) |
| Ours | `Vanaheimr.V2G.Exi` @ `bc93540` |
| Machine | WSL2 Debian 13 on Windows 11; station + relays in WSL, our EVCC on Windows through a TCP relay |
| Driven by | [`sil-car.sh`](../../../tools/interop-everest/sil-car.sh) `CP_AT_PLUGIN=1` + [`sdp-probe.sh`](../../../tools/interop-everest/sdp-probe.sh) (both under **bash**, see below) |
| Configs | checked in here: `config-*-ours.yaml` — same two-line deltas as the 2025.10 runs, plus one new one (below) |

This is the first counterparty here that was **built, not pulled**: 22 modules
(`EVEREST_INCLUDE_MODULES`), ~7 minutes on 16 cores, their CI's cmake flags. Worth having done once —
the demo-image ceiling (`everest-demo` still ships nothing newer than `2025.10.0-patches`) no longer
binds this harness.

## The matrix

| Scenario | 2025.10.0 (02/03.08) | **2026.02.1 (today)** |
|---|---|---|
| -2 DC EIM, forward | complete ×2 (43/49) | **complete ×2** (43/42), every response `OK` |
| second session, same `EvseV2G` process | no crash | **no crash** — the 2023.10.0 segfault stays dead |
| -20 DC Scheduled | complete ×2 (113/98) | **complete ×2** (61/62) — their cable check settles faster |
| -20 DC Dynamic | complete | **complete** (61) |
| IsoMux, -2-only offer | complete | **complete** (42) |
| IsoMux, -20-only offer | complete | **complete** (63) |
| IsoMux, both, -20 first | complete, routed -20 | **complete** (71), routed -20 |
| IsoMux, both, **-2 first** | routed **-20** — Priority ignored | **routed -20 again** (56) — **still ignored** |
| -2 AC | complete ×2 (13) | **complete ×2** (13/13) |
| -20 AC | `FAILED_ContactorError` at PowerDelivery | **same**, same message, 10 exchanges — the SIL still expects its own EV module |
| -20 DC, mutual TLS 1.3 | complete ×2 (116, macOS) | **their side completes** (61, bridged — see the platform finding) |
| unicast SDP → loop shutdown | reproduces 2/2 | **does not reproduce** 0/2 |
| refused TLS handshake → loop shutdown | reproduces | **reproduces 3/3** |

## Finding 1 — the loop-shutdown report: one trigger healed, the defect did not

[`docs/reports/everest-loop-shutdown.md`](../../reports/everest-loop-shutdown.md) names three triggers
for one defect: an error in any poll handler ends `TbdController::loop()` while the sockets stay bound.
On 2026.02.1:

- **Trigger 1 (unicast SDP request): fixed in behaviour.** Sent unicast to the station's own
  link-local, idle state: answered correctly, no shutdown. Sent again during a session: ignored with
  their own log line, no shutdown. Two attempts, both security bytes. The *code* the report quotes is
  unchanged (`sdp_server.cpp:106` still throws on `read_result <= 0`; `tbd_controller.cpp:54` still
  breaks the loop) — what changed is upstream of it: the malformed-payload path now warns and returns
  (`sdp_server.cpp:125`, "FIXME (aw): we should not die here immediately"), and the EAGAIN wakeup that
  a unicast reply used to provoke evidently no longer happens.
- **Trigger 3 (refused TLS handshake): alive.** Three independent reproductions today, all
  `Shutdown loop() because of: Failed to SSL_accept(): … certificate verify failed` (twice from a
  client chain their OpenSSL could not build, once from a deliberate client-side abort). After each,
  the **zombie state** the report describes was observed directly: SDP stays bound and silent — the
  next `sdp-probe.sh` times out against a socket that still exists — and nothing restarts the module.
- The contrast is now sharper than in the report: an `SSL_read_ex` failure on an **established**
  session is handled per-session ("Shutting down session … Closing TLS connection gracefully", loop
  survives — observed today after an `openssl s_client` disconnect), while the same class of error
  inside `SSL_accept` ends the world. That asymmetry is the one-line argument for scoping accept-path
  errors to the connection.

**Consequence for the report:** it can be filed, and should lead with the TLS-accept trigger; the
unicast-SDP section becomes "fixed in 2026.02.1, code still fragile". Updated in the report itself.

## Finding 2 — IsoMux still routes on "mentions -20", not on Priority

`V2G_INTEROP_SAP_FIRST=2` sent the discriminating offer again — -2 at SchemaID 1 / Priority 1, -20 at
SchemaID 2 / Priority 2 — and their mux answered `SchemaID 2` and handed the connection to
`Evse15118D20`, exactly as on 2025.10. The selection loop in `modules/EVSE/IsoMux/v2g_server.cpp` is
unchanged at the tag and at `main` (checked both): first entry whose namespace starts with
`urn:iso:std:iso:15118:-20` wins, `Priority` is never read. Our EVCC followed the station's answer and
completed in -20, so interop is unaffected; the EV's ranking is not.

## Finding 3 — stock `config-sil-dc-d20.yaml` is now Dynamic-only

2025.10's SIL config enabled both control modes on `Evse15118D20`; 2026.02.1's sets neither, and the
module's defaults are `supported_dynamic_mode: true`, `supported_scheduled_mode: false`. A Scheduled
EVCC against their **stock** SIL -20 station now fails service selection. [`config-d20-ours.yaml`](config-d20-ours.yaml)
re-enables both — the same posture the 2025.10 runs had, restored explicitly. Their stock d20 also
warns at boot that its advertised services include `DC_BPT`/`MCS_BPT` without BPT limits configured —
worth knowing before a BPT attempt.

## Finding 4 — mutual TLS 1.3, and what each side of it proved

Their PKI workflow is unchanged: `create_certs.sh -v iso-20` from the vendored Josev
(`build/_deps/josev-src/iso15118/shared/pki/`), output into `dist/etc/everest/certs/`, vehicle chain
`VEHICLE_LEAF ← VehicleSubCA2 ← VehicleSubCA1 ← V2GRootCA`, all password `123456`.

- **Their side holds.** `openssl s_client` with that credential: TLS 1.3, `TLS_AES_256_GCM_SHA384`,
  `Verification: OK`, client chain accepted. A full -20 DC session then ran through an
  openssl-terminated bridge (socat `OPENSSL:…,openssl-min-proto-version=TLS1.3,openssl-commonname=SECCCert`)
  to `SessionStop` — 61 exchanges, every response `OK`
  ([`iso20-dc-tls13.bridged.flow.md`](iso20-dc-tls13.bridged.flow.md)). So 2026.02.1 behaves like
  2025.10 as a TLS 1.3 mutual station. Note their leaf still says `CN=SECCCert` and the station still
  sends **only its leaf** — both 2025.10 findings intact.
- **Our TLS client could not be exercised on this machine, and the reason is now precise.** On
  Windows, `TlsPlatform` routes TLS 1.3 through `SslStream`/Schannel (the BouncyCastle fallback is
  macOS-gated), and Schannel **refuses to present a client chain whose root the system store does not
  trust** — `Die Zertifikatkette wurde von einer nicht vertrauenswürdigen Zertifizierungsstelle
  ausgestellt`, upstream of the wire; their side then sees a chain it cannot verify. Installing a
  throwaway test root into the user's Windows trust store was not an acceptable fix. The 2025.10 TLS
  result came from the BC path on macOS; the faithful -20 TLS client on Windows needs the same
  fallback made reachable there — named as follow-up for the app, not changed today.

So the honest TLS row reads: *station side re-validated on 2026.02.1; our client side remains
validated on 2025.10/macOS only.*

## Finding 5 — `Evse15118D20`'s TCP endpoint is strictly one-per-SDP

Sharper than the 2025.10 notes had it: the TCP server their SDP response names exists **for exactly
one connection**. After every session — completed or refused — the next connection to the same port is
reset, and a fresh multicast SDP request creates the next server (same port 50000 here, every time).
The per-session ritual is therefore: replug the car, probe, re-point the relay, connect. `EvseV2G`
(-2) keeps its bound port across sessions; `IsoMux` does too (61342, TLS 64110 — same numbers as
2025.10).

## Run environment (the part that was new today)

Native everest-core build, WSL2 Debian 13: `cmake -G Ninja -DEVEREST_INCLUDE_MODULES=<22 modules>
-DISO15118_2_GENERATE_AND_INSTALL_CERTIFICATES=OFF`, then `ninja install`; test PKI copied from
`tests/ocpp_tests/test_sets/everest-aux/certs/` as before (the certificate-or-abort behaviour of
`Evse15118D20` is unchanged). `PyEvJosev` needs the vendored Josev's `requirements.txt` installed for
its module process to boot — the demo images bake that in, a fresh build does not, and the manager
exits when any module dies.

Three machine notes, all cheap once known:

- **Their helper scripts assume bash-flavoured `printf`.** Under Debian's `dash`, `printf '\x01…'`
  emits the literal text, the station logs *"Sdp server received an unexpected payload"* (v0.9.1
  survives it — v0.9.0's report-trigger-1 would not have), and the probe reads as "no response". Run
  `sdp-probe.sh`/`sil-car.sh` with `bash` explicitly.
- **WSL kills a session's processes when its last `wsl.exe` client exits.** Anything long-running —
  manager, relays, mosquitto — needs `setsid` + a grace period, plus one held-open client for the
  session's lifetime. The relay listener must be **IPv4** (`TCP-LISTEN`, not `TCP6-LISTEN`) for
  Windows' localhost forwarding to reach it; the outbound leg speaks IPv6 to the link-local.
- **IPv6 multicast loopback works** on the WSL NIC — `sdp-probe.sh` from the station's own host
  reaches it, no second container/namespace needed.

`CP_AT_PLUGIN=1` on every scenario including -2 DC: the `Start_CableCheck` trigger topic shape died
with the 2023 image, and holding state C from the plug-in is the posture that works everywhere.

## Not repeated, and why

- **PnC** (2026-08-03-everest-pnc): needs the MO contract credential packaged for our side and the
  station's PnC configuration; nothing suggests 2026.02.1 moved, but it is unverified. Next.
- **Reverse** (`PyEvJosev` → our SECC): was not in the 02/03.08 set either; still the
  lowest-information direction (their car is Josev).
- **MCS: no longer parked for their reason.** `config-sil-mcs.yaml` **exists in 2026.02.1** — the
  counterpart our roadmap called distant is now one config away. What is missing now is on our side:
  the interop fixture has no MCS arm. That is the single most valuable next piece of work this run
  surfaced.

## Artifacts

Per scenario: `<name>.flow.md`, `<name>.frames.log`, `<name>.trace.json` (where the session was
well-formed), `their-charger.<station>.log`, and every `config-*-ours.yaml`. Recorded with
`V2G_INTEROP_RECORD`; scenario comparisons ran against the corpus traces named in each flow report.
