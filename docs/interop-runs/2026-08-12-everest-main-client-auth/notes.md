# 2026-08-12 — §1 re-measured on `main`: the EV still decides whether the EV is authenticated

`client-auth` §1 was measured on **`2026.02.1`** on 2026-08-10. Between then and `main` the mechanism
moved out of `libiso15118` into `lib/everest/tls` behind a flag called `verify_client_on_tls13`, which
is the reason the report was re-argued yesterday — so the behaviour needed re-measuring where the code
now lives. **It is unchanged.**

everest-core `main` **`ebcd36d`**, `Evse15118D20`, `config-sil-dc-d20.yaml` as shipped. **Stock PKI** —
the second V2G root from the [chain-selection run](../2026-08-12-everest-main-chain-selection/notes.md)
was stashed first, so nothing unusual is installed and no one can point at it.

## The two arms

Same station, same certificate, fresh session each. One variable: the TLS version offered. **No client
certificate in either arm** ([`arms.log`](arms.log)).

| Arm | client offers | their log | result |
|---|---|---|---|
| **1** (control) | TLS 1.3 only | *"Client supports TLS1.3: Change verify mode…"* — counter 0 → 1 | **refused**: `tlsv13 alert certificate required` (alert 116), station closes the connection |
| **2** | TLS 1.2 only | *(no such line)* — counter 1 → 1 | **`New, TLSv1.2, Cipher is ECDHE-ECDSA-AES128-SHA256`**, handshake complete, no alert |

Counting the *"Change verify mode"* lines rather than eyeballing them is what makes arm 2 a negative
result rather than an absence of evidence: the same station, minutes apart, fired the upgrade once and
then not at all.

So on `main`, exactly as on the release: **the station demands a vehicle certificate from an EV that
offers TLS 1.3 and asks nothing of one that does not.** The EV picks which it gets, in its first flight.

## What was *not* re-measured, and an artifact that misled us

§1 has a second half — *what the anonymous connection is good for* — measured on `2026.02.1` by
replaying two frames from our own `-20` DC corpus and getting `OK_SuccessfulNegotiation` and a session
id back. **That half was not re-run**, after an attempt that has to be recorded because it produced a
plausible wrong answer.

The attempt replayed [`replay.consequence.hex`](../2026-08-10-everest-d20-client-auth/replay.consequence.hex)
from the original run. The station warned `Expected SupportedAppProtocol` and the exchange went nowhere,
which for a few minutes looked like *"the anonymous path is closed on `main`"*.

**It was not. That file holds the station's two *responses*, not our two requests.** The original notes'
own hexdump says so plainly once read in the right direction — `01fe8001 00000004 80400040` is the
`OK_SuccessfulNegotiation` that came *back*. The replay therefore sent the station its own answers, and
`Expected SupportedAppProtocol` is the correct reaction to that. Nothing was learned about `main`.

Two things worth keeping out of it:

- **The run kept what came back and not what went out.** The request frames appear in the notes only as
  elided hexdumps (`8000f3ab…d222`), so they cannot be reconstructed from the artifacts. For a
  replay-based finding the *input* is the part that has to be stored — it is the half a reader cannot
  reproduce without us. `arms.log` here stores both directions for that reason.
- **`replay.consequence.hex` is a misleading name** — it reads as *the replay*, and it is *the
  consequence*. Labelled in that run's notes now rather than renamed, so existing links keep working.

The consequence half therefore still rests on the `2026.02.1` measurement, and `client-auth` §1 says so.
Re-running it needs the request frames regenerated from our own EVCC, which is a bigger job than this
run and was not the question asked.

## Also seen

The `-20` stack accepts and completes the anonymous TLS 1.2 connection at the transport layer —
*"Accepted connection on port 50000"*, *"Handshake complete!"* — which is the part `[V2G20-2356]`
speaks to and the reason arm 2 matters beyond the missing `CertificateRequest`.

## Reproduce

```bash
bash tools/interop-everest/client-auth-arm.sh "control" tls1_3
bash tools/interop-everest/client-auth-arm.sh "arm 2"   tls1_2
```
