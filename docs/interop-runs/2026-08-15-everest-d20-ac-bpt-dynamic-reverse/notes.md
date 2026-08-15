# 2026-08-15 — AC_BPT in Dynamic: the fourth variant, and the second explanation this week to be wrong

**Their `PyEvJosev` selected AC_BPT and ran it in Dynamic control mode — plain and over mutual TLS 1.3 —
with a BPT/Scheduled control arm.** That completes the four AC charge-loop control-mode variants against
a live peer. It also **refutes an inference published this morning**, from the run immediately before it.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `PyEvJosev` |
| Config | [`config-ac20bpt-reverse-ours.yaml`](config-ac20bpt-reverse-ours.yaml) — **unchanged** from yesterday |
| Ours | `V2G_INTEROP_MODE=ac`, `V2G_INTEROP_BPT_FIRST=1`, `V2G_INTEROP_DYNAMIC=1` |
| Outcome | **service 5 + Dynamic ×2**, **service 5 + Scheduled ×1** — all 56 exchanges, every response `OK` |

Both fixture assertions fire in the two Dynamic arms: the negotiated service must be bidirectional, and
the control mode must be the one the run claims. Neither is a restatement of configuration — both are
read off what their car sent.

## The four AC charge-loop variants have now all met a live peer

`Secc20Ac.ClResInKind` answers strictly in kind (`[V2G20-1600]`), and it has four arms. Until today three
of them had been exercised by somebody else's car:

| variant | first live session |
|---|---|
| `Scheduled_AC_CLResControlModeType` | [2026-08-13](../2026-08-13-everest-d20-ac-reverse/notes.md), the first reverse AC run |
| `BPT_Scheduled_AC_CLResControlModeType` | [2026-08-14](../2026-08-14-everest-d20-ac-bpt-reverse/notes.md), AC_BPT |
| `Dynamic_AC_CLResControlModeType` | [2026-08-15](../2026-08-15-everest-d20-ac-dynamic-reverse/notes.md), AC Dynamic |
| **`BPT_Dynamic_AC_CLResControlModeType`** | **this run** |

A wrong variant is a wire-type mismatch their Josev-derived EV does not survive, so 44 completed
charge loops in each Dynamic arm is the evidence.

## The correction: "Dynamic makes the polling larger" is wrong

This morning's AC Dynamic run found five sessions obeying `PowerDelivery before the loop + charge
loops = 45`, and offered a reading for *why the poll count moves*: Dynamic makes it systematically larger,
because the station steers and the car waits to be told. Two Dynamic sessions showed 5 and 4 against 1 in
the Scheduled arms.

**Today's Dynamic arms show 1.**

| run | service | mode | transport | PD before | loops | sum |
|---|---|---|---|---:|---:|---:|
| [08-13](../2026-08-13-everest-d20-ac-reverse/notes.md) | AC | Scheduled | TCP | 1 | 44 | 45 |
| [08-14](../2026-08-14-everest-d20-ac-reverse-tls/notes.md) | AC | Scheduled | TLS | 2 | 43 | 45 |
| [08-14](../2026-08-14-everest-d20-ac-bpt-reverse/notes.md) | AC_BPT | Scheduled | TCP | 1 | 44 | 45 |
| [08-14](../2026-08-14-everest-d20-ac-bpt-reverse/notes.md) | AC_BPT | Scheduled | TLS | 1 | 44 | 45 |
| [08-15](../2026-08-15-everest-d20-ac-dynamic-reverse/notes.md) | AC | Scheduled | TCP | 1 | 44 | 45 |
| [08-15](../2026-08-15-everest-d20-ac-dynamic-reverse/notes.md) | AC | **Dynamic** | TCP | **5** | 40 | 45 |
| [08-15](../2026-08-15-everest-d20-ac-dynamic-reverse/notes.md) | AC | **Dynamic** | TLS | **4** | 41 | 45 |
| this run | AC_BPT | **Dynamic** | TCP | **1** | 44 | 45 |
| this run | AC_BPT | **Dynamic** | TLS | **1** | 44 | 45 |
| this run | AC_BPT | Scheduled | TCP | 1 | 44 | 45 |

**What survives is the invariant. What does not survive is every explanation offered for the variation.**
Nine of ten sessions sit at 1 or 2; the two outliers share *AC and Dynamic together*, and adding BPT to
Dynamic puts it back to 1. So it is not the transport (withdrawn 08-14), not the control mode (withdrawn
here), and not the service either — a single pair of runs is not a mechanism, and with ten sessions the
honest statement is that **the poll count varies and nothing measured so far predicts it.**

The `PowerOn` phase is self-looping by design — *"a real EV repeats `PowerDeliveryReq(Start)`
(`EVProcessing=Ongoing`) until it begins the charge loop"* — so a varying count is expected behaviour and
not a defect on either side. What was wrong was attaching a cause to it twice.

**Two inferences refuted in two days, from the same three-line observation.** Worth keeping visible: both
were hedged when written, both were refuted by the next run rather than by re-reading, and in both cases
the measurement underneath was never in doubt. The rule this suggests is narrower than "hedge more" —
*an explanation offered for a difference between two runs is a hypothesis about the next ten.*

## Reproduce

```bash
V2G_INTEROP_LISTEN=15118 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac \
V2G_INTEROP_BPT_FIRST=1 V2G_INTEROP_DYNAMIC=1 V2G_INTEROP_CHARGELOOP=20000 \
  dotnet test -c Release --artifacts-path ~/wsl-artifacts \
              --filter FullyQualifiedName~EverestInteropTests.TheirPyEvJosev &
until ss -lnt | grep -q ':15118 '; do sleep 1; done

~/everest/dist/bin/manager --config ~/everest/configs-ours/config-ac20bpt-reverse-ours.yaml
```

Drop `V2G_INTEROP_DYNAMIC=1` for the control; add the two TLS variables after
[`tls-pki-setup.sh`](../../../tools/interop-everest/tls-pki-setup.sh) and restore afterwards.
`V2G_INTEROP_CHARGELOOP=20000` is there for the reason
[the forty-seventh filing](../../reports/josev-iso20-evcc-charge-loop-pacing.md) exists, and **a run that
used it is not a passing charge-loop conformance result.**

## Artifacts

[`plain/`](plain/), [`tls/`](tls/) and [`control/`](control/) — flow, frames, both octet streams, both
sides' logs. No `trace.json`: their EV signs the `AuthorizationReq` with a key that is theirs.

**No code changed for this run.** Offline gate unchanged at 1 405; pristine PKI restored and verified.

## Next

- A reverse **`-2`** run over TLS 1.2 — the last item that is one environment variable.
- If the poll count is ever worth explaining, the instrument is a timestamped log on **their** side, not
  another arm of ours: what is being counted is how long their car takes to be ready, and no frame log
  here records time.
