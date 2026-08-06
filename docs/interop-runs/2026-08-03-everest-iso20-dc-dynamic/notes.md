# 2026-08-03 — EVerest `Evse15118D20`, ISO 15118-20 DC, **Dynamic control mode**

**A complete Dynamic-mode charge — and it needed a feature, not an environment variable.**

The previous run's "Next" list opened with *"Dynamic control mode — one variable, and their module
supports it by default"*. That was wrong in a way worth writing down: `V2G_INTEROP_DYNAMIC` has always
driven our **SECC**. Our **EVCC** could not speak Dynamic at all. Every Dynamic session in
`docs/interop-runs/` had Josev's car on the other side
([`2026-07-22-iso20-dynamic-sdp`](../2026-07-22-iso20-dynamic-sdp/notes.md)), so the mode was
live-validated in exactly one direction while the roadmap said "Scheduled **and** Dynamic" without
qualification. `Evcc20Base` had two mentions of the word, both `null`.

So this run is a feature and its first live outing.

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2025.10.0**, `Evse15118D20` on libiso15118 v0.9.0 |
| Image | `ghcr.io/everest/everest-demo/manager@sha256:5b0136c31a9f4be985df313b5b1d2e90464d00b203f63613199657f2697ce097` |
| Ours | `Vanaheimr.V2G.Exi` @ `23b8779` + the Dynamic EVCC |
| Session | ISO 15118-20 DC, **ControlMode = 2**, plain TCP, [`../2026-08-03-everest-iso20-dc-full-charge/config-d20-ours.yaml`](../2026-08-03-everest-iso20-dc-full-charge/config-d20-ours.yaml) |
| Outcome | **complete charge, 102/102 `OK`, route identical to our own recorded -20 session** |
| Artifacts | [`flow.md`](flow.md), [`frames.log`](frames.log), [`session.trace.json`](session.trace.json), [`their-charger.log`](their-charger.log) |

## Their station says which mode it is

The one line that makes this a Dynamic result rather than a hopeful one, from their own log — the same
message from the Scheduled run above it for contrast:

```
22:52:05  Selected DC service parameters: control mode: Scheduled, mobility needs mode: ProvidedByEvcc
00:12:26  Selected DC service parameters: control mode: Dynamic,   mobility needs mode: ProvidedByEvcc
```

And the substantive difference shows up where it should — in the power supply. Scheduled mode ran at the
setpoint **our** car named:

```
EVSE IEC DC power supply set: 400.00V/120.00A, requested was 400.00V/120.00A.
```

Dynamic mode ran at an operating point **their station chose** from the envelope we declared
(`EVMaximumVoltage` 500 V, `EVMaximumChargeCurrent` 125 A):

```
EVSE IEC DC power supply set: 500.00V/125.00A, requested was 500.00V/125.00A.
```

That is the whole point of the mode in two log lines: in Scheduled the car plans and the station
follows, in Dynamic the car states needs and limits and the station steers.

Run twice, as the harness requires: 102 and 119 exchanges, both complete, no crash.

## What had to be built

Dynamic is a property of the *session*, not of one message, so a flag that reaches most of the way
produces a session that still completes against a lenient station and is wrong on the wire. Four places
had to agree, and each is asserted separately in `Evcc20DynamicModeTests` on the messages the station
actually received:

| Phase | Scheduled | Dynamic |
|---|---|---|
| `ServiceSelectionReq` | the parameter set with `ControlMode = 1` | `ControlMode = 2` |
| `ScheduleExchangeReq` | `Scheduled_SEReqControlMode` | `Dynamic_SEReqControlMode` — departure time + the **mandatory** energy triple |
| `PowerDeliveryReq(Start)` | `Scheduled_EVPPTControlMode`, pointing at a schedule tuple | `Dynamic_EVPPTControlMode`, which is empty — there is no tuple to point at |
| `DC_ChargeLoopReq` | `Scheduled_…`: target current and voltage | `Dynamic_…`: energy needs, power and voltage limits |

Answering in kind is [V2G20-1600]; asking in kind is the same rule read from the other end, and it is
what the EVCC now does. The energy triple being mandatory in the Dynamic arm and optional in the
Scheduled one is the schema making the same point: a station can only steer if it knows the target.

Two smaller things came with it. `VerifyPriceSchedule` now also looks at
`Dynamic_SEResControlMode.AbsolutePriceSchedule` — Dynamic mode has no schedule tuples to hang a price
schedule off, so the tariff-verification path would otherwise have been silently dead in this mode. And
an EVCC asked for Dynamic against a station that offers only Scheduled now **refuses by name** rather
than falling back: the parameter set it selects is what the station answers in kind against for the rest
of the session, so a silent fallback would negotiate one mode and then ask in the other.

`Secc20Ac` lost its `sealed`, to match `Secc20Dc`, which never had it. The asymmetry was an accident and
it meant a test could watch what a DC station received but not an AC one.

## Not yet ported

The Kotlin and Swift EVCCs stay Scheduled-only. Both earlier EVCC corrections — the response-code
handling and the ongoing-poll deadline — went to all three languages the same day, and this one does
not, so it is stated rather than left to be discovered: **C# only, for now.** The ports drive the trace
corpus, and no corpus entry is a Dynamic session, so nothing there fails; what is missing is the
capability, not a passing test.

## Next

- **Port Dynamic to Kotlin and Swift**, for parity with the two corrections.
- **-20 over TLS 1.3** — the configuration their own SIL uses, and the one the app's
  `libs/EVSimulatorApp/docs/pki-model.md` pins -20 to.
- **`IsoMux`**, one endpoint answering both protocols.
- **AC**, where our Dynamic arm is implemented but has never met a station.
