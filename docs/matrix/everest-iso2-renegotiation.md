# ISO 15118-2 renegotiation against EVerest

**Matrix cell:** EVCC · ISO 15118-2 · Renegotiation · EVerest

Back to the [interop matrix](../../README.md).

---

**Rewritten 2026-08-15: the filing behind this cell is withdrawn, and the defect is ours.** It read
*"half-working rather than absent, which is the finding"* — their station accepts
`PowerDeliveryReq(Renegotiate)` and the fresh `ChargeParameterDiscovery`, then answers the
`PowerDeliveryReq(Start)` that restarts the charge with `FAILED_SequenceError`. That answer is
**correct**. ISO 15118-2's SECC state table for DC goes from `Process ChargeParameterDiscoveryReq` to
*Wait for CableCheckReq* — `[V2G2-565]`, `[V2G2-582]`, the ids `EvseV2G`'s own comment cites — with no
renegotiation exception, so a DC renegotiation returns through `CableCheck` and `PreCharge` and our car
sends neither.
<br>**The argument that got it wrong is worth more than the finding was.** It rested on Annex I, whose
two sequence diagrams carry `ChargingStatusReq/Res` — the **AC** charge loop, which has no CableCheck to
skip. Checking that an informative annex was informative, and never checking which mode it described, is
how a conformant station was written up as defective for four days. Two smaller errors rode along:
`[V2G2-842]` constrains the *content* of the next `PowerDeliveryReq` rather than what precedes it, and
the *"contactor stays closed"* NOTE belongs to the Control-Pilot block at `[V2G2-847]`–`[V2G2-849]`, not
to `[V2G2-680]`.
<br>**It was caught by the report's own unticked gate** — *check the 2014 wording before posting* —
worked before sending rather than after, which is the whole argument for those checklists. Everything
needed to refute it was in the same document it was written from. Withdrawn:
[`everest-evsev2g-renegotiation-cablecheck.md`](docs/reports/everest-evsev2g-renegotiation-cablecheck.md);
reasoning in [`normative-basis.md`](docs/normative-basis.md); the session is still a fact about
2026.02.1 ([run](docs/interop-runs/2026-08-11-everest-iso2-renegotiation/notes.md)).
<br>**Our loopback agreed with itself**, which is why nothing here saw it: `Secc2` accepted the short
sequence our `Evcc2` sent, and both were built from the same reading. The AC half is unaffected and the
Josev cells are AC.
<br>**Both halves fixed the same day.** The car runs the isolation sequence again after a renegotiation;
the station expects it and answers `FAILED_SequenceError` when it is skipped — the answer EVerest gave
us, now from our own station, with `Secc2.IsolationSequences` counting the two CableChecks a DC
renegotiation owes. Four tests, **three fail on the pre-fix code**, checked by putting both halves back.
<br>**Re-run against their station the same evening, with a control three minutes apart**
([`…-iso2-renegotiation-rerun`](docs/interop-runs/2026-08-15-everest-iso2-renegotiation-rerun/notes.md)).
The pre-fix car reproduces `FAILED_SequenceError` on the same binary; the fixed car gets the renegotiated
`CableCheckReq` **accepted**, four `OK`s — the message that was unreachable that morning. The session
then dies four messages later inside their `EvseManager`: its cable check waits for the DC link to fall
below 60 V, which does not happen during a renegotiation, and it raises `MREC11CableCheckFault` →
`Inoperative` — which costs the **following** sessions too, not just this one.
<br>**The deciding arm ran on 2026-08-16, and the second wall is theirs.** With `EVReady = false` in the
isolation sequence the outcome is identical: nothing in that path reads the field. Their
`ChargeProgress = Stop` publishes `current_demand_finished`, which `EvseManager.cpp:865` binds to
`powersupply_DC_off()`; `ChargeProgress = Renegotiate` sets a flag and publishes **nothing**, and
`grep -rn "enegotiat"` over `modules/EVSE/EvseManager/` matches nothing at all — so `cable_check()`, which
*verifies* the safe voltage rather than establishing it, waits for something nobody commanded. **The
forty-ninth filing**, and the third instance of one shape: the layer that is right sits under the layer
that decides ([`everest-evsemanager-renegotiation-supply`](docs/reports/everest-evsemanager-renegotiation-supply.md),
[run](docs/interop-runs/2026-08-16-everest-cablecheck-renegotiation/notes.md)).
