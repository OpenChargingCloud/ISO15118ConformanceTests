# 2026-08-03 — EVerest, AC in both protocols

**A complete ISO 15118-2 AC session, a defect of ours found in seven messages, and a -20 AC session that
gets nine phases deep and then cannot close a contactor.**

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2025.10.0** — `EvseV2G` (-2 AC) and `Evse15118D20` (-20 AC) |
| Image | `ghcr.io/everest/everest-demo/manager@sha256:5b0136c31a9f4be985df313b5b1d2e90464d00b203f63613199657f2697ce097` |
| Ours | `Vanaheimr.V2G.Exi` @ `39a4b26` + the fix below |
| Configs | [`config-ac2-ours.yaml`](config-ac2-ours.yaml) (their `config-sil.yaml`), [`config-ac20-ours.yaml`](config-ac20-ours.yaml) (their `config-sil-ac-d20.yaml`) |
| Outcome | **-2 AC: complete, 13/13 `OK`, run twice.** **-20 AC: nine phases, then `FAILED_ContactorError`.** |
| Artifacts | [`flow.iso2-ac.md`](flow.iso2-ac.md), [`flow.iso20-ac.md`](flow.iso20-ac.md), frames and their logs for both |

## The finding — ours: the energy transfer mode was never read

The first -2 AC attempt died seven messages in:

```
SessionAborted: the station answered ChargeParameterDiscoveryResType
                with FAILED_WrongEnergyTransferMode; the session ends here.
```

Their station was right. Our `Evcc2` **hard-coded** the mode it asked for:

```csharp
mode == PowerMode.Dc
    ? new ChargeParameterDiscoveryReqType(…, EnergyTransferMode.DC_extended,     …)
    : new ChargeParameterDiscoveryReqType(…, EnergyTransferMode.AC_three_phase_core, …)
```

`ServiceDiscoveryRes` carries the station's `SupportedEnergyTransferMode` list, five messages earlier, and
we never looked at it. EVerest's AC SIL configuration is single-phase (`ac_nominal_voltage: 230`, no
three-phase capability), so it advertises `AC_single_phase_core` and refuses a three-phase request.

**Why nothing here could have found it.** Our own SECC advertises exactly one mode per power mode —
`AC_three_phase_core` for AC — which is the mode our EVCC names. Every counterparty so far did the same.
A constant and a list agree until they don't, and no loopback can tell them apart while both sides are
ours. Third instance of the same shape this week, after the unread response code and the unbounded poll:
**a value taken from our own assumption where the protocol supplies one.**

Fixed the same day: `SelectEnergyTransferMode` picks from what was offered, best-first within our power
mode (three-phase over single-phase, extended over core), and a station that offers nothing in our mode
is refused with the offer named — *"the station offers no AC energy transfer mode (offered:
DC_extended)"* is the line that turns "it refused" into "it is a DC charger". Three tests in
`Evcc2EnergyTransferModeTests`, including that refusal and the negative that three-phase is still chosen
when both are on offer.

Note also what made this legible: the abort came with the station's own response code, in the message it
happened in, because of the response-code handling added on 2026-08-01. Before that fix this would have
been a session that ran on and failed later, somewhere less obvious. **Third time that one has paid.**

## ISO 15118-2 AC: complete

With the mode read from their offer, `Selected energy transfer mode: AC_single_phase_core` on their side
and a full session on ours — 13 exchanges, every response `OK`, route matching our own recorded AC
session exactly, run twice:

```
SupportedAppProtocol → SessionSetup → ServiceDiscovery → PaymentServiceSelection
→ Authorization ×2 → ChargeParameterDiscovery → PowerDelivery(Start)
→ ChargingStatus ×3 → PowerDelivery(Stop) → SessionStop
```

One difference from the DC runs worth recording: their AC charger does **not** use the 5 % HLC duty
cycle. `ac_hlc_use_5percent: false`, and the log reads *"AC mode, HLC enabled(X1), matching already
started. We are in X1 so we can go directly to nominal PWM"* → `Set PWM On (53.3%)`. So the IEC layer is
already charging while the HLC session runs alongside, which is exactly how AC ISO 15118 is meant to
work and is a different shape from every DC run here.

## ISO 15118-20 AC: nine phases, then the contactor

The -20 AC session negotiates further than any AC session this project has run — and stops at the first
message that needs hardware:

```
0–1   SupportedAppProtocol, SessionSetup            OK
2–4   AuthorizationSetup, Authorization ×2          OK
5–7   ServiceDiscovery, ServiceDetail, ServiceSelection   OK
8     AC_ChargeParameterDiscovery                   OK
9     ScheduleExchange                              OK
10    PowerDelivery(Start)          → FAILED_ContactorError
```

Their side, repeating until it gives up:

```
iso15118_charge  :: Waiting for contactor is closed
evse_manager:Ev  ::            CAR ISO AC HLC Close contactor
```

Three car-side commands were tried through `sil-car.sh`'s interface — `draw_power_fixed 0,0` (what makes
the DC cable check pass), `draw_power_fixed 16,1`, and `draw_power_regulated 16,3`, their own value —
and none of them get the contactor closed. Their own SIL sequence for this configuration is

```
sleep 3;iso_wait_slac_matched;iso_start_v2g_session AC;iso_wait_pwr_ready;iso_draw_power_regulated 16,3;…
```

and `iso_wait_pwr_ready` waits on **their EV module's own callback** — `PyEvJosev` being told by the SECC
that power is ready. A foreign EV cannot produce it, and unlike the DC cable check there is no CP-line
lever that substitutes for it: in AC the IEC layer has already closed its own contactor and gone to
`Charging`, while the *HLC* contactor request is a separate signal their `EvseManager` never sees
satisfied.

So the honest statement is narrower than "their -20 AC is broken": **their -20 AC SIL expects its own EV
module in the loop, in a way the DC one does not.** Getting past it means driving their EV-side
hardware simulation rather than only the car's CP line — which is a bigger piece of work than everything
`sil-car.sh` does today, and the natural next step for this counterparty.

## Reproduce

Setup as in the [-20 DC run](../2026-08-03-everest-iso20-dc-full-charge/notes.md), with these configs.
Two device lines differ from their shipped files in each: the charger moves to `eth0`, their
`PyEvJosev` to `lo`.

```bash
V2G_INTEROP_SECC=127.0.0.1:15141 V2G_INTEROP_PROTOCOL=2  V2G_INTEROP_MODE=ac  dotnet test …
V2G_INTEROP_SECC=127.0.0.1:15141 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac  dotnet test …
```

`EvseV2G` binds 61341 at startup as always; `Evse15118D20` still needs [`sdp-probe.sh`](../../../tools/interop-everest/sdp-probe.sh)
first and then answers on 50000.

## Next

- **Drive their EV-side hardware simulation**, which is what the -20 AC contactor is waiting on.
- **AC BPT**, in both protocols — their `config-sil-ac-d20.yaml` has `supported_iso_ac_bpt: true`, and
  our -20 AC BPT arm has never met a station.
- Port the energy-transfer-mode selection to the Kotlin and Swift EVCCs, together with Dynamic.
