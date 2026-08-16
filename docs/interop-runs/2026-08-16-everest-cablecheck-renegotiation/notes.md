# 2026-08-16 — the `EVReady` arm: it changes nothing, and their source says why

[Last night's re-run](../2026-08-15-everest-iso2-renegotiation-rerun/notes.md) left one question and
refused to file anything until it was answered: their station accepts the renegotiated `CableCheckReq`
and then fails its own cable check because the DC link never drops below 60 V — **and our car was
declaring `EVReady = true` at that moment.** Whose job was it to de-energise?

| | |
|---|---|
| Counterparty | everest-core **2026.02.1** (`b61bb12`), `EvseV2G` + `EvseManager`, `config-dc2-ours.yaml` |
| Arm | the same renegotiation with `V2G_INTEROP_ISOLATION_NOT_READY=1` — `EVReady = false` in the isolation sequence |
| Outcome | **identical failure.** Their supply does not ramp down, and the flag never reaches the code that decides |

## The measurement

```
[39] ChargeParameterDiscoveryReq  →  ChargeParameterDiscoveryRes (OK)
[40] CableCheckReq                →  CableCheckRes (OK)          ← EVReady = false from here
[41] CableCheckReq                →  CableCheckRes (OK)
[42] CableCheckReq                →  CableCheckRes (OK)
[43] CableCheckReq                →  CableCheckRes (FAILED)
```

Their own lines, word for word what the `EVReady = true` arm produced:

```
evse_manager :: EVSE ISO Start cable check...
evse_manager :: Cancel cable check wait below voltage
evse_manager :: Error raised: evse_manager/MREC11CableCheckFault, sub_type: Self test failed,
                message: Voltage did not drop below 60V within timeout.
evse_manager :: Error raised: evse_manager/Inoperative
```

**So the EV cannot influence it, and that is the answer.** `EVReady` is a status field in
`DC_EVStatus`; nothing in their cable-check path reads it. The instrument was worth building anyway —
without it, *"maybe our car should have said it was not ready"* stays a plausible excuse forever.

## Where it actually comes from, in their source

Two functions, and the second is the one that surprised me:

```cpp
// EvseManager.cpp:2028-2038, cable_check()
session_log.evse(true, "Start cable check...");
// Verify output is below 60V initially
if (not wait_powersupply_DC_below_voltage(CABLECHECK_SAFE_VOLTAGE)) { … fail_cable_check(oss.str()); return; }
```

```cpp
// EvseManager.cpp:2444 ff., wait_powersupply_DC_below_voltage()
//   waits and measures; powersupply_DC_off() only in the cancel and no-measurement branches
```

**It verifies the safe voltage; it never establishes it.** On the way into a session that is correct and
free — nothing has been switched on yet. On the *return* path of a renegotiation, the supply is still
serving the charge loop, and nobody has told it to stop.

And nobody does, because of one asymmetry three lines wide:

```cpp
// EvseV2G/iso_server.cpp:1588-1598, handle_iso_power_delivery()
case iso2_chargeProgressType_Stop:
    …
    } else {                                              // DC
        conn->ctx->p_charger->publish_current_demand_finished(nullptr);   // → EvseManager: powersupply_DC_off()
        conn->ctx->p_charger->publish_dc_open_contactor(nullptr);
    }
    break;

case iso2_chargeProgressType_Renegotiate:
    conn->ctx->session.renegotiation_required = true;     // …and nothing else
    break;
```

`ChargeProgress = Stop` tells the manager to switch the DC supply off — `EvseManager.cpp:865` binds
`current_demand_finished` to exactly that. `ChargeProgress = Renegotiate` sets a flag their V2G module
reads and publishes **nothing**, so the manager never learns that energy transfer is pausing. `grep -rn
"enegotiat" modules/EVSE/EvseManager/` returns **nothing at all**: the module that owns the power supply
has no notion of renegotiation.

Their two halves are each self-consistent and the seam between them is empty. **`EvseV2G` correctly routes
a DC renegotiation back through `CableCheck` — that is the state table, and it is what withdrew our
filing yesterday — while `EvseManager` is never told to make that `CableCheck` possible.**

## What this is, and what it is not

**It is the third instance here of one shape**: the layer that is right sits under the layer that
decides — [`everest-d20-eim-rejection`](../../reports/everest-d20-eim-rejection.md) (a verdict never
forwarded) and [`everest-d20-ac-contactor-edge`](../../reports/everest-d20-ac-contactor-edge.md) (a state
never re-reported) are the other two, both in the same seam between `EvseManager` and an HLC module.

**It is not the withdrawn report.** That one said their station should not ask for `CableCheck` after a
renegotiation. It should, and it does. This says the station cannot *satisfy* the cable check it asks
for, because the supply is still on — and the consequence is worse than a refused message: the station
raises `MREC11CableCheckFault`, goes `Inoperative`, and stops serving **subsequent** sessions until it is
restarted.

**Both arms are the evidence**, and so is the positive control inside each of them: the same station, the
same session, ran `CableCheck` → `PreCharge` → `PowerDelivery` → `CurrentDemand` perfectly on the way in.
Nothing here is a claim that their cable check is broken in general.

Filed as [`everest-evsemanager-renegotiation-supply`](../../reports/everest-evsemanager-renegotiation-supply.md),
the forty-ninth.

## Artifacts

[`frames.evready-false.log`](frames.evready-false.log) — our side of this arm.
[`their-station.evready-false.log`](their-station.evready-false.log) — theirs.
[`their-source.txt`](their-source.txt) — the four excerpts the finding rests on, from the tree that ran.

Offline gate: **1 413 green**, four assemblies, exit code 0.

## Reproduce

```bash
V2G_INTEROP_SECC='[fe80::…%eth0]:61341' V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RENEGOTIATE=1 V2G_INTEROP_ISOLATION_NOT_READY=1 \
  dotnet test -c Release --filter "FullyQualifiedName~EverestInteropTests.OurEvcc_AgainstTheirEvseV2G"
```

Restart their manager first: the previous arm left it `Inoperative`.

## Next

- **Nothing measured.** What is left is the filing's own checklist, and one question for them rather than
  for us: whether `Renegotiate` should publish `current_demand_finished` (their existing hook), a new
  pause event, or whether `cable_check()` should establish the safe voltage instead of verifying it. The
  report asks rather than asserts, because all three are defensible and only they know what else those
  events mean downstream.
