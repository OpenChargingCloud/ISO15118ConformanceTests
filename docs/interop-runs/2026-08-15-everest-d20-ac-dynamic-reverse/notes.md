# 2026-08-15 — Dynamic over AC in reverse, and the invariant that settles a three-day loose end

**Their `PyEvJosev` ran Dynamic control mode over AC against our station — plain and over mutual TLS 1.3
— with a Scheduled control arm on the same rig.** All three sessions are **56 exchanges**, every response
`OK`. What differs is not the length but the *composition*, and that turns out to explain something two
earlier runs had left open.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `PyEvJosev` |
| Config | [`config-ac20-reverse-ours.yaml`](config-ac20-reverse-ours.yaml) — **unchanged** since 2026-08-13 |
| Ours | `EverestInteropTests.TheirPyEvJosev_AgainstOurSecc_RunsToCompletion`, `V2G_INTEROP_MODE=ac`, `V2G_INTEROP_DYNAMIC=1` |
| Outcome | **Dynamic ×2, Scheduled ×1** — all complete, service 1 (AC) in every arm |

## The three arms, and what moves between them

| arm | our offer | transport | exchanges | `PowerDelivery` before the loop | charge loops | mode |
|---|---|---|---:|---:|---:|---|
| plain | Dynamic first | TCP | 56 | **5** | 40 | **Dynamic** |
| tls | Dynamic first | mutual TLS 1.3 | 56 | **4** | 41 | **Dynamic** |
| control | Scheduled first | TCP | 56 | **1** | 44 | **Scheduled** |

**This is the AC charge loop's control-mode answer being exercised by a live peer for the first time.**
`ScheduleExchange` is shared between the power modes, but the charge-loop answer is not:
`Secc20Ac.ClResInKind` is its own `switch`, and a Dynamic AC response carries a **mandatory**
`EVSETargetActivePower` where the Scheduled one does not. A wrong variant there is a wire-type mismatch
their Josev-derived EV does not survive — so 40 and 41 completed Dynamic charge loops are the evidence
that our AC side answers in kind (`[V2G20-1600]`), not just our DC side.

## The invariant, across five AC reverse runs

Yesterday's TLS run recorded an extra `PowerDeliveryReq` before the charge loop, guessed at the
transport, and [the AC_BPT run withdrew that](../2026-08-14-everest-d20-ac-bpt-reverse/notes.md) because
it did not reproduce. With today's arms there are five AC reverse sessions to line up, and they line up
exactly:

| run | mode | `PowerDelivery` before | loops | sum |
|---|---|---:|---:|---:|
| [2026-08-13](../2026-08-13-everest-d20-ac-reverse/notes.md) plain | Scheduled | 1 | 44 | **45** |
| [2026-08-14](../2026-08-14-everest-d20-ac-reverse-tls/notes.md) TLS | Scheduled | 2 | 43 | **45** |
| today, control | Scheduled | 1 | 44 | **45** |
| today, plain | **Dynamic** | 5 | 40 | **45** |
| today, TLS | **Dynamic** | 4 | 41 | **45** |

**The sum is conserved.** The car simulator's `iso_wait_for_stop 20` fixes the *window*, not the exchange
count, so every message spent getting started is one not spent charging.

So the extra `PowerDeliveryReq` was never the transport, and it is not Dynamic-exclusive either —
Scheduled shows one or two. It is **readiness polling**, the behaviour our station's `PowerOn` phase
self-loops for and documents in as many words: *"a real EV repeats `PowerDeliveryReq(Start)`
(`EVProcessing=Ongoing`) until it begins the charge loop"*. What Dynamic does is make it systematically
larger — four or five rather than one — which is consistent with the station steering the operating point
and the car waiting to be told it. **That last clause is the inference; the table is the measurement.**

Withdrawing the TLS guess was right, and it was right for a better reason than "it did not reproduce":
the variable it was attributed to was never the one moving.

## Reproduce

```bash
V2G_INTEROP_LISTEN=15118 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac \
V2G_INTEROP_DYNAMIC=1 V2G_INTEROP_CHARGELOOP=20000 \
  dotnet test -c Release --artifacts-path ~/wsl-artifacts \
              --filter FullyQualifiedName~EverestInteropTests.TheirPyEvJosev &
until ss -lnt | grep -q ':15118 '; do sleep 1; done

~/everest/dist/bin/manager --config ~/everest/configs-ours/config-ac20-reverse-ours.yaml
```

Drop `V2G_INTEROP_DYNAMIC=1` for the control; add
`V2G_INTEROP_TLS_SERVER=~/everest/tlsac/secc.p12:123456 V2G_INTEROP_TLS_REQUIRE_CLIENT=1` for the TLS arm
after [`tls-pki-setup.sh`](../../../tools/interop-everest/tls-pki-setup.sh), and restore afterwards.
`V2G_INTEROP_CHARGELOOP=20000` is there for the reason
[the forty-seventh filing](../../reports/josev-iso20-evcc-charge-loop-pacing.md) exists, and **a run that
used it is not a passing charge-loop conformance result.**

## Artifacts

[`plain/`](plain/), [`tls/`](tls/) and [`control/`](control/) — flow, frames, both octet streams, both
sides' logs. No `trace.json`: their EV signs the `AuthorizationReq` with a key that is theirs, so
`SessionTrace.Build` refuses the recording rather than substitute the signature and verify nothing.

**No code changed for this run** — the config is three days old and the control-mode property landed
yesterday. The offline gate is unchanged at 1 405.

## Next

- **`AC_BPT` in Dynamic**, which would put `BPT_Dynamic_AC_CLResControlModeType` — the fourth and last
  AC charge-loop variant — in front of a live peer. One environment variable on top of the AC_BPT config.
- A reverse **`-2`** run over TLS 1.2, still one environment variable.
