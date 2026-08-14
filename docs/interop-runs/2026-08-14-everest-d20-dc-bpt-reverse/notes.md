# 2026-08-14 — DC_BPT in reverse, and a rule handed back to the stack it came from

**Their `PyEvJosev` selected energy transfer service 6 — DC_BPT — out of our station's `{ 2, 6 }`
catalogue and ran the whole DC sequence: CableCheck, PreCharge, the charge loop, WeldingDetection, to
`SessionStop`.** Plain and again over mutual TLS 1.3, every response `OK`.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `PyEvJosev` |
| Config | [`config-dc20bpt-reverse-ours.yaml`](config-dc20bpt-reverse-ours.yaml) — the MCS reverse config with `supported_d20_energy_services: MCS` → **`DC_BPT`**, `connector_type: cMCS` removed, and TLS enabled |
| Ours | `EverestInteropTests.TheirPyEvJosev_AgainstOurSecc_RunsToCompletion`, `V2G_INTEROP_MODE=dc`, `V2G_INTEROP_BPT_FIRST=1` |
| Outcome | **service 6 both arms** — 53 exchanges plain, 52 over TLS, all `OK` |

**Nothing of ours changed for this run.** The fixture guard, the TLS wiring, the SECC credential and the
`V2G_INTEROP_BPT_FIRST` semantics all came from the four runs before it; this one is one line in their
config and a config file of ours that already existed in another shape. That is worth recording as the
first run this week that cost no code at all.

## The sequence their EV drove

```
 0  SupportedAppProtocol      OK_SuccessfulNegotiation
 1  SessionSetup              OK_NewSessionEstablished
 2–3 AuthorizationSetup, Authorization        OK
 4–6 ServiceDiscovery, ServiceDetail, ServiceSelection   OK      ← service 6 selected here
 7  DC_ChargeParameterDiscovery               OK      ← and this OK is the proof, below
 8  ScheduleExchange                          OK
 9  DC_CableCheck                             OK
10–11 DC_PreCharge ×2                         OK
12  PowerDelivery(Start)                      OK
13–45 DC_ChargeLoop ×33  (×30 over TLS)       OK
46  PowerDelivery(Stop)                       OK
47–51 DC_WeldingDetection ×5                  OK
52  SessionStop                               OK
```

CableCheck, PreCharge and WeldingDetection are the three phases an AC session never has, so this is a
wider path through our SECC than either AC reverse run — driven by somebody else's car, in the
bidirectional service.

## What makes it a DC_BPT result, and where the rule came from

The same two things as the [AC_BPT run](../2026-08-14-everest-d20-ac-bpt-reverse/notes.md) an hour
earlier: the fixture **asserts** the negotiated id rather than logging it, and the `OK` at exchange 7 is
only reachable if their `DC_ChargeParameterDiscoveryReq` carried a `BPT_DC_CPDReqEnergyTransferModeType` —

```csharp
var responseCode = bidirectionalRequest == BidirectionalServiceSelected
                       ? Dc20.ResponseCode.OK
                       : Dc20.ResponseCode.FAILED_WrongChargeParameter;
```

**And that check is theirs before it was ours.** Our station only has it because everest-core refused
*our* session with `FAILED_WrongChargeParameter` on 2026-08-05 — an EV that negotiated a bidirectional
service and then sent charge-only parameters
([`…-mcs-bpt`](../2026-08-05-everest-mcs-bpt/notes.md)) — and they were right: ISO 15118-20 carries the
direction in the polymorphic type, so the selected service binds every message after it. The rule is
written into `Secc20Dc` with that run cited beside it. Today their EV was on the receiving end of it and
passed, which is the tidiest way this project has closed a loop yet: **a counterparty's refusal became
our conformance check, and the check then validated the counterparty.**

## Both transports

| arm | transport | exchanges | charge loops | service |
|---|---|---:|---:|---|
| plain | TCP | 53 | 33 | **6 (DC_BPT)** |
| tls | **mutual TLS 1.3**, `TLS_AES_256_GCM_SHA384` | 52 | 30 | **6 (DC_BPT)** |

The TLS arm again presented their CPO leaf from a regenerated PKI and read back their vehicle
certificate, `CN=WMIV1234567890ABCDEX, O=Pionix, DC=OEM` — the third reverse TLS session in two hours,
which is why the label now reads *"their car … we presented …"* rather than calling a vehicle certificate
a server. Pristine PKI restored, root `88:F8:C2:D5…` verified back in place.

The loop counts differ between the arms (33 / 30) for the reason established this afternoon: the car
simulator's `iso_wait_for_stop 15` fixes the window, not the exchange count, so anything that costs time
— the handshake, their pacing — comes out of the loops.

## Reproduce

```bash
sed -e 's/supported_d20_energy_services: MCS$/supported_d20_energy_services: DC_BPT/' \
    -e '/connector_type: cMCS$/d' \
    config-mcs-reverse-ours.yaml > config-dc20bpt-reverse-ours.yaml
```

```bash
V2G_INTEROP_LISTEN=15118 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc \
V2G_INTEROP_BPT_FIRST=1 V2G_INTEROP_CHARGELOOP=20000 \
  dotnet test -c Release --artifacts-path ~/wsl-artifacts \
              --filter FullyQualifiedName~EverestInteropTests.TheirPyEvJosev &
until ss -lnt | grep -q ':15118 '; do sleep 1; done

~/everest/dist/bin/manager --config ~/everest/configs-ours/config-dc20bpt-reverse-ours.yaml
```

Add `V2G_INTEROP_TLS_SERVER=~/everest/tlsac/secc.p12:123456 V2G_INTEROP_TLS_REQUIRE_CLIENT=1` for the
second arm, after [`tls-pki-setup.sh`](../../../tools/interop-everest/tls-pki-setup.sh) and the SECC
PKCS#12 it does not export. `V2G_INTEROP_CHARGELOOP=20000` is there for the reason
[the forty-seventh filing](../../reports/josev-iso20-evcc-charge-loop-pacing.md) exists, and **a run that
used it is not a passing charge-loop conformance result.**

## Artifacts

[`plain/`](plain/) and [`tls/`](tls/) — flow, frames, both octet streams, both sides' logs. No
`trace.json`: their EV signs the `AuthorizationReq` with a key that is theirs, so `SessionTrace.Build`
refuses the recording rather than substitute the signature and verify nothing.

## Next

- **`←SECC` Dynamic control mode** is the obvious remaining reverse variable — their `PyEvJosev` adopts
  whatever our station offers, so `V2G_INTEROP_DYNAMIC=1` puts our Dynamic parameter sets in front of it.
- A reverse **`-2`** run over TLS 1.2, still one environment variable.
