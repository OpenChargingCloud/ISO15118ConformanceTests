# 2026-08-15 — working one checklist gate, and it withdrew a filing and found a defect of ours

No session was run. This is the *audit* shape — the fourth one in this directory, after
[the reports sweep](../2026-08-11-reports-upstream-audit/notes.md) and the two source audits — and it is
the first where working a checklist item **refuted the report it belonged to**.

| | |
|---|---|
| Started as | the last unticked technical box in [`everest-evsev2g-renegotiation-cablecheck`](../../reports/everest-evsev2g-renegotiation-cablecheck.md): *"check the 2014 wording … this project cannot"* |
| Ended as | the report **withdrawn**, `[V2G2-565]`/`[V2G2-582]` written into [`normative-basis.md`](../../normative-basis.md), and a two-sided fix in our own `-2` stack |
| Outcome | **their station was right for four days while we called it broken** |

## What the report claimed, and the four things wrong with it

It said EVerest's `EvseV2G` is wrong to expect `CableCheckReq` after a renegotiation's
`ChargeParameterDiscoveryRes`, and answered `FAILED_SequenceError` to the `PowerDeliveryReq(Start)` that
should restart the charge.

| the claim | what checking it showed |
|---|---|
| *Annex I's own sequence goes straight to PowerDelivery* | **Both of Annex I's diagrams are AC.** They carry `ChargingStatusReq/Res`; the DC loop is `CurrentDemandReq/Res`. They show no CableCheck because AC has none |
| *`[V2G2-842]` is the shall behind the refused message* | It constrains the **content** of the next `PowerDeliveryReq` — that it carries `Start` — not what may precede it |
| *the NOTE at `[V2G2-680]` keeps the contactor closed* | That NOTE is in the Control-Pilot block at `[V2G2-847]`–`[V2G2-849]`. `[V2G2-680]`'s own NOTE is about an EV declining an SECC-initiated renegotiation |
| *Josev works, EVerest does not* | Both Josev renegotiation runs (2026-07-22) were **AC**. A table headed *DC renegotiation restart* was filled with AC evidence |

And the thing the section should have cited: the **SECC state table for DC**, where `Process
ChargeParameterDiscoveryReq` has exactly one successor — *Wait for CableCheckReq*, `[V2G2-565]` and
`[V2G2-582]` — and no renegotiation exception. `EvseV2G`'s comment at the state it lands in cites
`[V2G-582]`. **They implemented the table; we argued with an annex.**

The 2019 *ISO 15118 Manual* — explanatory, never a citation — says the same about the 2014 edition in
plain words: DC renegotiation exchanges `CableCheck` and `PreCharge`, this normally opens the contactor,
and skipping them was an intention for the **second** edition. So the caveat the checklist worried about
(*"the quoted text is the 2022 revision"*) pointed at the right file for the wrong reason: the revision
did not change this, and the mistake was never about the edition.

## What it cost us, and the fix

`Evcc2` renegotiated as `PowerDelivery(Renegotiate)` → `ChargeParameterDiscovery` →
`PowerDelivery(Start)` in **both** modes, and `Secc2` expected exactly that — its own comment said *"after
a renegotiation the cable is already checked … a Josev EVCC does exactly that"*, which was the AC
evidence again, one layer down. **Two ends built from one reading agree with each other**, so no loopback
could see it and no vector recorded it.

Both halves, stack branch `iso2-renegotiation-isolation`:

- **The car** — `Evcc2.RunDcIsolationSequence`, extracted from the opening flow and now called after a
  renegotiation too, because the return path is the same path.
  `Evcc2.RenegotiationSkipsIsolationSequence` reproduces the old car deliberately.
- **The station** — `Secc2.RenegotiationNeedsIsolationSequence` (default **true**): a renegotiated DC
  session goes to `CableCheck`, and a `PowerDeliveryReq` that skipped it is answered
  `FAILED_SequenceError`. `Secc2.IsolationSequences` counts them, since a phase that is never entered
  and a refusal that never happens look identical from outside.

Four tests in
[`Iso2RenegotiationSequenceTests`](../../../ISO15118ConformanceTests.Simulation/StateMachines/Iso2RenegotiationSequenceTests.cs):
both sides conformant (two isolation sequences, session completes), the old car refused by the new
station (`FAILED_SequenceError` at `PowerDeliveryReq` — the answer EVerest gave us, from our own
station), the new car refused by the old station (`CableCheckReq` out of sequence, which is what makes
this a fix of two halves), and AC untouched. **Three of the four fail with both halves put back**,
checked by putting them back.

**No recorded session changed** — checked rather than assumed: no `Vectors/*.trace.json` contains a
renegotiation at all, which is also why nothing in the corpus had ever pinned this.

## What this says about the checklists

The item that did the work was written on 2026-08-11 as *"this project cannot"* — and the check turned
out to need nothing this project did not already have. It sat unticked for four days behind a sentence
that sounded like a dependency and was actually a to-do.

Two habits come out of it, both narrower than *"be careful"*:

- **A sequence diagram has a mode.** Before citing one, name whether it is AC or DC. The three other
  errors above are variations of the same failure to ask *"which case is this an example of?"* —
  including the Josev row, which answered a DC question with AC runs.
- **Prose in a filing is a smell.** This was the only report in the directory reproducing ISO sentences
  rather than paraphrasing what they oblige, which
  [`normative-basis.md`](../../normative-basis.md) forbids for licence reasons — and it was also the only
  one whose argument did not survive contact with the document. Quoting at length reads as rigour and
  was, here, the opposite: nobody re-reads a sentence they have already pasted.

Offline gate: **1 413 green**, four assemblies, exit code 0.

## Next

- **Re-run the renegotiation against EVerest** — and it is now a test of *our* fix rather than of their
  station. One `V2G_INTEROP_RENEG=1` DC session; the expected result is a complete charge where
  2026-08-11 got `FAILED_SequenceError`, which would also close the withdrawn report's last unticked
  line by making it moot.
