# 2026-08-03 — EVerest `Evse15118D20`, ISO 15118-**20** DC: a complete charge

**A complete ISO 15118-20 DC session against a foreign station, and the route matches ours exactly.**
113 exchanges from `SupportedAppProtocolReq` to `SessionStopRes` — AuthorizationSetup, ServiceDetail,
ServiceSelection, DC_ChargeParameterDiscovery, ScheduleExchange, DC_CableCheck, DC_PreCharge,
PowerDelivery, the DC_ChargeLoop, DC_WeldingDetection — every response `OK`.

This also settles two things we had written down as findings and one we had written down as a
limitation. All three were wrong in the same way: **we had been running a three-year-old image.**

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2025.10.0**, module `Evse15118D20` on libiso15118 v0.9.0 / libcbV2G v0.3.1 |
| Image | `ghcr.io/everest/everest-demo/manager@sha256:5b0136c31a9f4be985df313b5b1d2e90464d00b203f63613199657f2697ce097` (`:2025.10.0-patches`) |
| Ours | `Vanaheimr.V2G.Exi` @ `6786550` |
| Direction | our EVCC → their charger |
| Session | ISO 15118-20 DC, Scheduled mode, plain TCP, [`config-d20-ours.yaml`](config-d20-ours.yaml) |
| Driven by | [`sil-car.sh`](../../../tools/interop-everest/sil-car.sh) `CP_AT_PLUGIN=1` + [`sdp-probe.sh`](../../../tools/interop-everest/sdp-probe.sh) |
| Outcome | **complete charge, 113/113 `OK`, route identical to our own recorded -20 session** |
| Artifacts | [`flow.md`](flow.md), [`frames.log`](frames.log), [`session.trace.json`](session.trace.json), [`their-charger.log`](their-charger.log) |

## The session

```
0     SupportedAppProtocolReq        → SupportedAppProtocolRes        OK_SuccessfulNegotiation
1     SessionSetupReq                → SessionSetupRes                OK_NewSessionEstablished
2     AuthorizationSetupReq          → AuthorizationSetupRes          OK
3–4   AuthorizationReq × 2           → AuthorizationRes               OK  (Ongoing, then Finished)
5     ServiceDiscoveryReq            → ServiceDiscoveryRes            OK
6     ServiceDetailReq               → ServiceDetailRes               OK
7     ServiceSelectionReq            → ServiceSelectionRes            OK
8     DC_ChargeParameterDiscoveryReq → DC_ChargeParameterDiscoveryRes OK
9     ScheduleExchangeReq            → ScheduleExchangeRes            OK
10…   DC_CableCheckReq × 95          → DC_CableCheckRes               OK
105   DC_PreChargeReq                → DC_PreChargeRes                OK
106   PowerDeliveryReq               → PowerDeliveryRes               OK
107–9 DC_ChargeLoopReq × 3           → DC_ChargeLoopRes               OK
110   PowerDeliveryReq               → PowerDeliveryRes               OK
111   DC_WeldingDetectionReq         → DC_WeldingDetectionRes         OK
112   SessionStopReq                 → SessionStopRes                 OK
```

Their `ServiceDetailRes` is the most substantial single message any counterparty has sent us: a 146-byte
parameter list naming `ControlMode`, `MobilityNeedsMode`, `Pricing` and `Connector`. Their side ran a
real cable check this time — isolation monitor self-test, one measurement sample, `R_F 900000` — then
500 V/2 A for the pre-charge and **400 V/120 A** for the loop, `PrepareCharging->Charging`.

Run twice, as the harness now requires. The second session (98 exchanges) also completed and **their
module did not crash** — unlike `EvseV2G` in the 2023 image.

## Correction 1 — the second-session crash is not in the current release

We recorded, five times, that `EvseV2G` segfaults on the second V2G session in a process and takes the
whole charger down with it, and put "report this to EVerest" in the roadmap. Before reporting it, the
same test against the **2025.10** `EvseV2G` (`config-sil-dc.yaml`, same two-session procedure):

| | exchanges | outcome |
|---|---|---|
| [session 1](evsev2g-2025-session1.flow.md) | 43 | complete charge, all `OK` |
| [session 2](evsev2g-2025-session2.flow.md) | 49 | complete charge, all `OK` |

**Zero crashes.** So the defect belongs to everest-core 2023.10.0 and is gone in the current release.
Nothing to report, and the finding is downgraded to a fact about that image. Config:
[`config-dc2-ours.yaml`](config-dc2-ours.yaml).

That check took ten minutes and it is the whole reason to pin a digest rather than a tag.

## Correction 2 — what "no -20 counterpart" meant

The harness README listed `Evse15118D20`, `IsoMux` and `config-sil-mcs.yaml` as targets and the roadmap
treated an MCS counterpart as distant. All three were read from `everest-core` HEAD and none of them
existed in the image we were running: everest-core 2023.10.0 predates the -20 charger entirely. In
2025.10 `Evse15118D20`, `IsoMux`, `Iso15118InternetVas` and `config-sil-{dc,ac}-d20.yaml` are all
present. `config-sil-mcs.yaml` still is **not**, so MCS remains without a live counterpart — but for a
different and much narrower reason than "the module does not exist yet".

## What had to change for -20, and one finding

### Their -20 charger has no TCP port until SDP asks

`EvseV2G` binds its TCP server at startup and logs the port, which is why the relay path worked.
**`Evse15118D20` does not**: libiso15118 creates the TCP server in response to an SDP request and picks
the port then. Before that there is nothing to relay to. So the relay recipe needs one step more —
[`sdp-probe.sh`](../../../tools/interop-everest/sdp-probe.sh) sends the SDP request and prints the
endpoint, and the relay is pointed at that.

### A *unicast* SDP request shuts their event loop down

Found the hard way, and the reason `sdp-probe.sh` multicasts:

```
Got SDP request from fe80::18c8:1fff:fe04:b4e8%eth0
[ERRO] Shutdown loop() because of: Read on sdp server socket failed (reason: Resource temporarily unavailable)
```

`EAGAIN` on a non-blocking socket is a normal condition, not a failure, and treating it as fatal ends
their whole ISO 15118-20 loop ~20 ms after it has answered correctly. The **sockets stay bound**, so the
station keeps accepting TCP connections and never answers a byte — from the outside it looks like a dead
peer, not a crash, which is what cost the first hour.

Reproduced twice, with `security = 0x00` and `0x10`, so it is not about the TLS byte. With the same
request sent as multicast to `ff02::1` it does not happen — which is why a real EVCC never trips it, and
why this is a robustness bug rather than something that breaks their demo. Worth reporting; it is the
kind of thing a fuzzer would find and a conformance test would not.

### CP state C has to be held from the plug-in

`sil-car.sh` moved the CP line to state C when `EvseV2G` published `Start_CableCheck`.
**`Evse15118D20` publishes no such variable** — our subscriber saw exactly nothing on
`everest/iso15118_charger/charger/var` for the whole session. So `CP_AT_PLUGIN=1` appends
`draw_power_fixed 0,0` to the plug-in sequence and holds the CP line at 6 V from the start. Harmless for
`EvseV2G` too: the station closes the contactor when it decides to, the car is simply ready earlier.

Without it, the run reached DC_CableCheck and stayed there — and **our ongoing-poll deadline ended the
session**, at the message it was written for:

```
SessionAborted: DC_CableCheck: the station kept answering 'Ongoing' for 60 s (limit 60 s);
                the session ends here.
```

1 099 polls in that first attempt, versus 1 170 in the run that produced the fix. The guard is the only
reason this one stopped. Note the difference in their behaviour between images, too: the 2023 `EvseV2G`
answered `FAILED` when its cable check timed out, while 2025's `Evse15118D20` raises a structured error
(`evse_manager/MREC11CableCheckFault`) and keeps answering `Ongoing`. Both of our corrections from
2026-08-02 were needed to survive one counterparty, one each.

### Their -20 charger will not start without a certificate

Even with `tls_negotiation_strategy: ENFORCE_NO_TLS`, `Evse15118D20` aborts at startup with
`V2G certificate not found` (SIGABRT, and the manager takes everything down). The image ships CA roots
but an empty `client/cso`. Their own test PKI is in the image at
`tests/ocpp_tests/test_sets/everest-aux/certs/`, with a `SECC_LEAF` whose password matches the
`private_key_password: "123456"` in the SIL configs, so the fix is a copy and **no key generation**:

```bash
docker exec everest sh -c "cp -r /ext/source/tests/ocpp_tests/test_sets/everest-aux/certs/* \
                                 /ext/dist/etc/everest/certs/"
```

Also note `supported_scheduled_mode` defaults to **false** on that module while `supported_dynamic_mode`
defaults to true. Our EVCC negotiates Scheduled unless told otherwise, so the config enables both — and
a Dynamic run is now one environment variable away.

## What this proves

Their `Evse15118D20` sits on **libiso15118 + libcbV2G v0.3.1**, and our corpus is generated from cbV2G —
so unlike the runs against the 2023 image, this one is *not* an independent-codec result. What it is:
the first complete **ISO 15118-20** session this project has run against a foreign station, covering
the message set with the most our stack has to say about it, and confirming the route message for
message against our own recorded session. The `AuthorizationSetup` → `ServiceDiscovery` →
`ServiceDetail` → `ServiceSelection` → `ScheduleExchange` sequence in particular has never before been
answered by anything but our own SECC.

`session.trace.json` is checked in: 113 exchanges, strictly alternating, complete.

## Notes on the machine

The 2025.10 image is **19.8 GB extracted** (4.3 GB compressed) and does not fit in colima's default
20 GB VM; the disk was grown to 60 GB. Two more instances of the colima port trap turned up: publishing
a container port that later goes dead cannot be repaired by restarting the container, and a fresh port
does not always help either. The reliable workaround is a relay container that is **created once and
never restarted**; when its upstream has to change, put a second forwarder inside the target container
and leave the published one alone.

## Next

- ✅ **Dynamic control mode** against `Evse15118D20` — done, and *not* one variable: our EVCC could not
  speak Dynamic at all, only our SECC could. See
  [`../2026-08-03-everest-iso20-dc-dynamic/`](../2026-08-03-everest-iso20-dc-dynamic/notes.md).
- **-20 over TLS 1.3** (`ENFORCE_TLS`, `enable_tls_key_logging`), the configuration their own SIL uses.
- **`IsoMux`** — one endpoint answering both -2 and -20, which is the closest thing to a real charger.
- **AC**, both protocols.
- **MCS** stays parked: no `config-sil-mcs.yaml` in 2025.10 either.
- Report the unicast-SDP loop shutdown to EVerest.
