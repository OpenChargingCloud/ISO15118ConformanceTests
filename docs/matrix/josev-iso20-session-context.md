# Josev's empty -20 session context

**Matrix cell:** EVCC · ISO 15118-20 · Pause / Resume · Josev — and SECC · Renegotiation · Josev

Back to the [interop matrix](../../README.md).

---

Our side is complete for both; **theirs is the bound**. Josev's -20 states never fill the session
context, so a -20 resume degrades to a new session; and its EVCC drops the link after a real
`SessionStopReq(ServiceRenegotiation)` [V2G20-1477] that our SECC answers without ending the session — the
renegotiation branch of their `SessionStop` state is the one transition in that file that never builds the
message the next state needs, and their own framework refuses it. Filed:
[`josev-iso20-renegotiation.md`](docs/reports/josev-iso20-renegotiation.md).
