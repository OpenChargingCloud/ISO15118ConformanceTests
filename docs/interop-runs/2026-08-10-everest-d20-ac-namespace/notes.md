# 2026-08-10 — a DC-only `Evse15118D20` accepts the ISO 15118-20 **AC** message set

Offered `urn:iso:std:iso:15118:-20:AC` and nothing else, a station configured for DC — DC power supply,
`charge_mode: DC`, no AC hardware anywhere in the module graph — answers **`OK_SuccessfulNegotiation`**
and commits the session to the AC schema. Four message pairs later, at `ServiceDiscovery`, it lists the
two DC services it actually has and the session dies there.

`Failed_NoNegotiation` exists for exactly this, and their handler returns it only when the offer
contains no `-20` namespace at all.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), source build in WSL2 |
| Their module | `Evse15118D20` alone (no `IsoMux`), `config-d20-ours.yaml`, plain TCP |
| Ours | `WWCP_ISO15118` @ `4b46bc5`, EVCC — plus a throwaway SAP probe, since the fixture has no knob for a two-namespace `-20` offer |
| Outcome | **AC accepted by a DC station; the mismatch surfaces at `ServiceDiscovery`, not at the handshake** |
| Artifacts | [`their-charger.ac.log`](their-charger.ac.log), [`their-charger.dc.log`](their-charger.dc.log), [`their-charger.aconly.log`](their-charger.aconly.log), [`their-charger.cons2.log`](their-charger.cons2.log), [`sil-car.log`](sil-car.log) |
| Filed | [`everest-d20-ac-namespace.md`](../../reports/everest-d20-ac-namespace.md) |

## Three arms at the handshake

Each arm gets a fresh manager — their `-20` station serves one session per process life, and
libiso15118 creates the TCP server when the SDP request arrives, so a stale port answers nothing.

| Arm | The EV offered | The station answered |
|---|---|---|
| **A** | `[1] -20:AC`, `[2] -20:DC` | **`-20:AC`** |
| **B** (control) | `[1] -20:DC`, `[2] -20:AC` | `-20:DC` |
| **C** | `[1] -20:AC` — nothing else | **`-20:AC`**, `OK_SuccessfulNegotiation` |

**B is the control and it matters**: priority *is* honoured, so this is not the ranking defect
[`IsoMux` has](../../reports/everest-isomux.md). What is missing is the other half of the same
requirement — the station's own capability as a filter, applied before the ranking.

**C is the finding in its purest form.** One entry, the AC message set, a station that has no AC
anything, and a positive answer.

## What follows, when the session is allowed to continue

Arm C again, with `sil-car.sh CP_AT_PLUGIN=1` so authorization completes and the session can get past
it. Our EVCC in `-20` AC mode:

```
SupportedAppProtocolReq(-20:AC)      → OK_SuccessfulNegotiation      ← the station commits to AC here
SessionSetupReq                      → OK
AuthorizationSetupReq                → OK
AuthorizationReq                     → OK           (the plug-in token)
ServiceDiscoveryReq                  → OK, services 2 and 6
```

and our car stops, correctly:

```
SessionAborted: ServiceDiscovery: the station offers no AC energy-transfer service
                (wanted 1/5, offered 2, 6).
```

Services 2 and 6 are `DC` and `DC_BPT`. So the station spent four message pairs, an authorization and a
token on a session whose message set it could never serve, and the EV is the one that had to notice.

Without the car the same session stalls at `Authorization` for 60 s instead — the ordinary no-token
wall, and the reason the first attempt showed nothing beyond the handshake.

## Where it comes from

`lib/everest/iso15118/src/iso15118/d20/state/supported_app_protocol.cpp:27-44`:

```cpp
std::map<uint8_t, uint8_t> ev_supported_protocols{}; // key: priority, value: schema_id

for (const auto& protocol : req.app_protocol) {
    if (protocol.protocol_namespace.compare(ISO20_DC_NAMESPACE) == 0) {
        ev_supported_protocols[protocol.priority] = protocol.schema_id;
    } else if (protocol.protocol_namespace.compare(ISO20_AC_NAMESPACE) == 0) {
        ev_supported_protocols[protocol.priority] = protocol.schema_id;
    } else if (protocol.protocol_namespace.compare(custom_protocol_namespace.value_or("")) == 0) {
        ev_supported_protocols[protocol.priority] = protocol.schema_id;
    }
}

if (ev_supported_protocols.empty()) {
    return response_with_code(res, ResponseCode::Failed_NoNegotiation);
}

res.schema_id = ev_supported_protocols.begin()->second; // [V2G20-167] Highest Prio: 1, Lowest Prio: 20
```

`ISO20_AC_NAMESPACE` and `ISO20_DC_NAMESPACE` land in the same map with no reference to what this
station is configured for. `handle_request` takes only the request and the *custom* namespace — the
session config it would need is not passed in at all. The `[V2G20-167]` comment on the last line is
correct as far as it goes: the ranking is applied. The filter that should precede it is absent.

## The requirement

- **`[V2G20-169]`** — the SECC selects, **from the protocols it supports itself**, the one the EVCC
  ranked highest. Two halves: *supports itself* is a filter, `Priority` ranks what survives it. This
  station applies the ranking and skips the filter. (`[V2G2-169]` is the `-2` twin; the `-20`
  identifier needs no document caveat.)
- **`[V2G20-167]`** — defines the field, and is the one their comment already cites.
- The `-20` AC and DC namespaces are separate `ProtocolNamespace` values precisely because they select
  different message sets: answering with the AC SchemaID tells the EV to encode the rest of the session
  against the AC schema. It is a commitment, not a preference.

Recorded in [`normative-basis.md`](../../normative-basis.md).

## How it was run

```bash
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-d20-ours.yaml &   # fresh per arm
bash tools/interop-everest/sdp-probe.sh eth0                                       # → port, per arm
# probe: SapHandshake.RunEvccSideAsync(stream, [SapOffer(Iso20, Ac), SapOffer(Iso20, Dc)])
# consequence: V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac against the same station, car plugged in
```

Two rig notes for anyone repeating it, both of which cost time here:

- **The address must carry a numeric zone.** `sdp-probe.sh` prints
  `[fe80:0000:…:03d4%eth0]`, and `TcpClient.ConnectAsync(host, …)` cannot resolve `%eth0`; the
  interop fixture goes through `V2GEndpoint.ConnectHost`, which is why its runs work and a hand-rolled
  probe using `.Host` fails with a socket error and no packet on the wire. Use
  `[fe80::215:5dff:fe6b:3d4%2]` or `ConnectHost`.
- **`Evse15118D20` writes its session log to a relative path**, so it lands wherever the manager was
  started. Start it from outside the repository — two stray `2608*.yaml` files were written into this
  working tree before that was noticed, and deleted.

*The probe itself was a throwaway and is not in the tree: the interop fixture has no knob for a
two-namespace `-20` offer, and adding one for a single finding did not seem worth the surface. The
three lines it needed are in the code block above.*
