# Draft report to EVerest (`EvseV2G`) — a DC renegotiation cannot restart: the station expects CableCheck where the standard's own sequence goes straight to PowerDelivery

Status: **draft, not sent**, and **measured on the wire**. Post it under your own name; see
*Before sending*.

Evidence in this repository:
[`2026-08-11-everest-iso2-renegotiation`](../interop-runs/2026-08-11-everest-iso2-renegotiation/notes.md).

---

**Title:** `handle_iso_charge_parameter_discovery` sends a DC session to `WAIT_FOR_CABLECHECK` after a
renegotiation, so `PowerDeliveryReq(Start)` — the message `[V2G2-842]` asks for next — is answered
`FAILED_SequenceError`

**Version:** everest-core **2026.02.1**, `modules/EVSE/EvseV2G/`, `config-sil-dc.yaml` shape,
unmodified.

## Observed

An EV charging normally initiates one renegotiation:

```
PowerDeliveryReq(Start)        →  PowerDeliveryRes (OK)
CurrentDemandReq               →  CurrentDemandRes (OK)
PowerDeliveryReq(Renegotiate)  →  PowerDeliveryRes (OK)                    ← accepted
ChargeParameterDiscoveryReq    →  ChargeParameterDiscoveryRes (OK)         ← accepted
PowerDeliveryReq(Start)        →  PowerDeliveryRes (FAILED_SequenceError)  ← refused, session over
```

The renegotiation itself is implemented and works: `iso_server.cpp:1596` sets
`session.renegotiation_required` on `chargeProgressType_Renegotiate`, and the SECC-initiated direction
has its `EVSENotification` reset logic. **Only the restart is unreachable.**

## Where it comes from

`handle_iso_charge_parameter_discovery`, choosing the next expected message:

```cpp
// iso_server.cpp:1490 ff.
if (conn->ctx->is_dc_charger == true) {
    if (conn->ctx->evse_v2g_data.no_energy_pause == NoEnergyPauseStatus::BeforeCableCheck) {
        conn->ctx->state = (int)iso_dc_state_id::WAIT_FOR_PRECHARGE_POWERDELIVERY; // IEC61851-1:2023 CC.3.5.2
    } else {
        conn->ctx->state = (iso2_EVSEProcessingType_Finished == res->EVSEProcessing)
                               ? (int)iso_dc_state_id::WAIT_FOR_CABLECHECK          // [V2G-582], [V2G-688]
                               : (int)iso_dc_state_id::WAIT_FOR_CHARGEPARAMETERDISCOVERY;
    }
```

and the state it lands in (`iso_server.hpp`):

```cpp
/* [V2G-582], [V2G-621] Expected req msg after CableCheckRes or ChargeParameterDiscoveryRes */
{"Waiting for CableCheckReq, SessionStopReq", 1 << V2G_CABLE_CHECK_MSG | 1 << V2G_SESSION_STOP_MSG},
```

`PowerDelivery` is not in that mask, so `iso_validate_state` returns `FAILED_SequenceError`.

**The branch that would have worked already exists**: `WAIT_FOR_PRECHARGE_POWERDELIVERY` admits
`PRE_CHARGE`, `POWER_DELIVERY` and `SESSION_STOP`. It is simply gated on `no_energy_pause` — a pause
scenario — and a renegotiation takes the `else`.

## What the standard asks

- **`[V2G2-842]`** — *If [V2G2-841] applies the EVCC shall set the parameter ChargeProgress to "Start" in
  the next following message PowerDeliveryReq to apply the negotiated charging limits after a
  renegotiation.* The EV's next `PowerDeliveryReq` is the one being refused.
- **Annex I, "Message sequencing for renegotiation"**, states the sequence in the diagram's own note:
  `PowerDeliveryReq(ChargeProgress = Renegotiate)` *"followed by an exchange of
  ChargeParameterDiscoveryReq/Res and PowerDeliveryReq/Res message-pairs and then re-enter the charging
  loop."* No CableCheck, no PreCharge. **Annex I is informative** — said here rather than left to be
  discovered — but it is the document's own worked example of this mechanism.
- **The NOTE beside `[V2G2-680]`** is the physical reason the annex reads that way: *"In case of
  renegotiation the contactor stays closed to allow charging based on the existing charging limits during
  renegotiation."* CableCheck is the isolation test performed **before** the contactor closes. Requiring
  it after a renegotiation asks for a pre-energisation check while energised.

## Suggested fix

Let a renegotiation reach the state a pause already reaches. Something like:

```cpp
} else if (conn->ctx->session.renegotiation_required) {
    conn->ctx->state = (int)iso_dc_state_id::WAIT_FOR_PRECHARGE_POWERDELIVERY;  // [V2G2-842], Annex I
} else {
    conn->ctx->state = (Finished == res->EVSEProcessing) ? WAIT_FOR_CABLECHECK : …;
}
```

`WAIT_FOR_PRECHARGE_POWERDELIVERY` accepts both `PreChargeReq` and `PowerDeliveryReq`, so it admits the
annex's sequence **and** still tolerates an EV that chooses to pre-charge again — which is the
conservative shape and needs no new state. Whether `renegotiation_required` is the right flag to read
there, or whether you would rather track it in the session explicitly, is yours to choose.

## Context

| stack | DC renegotiation restart |
|---|---|
| Josev (SwitchEV) | **works** — `EV→` and `←SECC`, `[V2G2-841]`, ✅ in this project's matrix since 2026-07 |
| **EVerest `EvseV2G`** | trigger and re-discovery accepted, restart refused `FAILED_SequenceError` |
| *(ours)* | both directions, loopback-tested |

---

## Before sending

- [x] **Reproduce it yourself** — one session against your stock `config-sil-dc.yaml`, an EV that sends
      `PowerDeliveryReq(Renegotiate)` mid-charge. No PKI, no TLS, no configuration change.
- [x] **Check it is not dead code behind a flag.** The renegotiation trigger is handled and the
      SECC-initiated direction is implemented too; this is the restart path only.
- [x] **Separate the normative from the informative.** `[V2G2-842]` is the *shall*; Annex I is the
      worked example; the contactor NOTE is the reason. The report leads with that split rather than
      presenting the annex as binding.
- [ ] **The `-2` document caveat.** The text quoted here is the **2022 DIS** revision; your target and
      ours is ISO 15118-2:**2014**. Check the 2014 wording of `[V2G2-842]`, Annex I and the `[V2G2-680]`
      NOTE before posting — this project cannot, and a quotation from the wrong revision is the fastest
      way to have a real finding dismissed.
- [ ] **Say what was not measured.** Whether the station accepts the sequence when CableCheck and
      PreCharge *are* re-sent is read from the state table, not observed.
- [ ] **Re-read the citations against the tree before posting.** Four `file:line` references.
- [ ] **Ask, do not assert, about the intended sequence.** If `WAIT_FOR_CABLECHECK` after renegotiation
      is deliberate — an isolation re-test policy rather than an oversight — that is a position worth
      hearing, and it would belong in the standard's discussion rather than in a patch.
- [ ] **Post under your own name, in your own words.**
