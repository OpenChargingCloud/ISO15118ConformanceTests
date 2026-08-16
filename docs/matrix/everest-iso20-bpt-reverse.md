# EVerest's car picks our bidirectional services

**Matrix cell:** SECC · ISO 15118-20 · BPT · EVerest

Back to the [interop matrix](../../README.md).

---

**Their car chose our bidirectional service**, which is the half of BPT no forward run can show: in
`EV→` we rank AC_BPT first and their station answers, so what is tested is their *response*; here their
EV picked service **5** out of `Secc20Ac`'s `{ 1, 5 }` on its own. One line changed on their side
(`supported_d20_energy_services: AC_BPT`); 56 exchanges, 44 charge loops, all `OK`, **plain and again
over mutual TLS 1.3**. Two things make it an AC_BPT result rather than a relabelled AC one: the fixture
now **asserts** the negotiated id in this direction — an EV that quietly took service 1 would have
completed identically, which is the trap the MCS guard has covered since 2026-08-06 — and the `OK` at
charge-parameter discovery is only reachable if their request carried the bidirectional mode, since our
station answers `FAILED_WrongChargeParameter` to a direction that contradicts the selected service. It
also **withdrew an inference from the run before it**: the extra `PowerDeliveryReq` seen in both reverse
TLS sessions does not appear here, so it was never the transport.
[`…-d20-ac-bpt-reverse`](docs/interop-runs/2026-08-14-everest-d20-ac-bpt-reverse/notes.md).
<br>**`DC_BPT` followed an hour later and cost no code at all** — one line in their config, everything
else built by the four runs before it. Their EV picked service **6** out of `Secc20Dc`'s `{ 2, 6 }` and
drove the whole DC sequence, CableCheck and PreCharge and WeldingDetection included, to `SessionStop`;
53 exchanges plain, 52 over mutual TLS 1.3. Its charge-parameter check has the neatest provenance in the
directory: our station only refuses a direction that contradicts the selected service **because
everest-core refused ours that way on 2026-08-05**, so a counterparty's `FAILED_WrongChargeParameter`
became our conformance check and the check then validated the counterparty.
[`…-d20-dc-bpt-reverse`](docs/interop-runs/2026-08-14-everest-d20-dc-bpt-reverse/notes.md).

**All four AC charge-loop control-mode variants have now met somebody else's car.** `Secc20Ac.ClResInKind`
answers strictly in kind (`[V2G20-1600]`) and has four arms — Scheduled, BPT_Scheduled, Dynamic, and
**BPT_Dynamic**, which this run supplied: service 5 in Dynamic, plain and over mutual TLS 1.3, with a
BPT/Scheduled control. A wrong variant is a wire-type mismatch their Josev-derived EV does not survive, so
44 completed loops per arm is the evidence. **It also refuted footnote 36's own explanation**: its Dynamic
arms poll `PowerDelivery` once, not four or five times. Across ten AC reverse sessions the invariant holds
every time and nothing measured predicts the variation.
[`…-ac-bpt-dynamic-reverse`](docs/interop-runs/2026-08-15-everest-d20-ac-bpt-dynamic-reverse/notes.md).
