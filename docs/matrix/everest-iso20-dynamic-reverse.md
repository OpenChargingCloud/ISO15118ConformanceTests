# ISO 15118-20 Dynamic in reverse against EVerest

**Matrix cell:** SECC · ISO 15118-20 · DC, Dynamic · EVerest

Back to the [interop matrix](../../README.md).

---

**Three sessions no frame count can tell apart, and one of them is a different control mode.** Their EV
ran Dynamic against our station plain and over mutual TLS 1.3; the **control arm**, the same rig with
`V2G_INTEROP_DYNAMIC` removed so Scheduled is offered first, ran Scheduled — and all three carry
identical counts: 53 exchanges, 33 charge loops, one CableCheck, two PreCharges, five WeldingDetections.
`[V2G20-2656]` has the SECC advertise **both** modes always, so the preference only decides which comes
first and an EV that takes the other one completes exactly as well; our station answers in kind
(`[V2G20-1600]`) either way. It had branched on the car's choice since the mode existed and thrown the
answer away — `Secc20Base.EvControlModeIsDynamic` now records it, the fixture asserts it, and the control
arm proves the value tracks the peer rather than our flag. That control also **measures a claim this
repository had only ever read off its own source**: their EV takes whichever parameter set is offered
first. Fourth instance in three days of *a value our own side already held that no caller could reach*.
[`…-dc-dynamic-reverse`](docs/interop-runs/2026-08-15-everest-d20-dc-dynamic-reverse/notes.md).
