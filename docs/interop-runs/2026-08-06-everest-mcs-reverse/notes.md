# 2026-08-06 — **MCS in reverse**: their car picks service 8 out of *our* catalogue

**The one direction that puts our MCS catalogue in front of a foreign chooser, and it chose MCS.**
EVerest's `PyEvJosev`, configured `supported_d20_energy_services: MCS`, discovered our SECC by SDP and ran
a complete ISO 15118-20 session against it:

```
SECC listening on [::]:55000 (protocol -20, DC, TLS None)...
SDP: advertising [fe80::215:5dff:fe79:69ca%2]:55000 (NoTLS) on eth0...
Plug & Charge: contract DC=MO, C=DE, O=EVerest, CN=UKSWI123456789A;
               challenge OK, digest OK, signature OK (ecdsa-sha256, grammar=xmldsig-standalone).
Energy transfer service: 8 (MCS).

✓ Session complete in 51793 ms.
```

Every forward run so far proved that *their* station understands service 8. This proves the other half:
offered `{ 8, 9 }` by our `Secc20Mcs`, an independent EV picked 8 and drove the session on it.

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), native build |
| Their EV | `PyEvJosev` on the vendored Josev, `supported_d20_energy_services: MCS` |
| Ours | `Vanaheimr.V2G.Exi` @ `1b5d8ae`, `Secc20Mcs` via the CLI |
| Config | [`config-mcs-reverse-ours.yaml`](config-mcs-reverse-ours.yaml) — their `config-sil-mcs.yaml` plus four lines |
| Command | `secc --listen 55000 --protocol 20 --mode mcs --sdp --interface eth0` |

This note pinned the branch `claude/cli-mcs-mode` rather than a commit until 2026-08-09, and by then the
branch was gone — which is exactly the failure a run note is supposed to be immune to. `1b5d8ae` is that
branch's tip, recovered as the second parent of its merge (`7eb0464`, 2026-08-06 08:50), so it is
**reconstructed rather than recorded**. It is the right commit and it is not a contemporaneous
observation; the distinction is the same one this directory makes everywhere else.

## Finding 1 — Plug & Charge, verified, in the direction that had never shown it

Unasked for and the more valuable half of the run: their EV authorized with a **contract certificate**, and
our SECC verified the signed `AuthorizationReq` — challenge, reference digest and ECDSA signature all OK,
against their own `everest-aux` MO material (`CN=UKSWI123456789A`).

Worth placing precisely against what the matrix already claimed. -2 PnC against EVerest was proven the
other way round (*they* verify *our* signature, `docs/interop-runs/2026-08-05-everest-2026021-matrix/`
finding 6), and their `Evse15118D20` still has -20 PnC commented out — so **-20 PnC against EVerest had
never run in either direction**. It has now, inbound: their -20 EV signs, our SECC verifies. That the
same run also negotiated MCS is a coincidence of configuration, not a dependency.

## Finding 2 — the station could not report which service it had been given

This run could not be *read* at first. Our SECC printed nothing about the selection, and the answer is not
in their logs either — the session goes to our station, so their charger module never sees it.

The cause was an asymmetry in the app: `Evcc20Base.SelectedEnergyServiceId` is `public`, its `Secc20Base`
counterpart was `protected`. In the reverse direction the station is the **only** side that can report the
choice, and that was exactly the side that kept it private. Fixed by making it public — mirroring the EVCC
— and by having the CLI print the id with its Table 204 name at session end.

Worth stating as a general point about this repository: a reverse run whose result nobody can read is not
a result. The forward fixture has asserted `SelectedEnergyServiceId` since the first MCS run; the reverse
side had no equivalent because nothing had needed one.

## Finding 3 — their EV module's manifest under-documents its own config

`modules/EV/PyEvJosev/manifest.yaml` documents `supported_d20_energy_services` as taking
*"(DC, DC_BPT, AC, AC_BPT)"* — **MCS is not in the list**, yet their own `config/config-sil-mcs.yaml` sets
`supported_d20_energy_services: MCS`. The vendored Josev is fine: `load_requested_energy_services`
(`iso15118/shared/utils.py`) accepts `MCS` and `MCS_BPT` along with the rest. So the config works and only
the manifest documentation lags. Cheap to trip over, since a value absent from the manifest is a natural
thing to assume unsupported.

Written up for filing as [`docs/reports/pyevjosev-manifest-services.md`](../../reports/pyevjosev-manifest-services.md).
Two details found while drafting it sharpen the case: the manifest lists **4 of 12** accepted values, and
an unrecognised entry is *silently dropped* rather than reported — so the description is the only place
the correct spelling exists. The station side avoids the same trap by documenting its enum **by
reference** (`EvseManager.connector_type` → `/evse_manager#/ConnectorTypeEnum`), which is what the fix
should copy.

## What it took to run at all

The reverse direction has a topology problem the forward one does not: `PyEvJosev` finds a station **only**
by SDP multicast on its own interface, and our EVCC-side runs live on Windows. The answer is the one
`tools/interop-everest/reverse-iso2-dc.sh` already assumes — run our SECC **inside WSL**, on the same link,
with its own SDP server (`--sdp --interface eth0`). .NET 10 is present there and the CLI builds from
`/mnt/d` in about 45 s.

Config deltas, all four so their charger stays out of the way:

```diff
   iso15118_charger:            # their Evse15118D20 off eth0, so ours is the only station answering SDP
-      device: auto
+      device: lo
   iso15118_car:                # their EV onto eth0, plain TCP — our SECC listens without TLS
-      device: auto
+      device: eth0
-      tls_active: true
-      enable_tls_1_3: true
+      tls_active: false
+      enable_tls_1_3: false
   ev_manager:
-      auto_exec: false
+      auto_exec: true          # let their car drive its own session
```

Note their `EvManager` only knows `AC` and `DC` (`iso_start_v2g_session DC` → `EnergyTransferMode::DC_extended`);
the -20 service the EV asks for comes from Josev's `supported_energy_services`, not from that command. So
`DC` in the command list and `MCS` on the wire is correct, not a contradiction.

## Artifacts

`our-secc.log` (the verdict above), `their-manager.log`, and `config-mcs-reverse-ours.yaml`. No frame log
or trace: this run was driven by the CLI rather than the recording fixture, because the fixture cannot
advertise over SDP and their EV cannot be pointed at an endpoint. Making the reverse fixture recordable
against an SDP-discovering peer is the obvious next piece of harness work.

**Done the same day** — the fixture advertises now (`V2G_INTEROP_SDP=<iface>`), and this scenario was run
through it twice, with frames, flow reports and a corpus trace:
[`2026-08-06-everest-mcs-reverse-recorded`](../2026-08-06-everest-mcs-reverse-recorded/notes.md). It turned
up the fixture-side half of finding 2 above: the app could report the selected service by then, but
`InteropSession.RunSeccAsync` still returned a bare `Boolean`, so the reverse fixture had nowhere to put it
and would have passed an MCS run that never negotiated MCS.
