# eVDriveFlow's DC_BPT against our SECC

**Matrix cell:** SECC · ISO 15118-20 · BPT · eVDriveFlow

Back to the [interop matrix](../../README.md).

---

Their EV picks service **6** out of our `{2, 6}` — the choice is theirs — and
`DC_ChargeParameterDiscovery` carried a real bidirectional envelope each way, each side's numbers read
by the other's codec: their car **48 kW / 137 A** of discharge against our station's **50 kW / 200 A**,
then a charge loop in `BPT_Dynamic_DC_CLReqControlMode`. No energy reverses — the session ends at their
charge-loop defect first — so this is the negotiation, in full, and not a discharge. One deviation,
recorded: their `ev_dummy_controller` starts at `present_soc = 0` (the GUI's field sets it), and an
empty battery correctly declares zero discharge, so the run patches that one line to 60 in their copy.
Both numbers are on file.
