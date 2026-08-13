# 2026-08-13 — EVerest's EV against our AC station, and a number neither side wanted

**The first ISO 15118-20 AC session this project has run in the reverse direction — in any protocol.**
Their `PyEvJosev` discovered our SECC over SDP, negotiated `-20:AC`, and charged: 56 exchanges, every
response `OK`, through 44 `AC_ChargeLoop` pairs to `SessionStop`.

It also produced two things nobody was looking for: **a defect of ours that had been silently narrowing
every reverse run ever made**, and **a measurement of their EV that our own conformant timer refuses.**

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `PyEvJosev` wrapping `EVerest/ext-switchev-iso15118` |
| Config | [`config-ac20-reverse-ours.yaml`](config-ac20-reverse-ours.yaml) — the forward AC config with the two device lines swapped, `auto_exec: true`, `supported_d20_energy_services: AC` |
| Ours | `EverestInteropTests.TheirPyEvJosev_AgainstOurSecc_RunsToCompletion`, listening on 15118 and advertising over SDP, **inside WSL** because SDP is multicast |
| Outcome | **56 exchanges, all `OK`** with our charge-loop timer relaxed — and **2 of 2 aborts** with it at its conformant value |

## The defect of ours: the reverse fixture could only ever offer DC

The first attempt got as far as SAP and our own station refused their EV:

```
SAP: the EVCC offered none of urn:iso:std:iso:15118:-20:DC
```

— with `V2G_INTEROP_MODE=ac` set. `SapHandshake.RunSeccSideAsync` takes `PowerMode mode = PowerMode.Dc`
as a **defaulted** argument, and the fixture called it without passing the mode it had already computed
two lines earlier. So the reverse fixture announced a DC-only `-20` catalogue no matter what the
environment said, and had done since it was written.

Nobody noticed because **every reverse `-20` run before today was DC**. It took an AC EV on the other
end to ask for something else. The forward fixture never had it: the EVCC side takes the mode as a
required argument.

That is the same shape this project keeps finding in counterparties and now in itself — *a value we
already held, defaulted instead of passed*. One argument fixes it, and the comment beside it says why
it is there.

## The measurement: 532 ms, against a 500 ms requirement

With the fixture fixed, the session negotiated `-20:AC` and reached the charge loop — and our station
ended it:

```
SECC sequence timeout: EV silent for > 500 ms in the charge loop
```

That is **our** implementation of Tables 216/217 — `V2G_SECC_Sequence_Timeout` is 0,5 s after a
charge-loop response, obliged by `[V2G20-1500]` and `[V2G20-1502]` — added on 2026-08-11 and, until
today, never met by a peer. **2 of 2** runs stopped at the first `AC_ChargeLoop` pair.

A run that stops on our own timer has measured *us*, so the timer was taken out of the way
(`V2G_INTEROP_CHARGELOOP=20000`, a new knob, the mirror of `V2G_INTEROP_ONGOING` and added for the same
reason). Their EV then charged happily:

| | strict — the conformant 500 ms | relaxed — 20 s |
|---|---|---|
| `AC_ChargeLoop` pairs | **1** | **44** |
| ended by | our sequence timeout, 2 of 2 | their EV, `SessionStop` |
| every response code | `OK` | `OK` |

**Their pacing, from their own log:** `EVSE IEC Event PowerOn` at 20:36:20.769, `Stop in Charging` at
20:36:44.176 — **23,407 s for 44 charge loops, ≈ 532 ms each.** Just over the 500 ms a conformant
station is allowed to wait, which is exactly why the strict runs die on the *first* loop rather than
somewhere in the middle.

### Why this is not written up as a report yet

Because the requirement has one more question in it, and
[`normative-basis.md`](../../normative-basis.md) is explicit that this project decides such things
before citing them. `[V2G20-1500]` and `[V2G20-1502]` oblige the **SECC** to time out at 0,5 s. Whether
there is a matching obligation on the **EVCC** to send within it — rather than the practical
consequence that an EV pacing at 532 ms cannot charge on a conformant station — is a table this run did
not read. The measurement stands either way; the filing waits on that.

**What makes it worth the trouble** is the pair it forms. Their `-20` station flattens this same
override to a single 60 s constant, which is [the twenty-second filing](../../reports/everest-d20-sequence-timeout.md),
and their fork's SECC has the 0,5 s constants sitting unused, which is
[the SwitchEV one](../../reports/josev-iso20-charge-loop-timeout.md). **A station that waits 60 s never
discovers that its own EV takes 532 ms.** The two halves hide each other, and only a conformant third
party sees either.

## Reproduce

Runs inside WSL — SDP is multicast on an interface, and a station advertising from Windows is not on
their `eth0`.

```bash
# ours first: the readiness signal is the listening socket, not a log line.
# NUnit buffers TestContext.Out until the run ends, so "Waiting for their PyEvJosev" appears afterwards.
V2G_INTEROP_LISTEN=15118 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac \
  dotnet test -c Release --artifacts-path ~/wsl-artifacts \
              --filter FullyQualifiedName~EverestInteropTests.TheirPyEvJosev &
until ss -lnt | grep -q ':15118 '; do sleep 1; done

~/everest/dist/bin/manager --config ~/everest/configs-ours/config-ac20-reverse-ours.yaml
```

Add `V2G_INTEROP_CHARGELOOP=20000` for the measured run. **A run that used it is not a passing
charge-loop conformance result** and the knob's own documentation says so.

## Artifacts

[`strict/`](strict/) and [`measure/`](measure/), each with the flow, the frames, both sides' logs. The
measured run has no `trace.json`: `SessionTrace.Build` refuses a recording with signed requests and no
signing key rather than substitute the recorded signature and verify nothing.

## Next

- **Read the EVCC side of Tables 216/217** and decide whether the 532 ms is a filing.
- The reverse direction has still never run **over TLS**, in any mode.
- `AC_BPT` in reverse: their config would need `supported_d20_energy_services: AC_BPT`, and our
  `Secc20Ac` already offers the service.
