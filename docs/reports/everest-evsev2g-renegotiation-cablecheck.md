# ~~Draft report to EVerest (`EvseV2G`) — a DC renegotiation cannot restart~~ — **WITHDRAWN**

> ## Withdrawn 2026-08-15, before it was sent, and it was wrong
>
> **Their station is right and our car is not.** After a DC `ChargeParameterDiscovery`, ISO 15118-2's own
> SECC state table for DC goes to *"Wait for CableCheckReq"* — `[V2G2-565]`, `[V2G2-582]`, the very
> requirement ids `EvseV2G`'s comment cites — with no renegotiation shortcut anywhere in it. A DC
> renegotiation returns through `CableCheck` and `PreCharge`; `EvseV2G` implements that, and answering
> `FAILED_SequenceError` to a `PowerDeliveryReq` that skipped both is the correct answer.
>
> **What went wrong here is worth more than the finding was.** The argument below leans on Annex I —
> whose two sequence diagrams are **AC**: they show `ChargingStatusReq/Res`, the AC charge loop, and AC
> has no CableCheck to skip. An AC example was read as a general one, and a DC station was measured
> against it. Three further legs, each independently sufficient, are in
> [`normative-basis.md`](../normative-basis.md).
>
> **The checklist caught it.** The unticked item was *"the `-2` document caveat … check the 2014 wording
> before posting — this project cannot"*, marked in [`sending-order.md`](sending-order.md) as **a gate,
> not a footnote**. Working that gate is what refuted the report: not new evidence, just reading the
> thing it said should be read. The third withdrawal in this directory and the first caused by us
> misreading the standard rather than by upstream moving.
>
> **It leaves a defect of ours.** `Evcc2` renegotiates as `PowerDelivery(Renegotiate)` →
> `ChargeParameterDiscovery` → `PowerDelivery(Start)` in **both** modes, and our own SECC accepts it, so
> no loopback could see it. Now in *Ours to fix* in [`open-work.md`](../open-work.md).
>
> **The session below is still a fact about 2026.02.1** and the file is kept for it, like the two other
> withdrawn reports. Everything after this box is as it was written on 2026-08-11, wrong argument
> included, because a withdrawal that hides what it withdrew teaches nobody anything.

Status: ~~draft, not sent~~ **withdrawn — do not send**. Measured on the wire; the measurement stands and
the conclusion does not.

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

## ~~What the standard asks~~ — every line of this section is wrong; see the box at the top

> Kept verbatim as the record of the mistake. Read with the corrections beside it, because each is a
> different way of getting a citation wrong and only the first one is obvious in hindsight.

- **`[V2G2-842]`** — *If [V2G2-841] applies the EVCC shall set the parameter ChargeProgress to "Start" in
  the next following message PowerDeliveryReq to apply the negotiated charging limits after a
  renegotiation.* The EV's next `PowerDeliveryReq` is the one being refused.
  <br>**Wrong reading of a correctly quoted sentence.** It constrains the *content* of the next
  `PowerDeliveryReq` — that it carries `Start` — and says nothing about which messages may precede it.
  `CableCheck` and `PreCharge` in between satisfy it exactly as well.
- **Annex I, "Message sequencing for renegotiation"**, states the sequence in the diagram's own note:
  `PowerDeliveryReq(ChargeProgress = Renegotiate)` *"followed by an exchange of
  ChargeParameterDiscoveryReq/Res and PowerDeliveryReq/Res message-pairs and then re-enter the charging
  loop."* No CableCheck, no PreCharge. **Annex I is informative** — said here rather than left to be
  discovered — but it is the document's own worked example of this mechanism.
  <br>**Both of Annex I's diagrams are AC.** They carry `ChargingStatusReq/Res`, which is the AC charge
  loop; the DC loop is `CurrentDemandReq/Res`. There is no CableCheck in them because AC has none. The
  report checked whether the annex was normative and never checked which mode it was about.
- **The NOTE beside `[V2G2-680]`** is the physical reason the annex reads that way: *"In case of
  renegotiation the contactor stays closed to allow charging based on the existing charging limits during
  renegotiation."* CableCheck is the isolation test performed **before** the contactor closes. Requiring
  it after a renegotiation asks for a pre-energisation check while energised.
  <br>**Misattributed.** That sentence is NOTE 1 in the Control-Pilot block, beside `[V2G2-847]` to
  `[V2G2-849]`, not at `[V2G2-680]` — whose own NOTE is about an EV declining an SECC-initiated
  renegotiation. And in DC the 2019 *ISO 15118 Manual* describes the contactor as normally **opening**
  for exactly this sequence, which is why the second edition wanted to change it.
- **What the section should have cited**, and what settles it: the SECC state table for **DC**, where
  `Process ChargeParameterDiscoveryReq` is followed by *Wait for CableCheckReq* — `[V2G2-565]`,
  `[V2G2-582]` — with no renegotiation exception. `EvseV2G`'s comment at the state it lands in cites
  `[V2G-582]`.

## ~~Suggested fix~~ — do not apply this to their tree

> It would make their station accept a sequence the DC state table does not have, i.e. break a
> conformant implementation to accommodate a non-conformant car. The change belongs in **our** EVCC.

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

## ~~Context~~ — and the table is a fourth mistake

| stack | DC renegotiation restart |
|---|---|
| Josev (SwitchEV) | **works** — `EV→` and `←SECC`, `[V2G2-841]`, ✅ in this project's matrix since 2026-07 |
| **EVerest `EvseV2G`** | trigger and re-discovery accepted, restart refused `FAILED_SequenceError` |
| *(ours)* | both directions, loopback-tested |

> **The Josev row is AC.** Both 2026-07-22 runs drove `ChargingStatusReq/Res`
> ([run](../interop-runs/2026-07-22-renegotiation/notes.md)), so nothing there says what their DC station
> does with a renegotiation that skips CableCheck — and a column headed *DC renegotiation restart* said it
> did. **Our own row is the same shape one step worse**: our SECC accepts our EVCC's sequence because both
> were built from the same wrong reading, which is what a loopback cannot catch and what an unrelated
> counterparty caught in one session.

---

## Before sending

- [x] **Reproduce it yourself** — one session against your stock `config-sil-dc.yaml`, an EV that sends
      `PowerDeliveryReq(Renegotiate)` mid-charge. No PKI, no TLS, no configuration change.
- [x] **Check it is not dead code behind a flag.** The renegotiation trigger is handled and the
      SECC-initiated direction is implemented too; this is the restart path only.
- [x] **Separate the normative from the informative.** `[V2G2-842]` is the *shall*; Annex I is the
      worked example; the contactor NOTE is the reason. The report leads with that split rather than
      presenting the annex as binding.
- [x] **The `-2` document caveat — worked 2026-08-15, and it ended the report.** The concern was the
      wrong *revision*; the actual defect was the wrong *mode*. Annex I is AC, the DC state table has no
      renegotiation shortcut in either edition, `[V2G2-842]` constrains a field rather than an order, and
      the contactor NOTE belongs to a different requirement. The 2019 *ISO 15118 Manual* — explanatory,
      never a citation — says plainly that DC renegotiation in the 2014 edition exchanges CableCheck and
      PreCharge, and names skipping them as an intention for the second edition. Written up in
      [`normative-basis.md`](../normative-basis.md).
      <br>**The gate was the finding.** Everything needed to refute this was in the same document the
      report was written from; nothing arrived later. A checklist item that says *"this project cannot"*
      is worth re-testing before it is believed — it was written when the documents were newer here than
      the habit of reading them.
- [ ] ~~**Say what was not measured.** Whether the station accepts the sequence when CableCheck and
      PreCharge *are* re-sent is read from the state table, not observed.~~ **Moot** — that arm is now a
      test of *our* fix rather than of their station, and it belongs with the `Ours to fix` entry.
- [x] **Re-read the citations against the tree before posting — done 2026-08-11.** All four verified
      against the built 2026.02.1 source in the sweep over all 189 `file:line` references in this
      directory, and the state assignment is **unchanged on everest-core `main`** (`ebcd36d`): a DC
      renegotiation still lands in `WAIT_FOR_CABLECHECK`.
- [ ] **Ask, do not assert, about the intended sequence.** If `WAIT_FOR_CABLECHECK` after renegotiation
      is deliberate — an isolation re-test policy rather than an oversight — that is a position worth
      hearing, and it would belong in the standard's discussion rather than in a patch.
- [ ] **Post under your own name, in your own words.**
