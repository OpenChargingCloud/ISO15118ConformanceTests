# tux-evse's captured AC routes against our SECC

**Matrix cell:** SECC · ISO 15118-2 · AC, EIM · tux-evse

Back to the [interop matrix](../../README.md).

---

The AC capture exists only at their HEAD, converted by their own `pcap-iso15118`. Under their
`basic` compaction the route runs to `SessionStop` — including the VW stopping straight from the
charging phase, where **the recorded charger answered `FAILED_SequenceError` and ours answers `OK`**,
a divergence kept, not corrected. Uncompacted, the VW's double `Authorization` poll reached the arm of
our sequence guard that closed the connection instead of answering `FAILED_SequenceError` on the wire —
the first finding against us from this counterparty, and one only a replayer could produce: every
other peer polls only while our station says `Ongoing`. **Fixed and re-run the same day**: the refusal
now goes out in the request's own response type, and their injector decodes it.

Both Taycan captures ask for **11,040 W** in their `ChargingProfile` — 3 × 230 V × 16 A, the
ubiquitous European AC charge point — and our station offered a rounded **11,000 W**, so [V2G2-761]
refused `PowerDelivery` by 0.4 %. Correct by the letter on both sides, and a bad trade for a station
built to test interoperability: it manufactures a failure no real charger would produce, at the last
message before charging. **Fixed and re-run the same day**: the offer is now the physical number, in
the plain schedule and in tuple 1 of the tariff offer, the recorded corpus moved with it (the offer,
the profile, and the AC energies 549 → 552 Wh), and both captures then ran to `SessionStop` — ten
exchanges, every response `OK`, their injector's own TAP reporting 12/12, and both flow reports ending
"the order matches the declared flow exactly". It is also the first AC session here to reach the charge
loop, which is how `charging_status_req` finally entered the verb table — from **their** converter and
**their** TAP output, not from a guess. The unfolded runs of the same captures are the second and third
real car to poll `Authorization` twice, and both confirm the 2026-08-06 fix: the refusal goes out as
`FAILED_SequenceError` instead of a closed socket. They were re-run too and are **unchanged**, which is
the answer rather than a gap — the session dies four messages before `PowerDelivery`, so a schedule fix
cannot reach it, and "changed nothing" is now a measurement instead of a claim.
