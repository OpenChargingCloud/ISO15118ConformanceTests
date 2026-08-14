# 2026-08-14 — AC_BPT in reverse: their EV picks our bidirectional service, twice over

**Their `PyEvJosev` selected energy transfer service 5 — AC_BPT — out of our station's `{ 1, 5 }`
catalogue and charged, plain and again over mutual TLS 1.3.** 56 exchanges each, every response `OK`,
44 `AC_ChargeLoop` pairs to `SessionStop`. One config line on their side; our `Secc20Ac` already
advertised the service.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `PyEvJosev` |
| Config | [`config-ac20bpt-reverse-ours.yaml`](config-ac20bpt-reverse-ours.yaml) — yesterday's reverse config with `supported_d20_energy_services: AC` → **`AC_BPT`**, and nothing else |
| Ours | `EverestInteropTests.TheirPyEvJosev_AgainstOurSecc_RunsToCompletion`, `V2G_INTEROP_BPT_FIRST=1` |
| Outcome | **service 5 both arms**, 56 exchanges, 44 charge loops, all `OK` |

## What makes this an AC_BPT result rather than an AC one

Two things, and neither is the label on the run:

**The id is asserted, not logged.** The reverse fixture guarded only MCS until today: an EV configured for
AC_BPT that quietly took service **1** out of our catalogue would charge to `SessionStop` exactly as
happily, and nothing on the wire afterwards distinguishes that run from this one. The guard is now stated
over `EnergyTransferService.IsBidirectional`, so 5, 6 and 9 are all held to it — the same shape the
forward fixture has carried since the MCS runs, from the other end.

**Their charge-parameter request really carried the discharge half**, and our own station is the witness.
`Secc20Ac.HandleChargeParameterDiscovery` answers

```csharp
var responseCode = bidirectionalRequest == BidirectionalServiceSelected
                       ? Ac20.ResponseCode.OK
                       : Ac20.ResponseCode.FAILED_WrongChargeParameter;
```

so the `OK` at exchange 7 is only reachable if their `AC_ChargeParameterDiscoveryReq` carried a
`BPT_AC_CPDReqEnergyTransferModeType`. A car that selected service 5 and then sent charge-only parameters
would have been refused by construction. Our answer advertised `EVSEMaximumDischargePower` 22 kW beside
the charge limit.

## Both transports

| arm | transport | exchanges | charge loops | service |
|---|---|---:|---:|---|
| plain | TCP | 56 | 44 | **5 (AC_BPT)** |
| tls | **mutual TLS 1.3**, `TLS_AES_256_GCM_SHA384` | 56 | 44 | **5 (AC_BPT)** |

The TLS arm presented their own CPO leaf from a regenerated PKI and read back their vehicle certificate —
`CN=WMIV1234567890ABCDEX, O=Pionix, DC=OEM` — exactly as
[yesterday's reverse TLS run](../2026-08-14-everest-d20-ac-reverse-tls/notes.md) did, which is now a
second instance rather than a single one. Pristine PKI restored afterwards, root `88:F8:C2:D5…` verified
back in place.

It also confirms the corrected label from that run reads properly in practice:

```
TLS: Tls13, TLS_AES_256_GCM_SHA384, their car DC=OEM, …, we presented DC=CPO, …, CN=SECCCert
```

## A correction to yesterday's run, from today's

The reverse TLS notes recorded an extra `PowerDeliveryReq` before the first charge loop — one in the
relaxed arm, four in the strict one — present in both TLS runs and neither plain one, and said so as
*"suggestive and not conclusive"*.

**It does not reproduce.** This TLS arm has exactly one `PowerDeliveryReq` at exchange 9 and 44 charge
loops, the same shape as the plain arm beside it and as the plain run of 2026-08-13. So the extra message
is **not** a property of the transport; the likeliest reading now is readiness timing on their side, and
the loop count follows it — 43 loops in the run that spent an exchange on it, 44 in the three that did
not. The hedge in that note was worth writing, and this is what it was for.

## Reproduce

```bash
sed 's/supported_d20_energy_services: AC$/supported_d20_energy_services: AC_BPT/' \
    config-ac20-reverse-ours.yaml > config-ac20bpt-reverse-ours.yaml
```

```bash
# ours first; add the two TLS variables for the second arm, per the reverse-TLS run
V2G_INTEROP_LISTEN=15118 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac \
V2G_INTEROP_BPT_FIRST=1 V2G_INTEROP_CHARGELOOP=20000 \
  dotnet test -c Release --artifacts-path ~/wsl-artifacts \
              --filter FullyQualifiedName~EverestInteropTests.TheirPyEvJosev &
until ss -lnt | grep -q ':15118 '; do sleep 1; done

~/everest/dist/bin/manager --config ~/everest/configs-ours/config-ac20bpt-reverse-ours.yaml
```

`V2G_INTEROP_CHARGELOOP=20000` is there for the reason
[the forty-seventh filing](../../reports/josev-iso20-evcc-charge-loop-pacing.md) exists — their EV paces
the charge loop above the 0,5 s a conformant station may wait — and **a run that used it is not a passing
charge-loop conformance result.** In a reverse BPT run `V2G_INTEROP_BPT_FIRST=1` means *assert the peer
chose a bidirectional service*; its EVCC-side ranking half has nothing to rank here.

## Artifacts

[`plain/`](plain/) and [`tls/`](tls/) — flow, frames, both octet streams, both sides' logs. No
`trace.json` in either: their EV signs the `AuthorizationReq` with a key that is theirs, so
`SessionTrace.Build` refuses the recording rather than substitute the signature and verify nothing.

## Next

- **`DC_BPT` in reverse** is now the same one-line change on their side (`supported_d20_energy_services:
  DC_BPT`) against `Secc20Dc`'s `{ 2, 6 }`, and the guard added today covers it unchanged.
- A reverse **`-2`** run over TLS 1.2, still one environment variable.
