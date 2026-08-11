# 2026-08-11 — ISO 15118-2 renegotiation against EVerest: they take it, then refuse the restart

everest-core **2026.02.1**, `modules/EVSE/EvseV2G/`, DC over plain TCP, config `config-dc2-ours.yaml`
unchanged. `EV→`: our EVCC initiates one renegotiation mid-charge (`V2G_INTEROP_RENEGOTIATE=1`, added
here — the fourth capability this month our car had and no interop run could ask for).

## What happened

```
[36] PowerDeliveryReq(Start)        →  PowerDeliveryRes (OK)
[37] CurrentDemandReq               →  CurrentDemandRes (OK)
[38] PowerDeliveryReq(Renegotiate)  →  PowerDeliveryRes (OK)                    ← accepted
[39] ChargeParameterDiscoveryReq    →  ChargeParameterDiscoveryRes (OK)         ← accepted
[40] PowerDeliveryReq(Start)        →  PowerDeliveryRes (FAILED_SequenceError)  ← refused
```

So the mechanism is **implemented and half-working**: they accept the renegotiation trigger and the
re-discovery, and then refuse the message that restarts the charge. Their `-2` renegotiation is not a
stub — `iso_server.cpp:1596` sets `session.renegotiation_required` on
`chargeProgressType_Renegotiate`, and the `EVSENotification` side even has the `ReNegotiation → None`
reset. This is a sequencing defect inside a real implementation.

## Why, from their state table

`handle_iso_charge_parameter_discovery` picks the next expected message like this
(`iso_server.cpp:1490-1259`):

```cpp
if (conn->ctx->is_dc_charger == true) {
    if (conn->ctx->evse_v2g_data.no_energy_pause == NoEnergyPauseStatus::BeforeCableCheck) {
        conn->ctx->state = WAIT_FOR_PRECHARGE_POWERDELIVERY;   // IEC61851-1:2023 CC.3.5.2
    } else {
        conn->ctx->state = (Finished == res->EVSEProcessing)
                               ? WAIT_FOR_CABLECHECK            // [V2G-582], [V2G-688]
                               : WAIT_FOR_CHARGEPARAMETERDISCOVERY;
    }
```

There **is** a branch whose state would have accepted our `PowerDelivery(Start)` —
`WAIT_FOR_PRECHARGE_POWERDELIVERY` allows `PRE_CHARGE`, `POWER_DELIVERY` and `SESSION_STOP` — but it is
gated on `no_energy_pause`, a **pause** scenario, not renegotiation. Renegotiation takes the `else`, and
`WAIT_FOR_CABLECHECK` admits `CABLE_CHECK` and `SESSION_STOP` **only** (`iso_server.hpp:120-122`). So
after a renegotiation their DC station requires the EV to run CableCheck and PreCharge again before it
may restart the charge.

The attribution needs no second session: the state mask *is* the mechanism, and the observed
`FAILED_SequenceError` is what that mask produces.

## What the standard says, and it is not what they expect

**Annex I is called "Message sequencing for renegotiation"** and its own sequence diagram carries the
answer as an explanatory note:

> If EVCC plans to perform a renegotiation, it shall start by sending PowerDeliveryReq with
> 'ChargeProgress' parameter set to 'Renegotiate' followed by an exchange of
> ChargeParameterDiscoveryReq/Res and PowerDeliveryReq/Res message-pairs and then re-enter the charging
> loop.

`PowerDelivery(Renegotiate)` → `ChargeParameterDiscovery` → `PowerDelivery` → charging loop. **No
CableCheck and no PreCharge.** That is exactly the sequence our EVCC sent and their station refused.

**And there is a physical reason the annex reads that way**, stated as a NOTE beside `[V2G2-680]`:

> In case of renegotiation the contactor stays closed to allow charging based on the existing charging
> limits during renegotiation.

CableCheck is the isolation test performed **before** the contactor closes. Requiring it after a
renegotiation asks the EV to re-run a pre-energisation check while energised — which contradicts the
mechanism's own premise, and is why the annex has no room for it.

The normative side is consistent: `[V2G2-842]` — *the EVCC shall set ChargeProgress to "Start" in the
**next following** message PowerDeliveryReq to apply the negotiated charging limits after a
renegotiation.* Our car does exactly that.

### What is honestly weaker here

- **Annex I is informative**, not normative. It is the standard's own worked example of the mechanism
  rather than a `shall`, and the report says so first. The load-bearing argument is the pair
  `[V2G2-842]` plus the contactor NOTE; the annex shows what they describe.
- **The `-2` document caveat applies** and matters more than usual: the text to hand is the **2022 DIS**
  revision while this stack targets ISO 15118-2:**2014**. Whether Annex I and that NOTE read the same in
  2014 is not something this project can check. See [`normative-basis.md`](../../normative-basis.md).
- Nothing here proves their station *would* accept the sequence with CableCheck and PreCharge
  re-inserted. It is what their state table says, not what was measured — our EVCC has no mode that
  sends it, and building one to accommodate behaviour we believe is wrong would be the wrong way round.

## Filed

[`everest-evsev2g-renegotiation-cablecheck.md`](../../reports/everest-evsev2g-renegotiation-cablecheck.md)
— the fortieth.

## Reproduce

```
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-dc2-ours.yaml
CP_AT_PLUGIN=1 bash ~/everest/sil-car.sh          # wait for "Set PWM On"
socat TCP-LISTEN:15118,bind=0.0.0.0,reuseaddr,fork "TCP6:[<link-local>%eth0]:<tcp-port>"
```

```
V2G_INTEROP_SECC=127.0.0.1:15118 V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
V2G_INTEROP_RENEGOTIATE=1 \
dotnet test -c Release --filter "FullyQualifiedName~EverestInteropTests.OurEvcc_AgainstTheirEvseV2G"
```

[`frames.log`](frames.log) is our side of the wire; [`station.log`](station.log) their own lines.
