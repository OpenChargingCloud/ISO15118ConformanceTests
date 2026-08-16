# eVDriveFlow's EV in our Dynamic charge loop

**Matrix cell:** SECC · ISO 15118-20 · DC, Dynamic · eVDriveFlow

Back to the [interop matrix](../../README.md).

---

It used to read *"their EV quits at Authorization"*, recorded as an open question after two runs could
not move it. Reading their state machine settled it on 2026-08-06: their EV arms a "press Enter to stop"
listener on `sys.stdin` unconditionally, EOF returns immediately, and `process_reaction` then replaces the
message the state machine built with `SessionStopReq` in the first state that permits it — which is the
authorization one. The rig had started it with `docker exec -d`. **With stdin held open and nothing else
changed, 4 exchanges became 15**, through ScheduleExchange, CableCheck, PreCharge ×3, PowerDelivery and
into DC_ChargeLoop. It stops there on a defect of theirs: `hasattr` used as a presence test on an
`Optional[int]` copies our legally omitted `TargetSOC` over their own default, and `None * int` ends it.
Their EV also selects the **BPT** service on the way, so that cell is reachable now too.
