# tux-evse's captured Audi session against our SECC

**Matrix cell:** SECC · ISO 15118-2 · DC, EIM · tux-evse

Back to the [interop matrix](../../README.md).

---

Their injector replays the capture at us with `expect` blocks reduced to protocol fields
(`scenario-relax.py` — message type and response code stay checked; the stock file aborts at the first
field our station legitimately answers differently, its recorded charger's EVSE ID). 25 exchanges,
`SessionSetup` to `SessionStop`, every code OK, at their `main` built from source — which also carries
our freshly-issued session id through every request, something the v0.1 image's player could not.
