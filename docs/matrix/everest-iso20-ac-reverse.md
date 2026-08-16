# ISO 15118-20 AC in reverse against EVerest

**Matrix cell:** SECC · ISO 15118-20 · AC *and* Mutual TLS 1.3 · EVerest

Back to the [interop matrix](../../README.md).

---

**The first AC session this project has run in the reverse direction, in either protocol** — and it
cost two findings on the way. Ours: the reverse fixture passed no power mode to the SAP handshake, whose
parameter *defaults* to DC, so every reverse `-20` run ever made announced a DC-only catalogue and it
took an AC EV to notice. Theirs, measured rather than read: their `PyEvJosev` paces the AC charge loop at
**≈532 ms** — 44 loops in 23,407 s from their own log — against the **500 ms** `[V2G20-1500]` and
`[V2G20-1502]` allow a station to wait, so **2 of 2** runs with our conformant timer die on the *first*
charge loop and the 56-exchange session above needed `V2G_INTEROP_CHARGELOOP` to relax it. **Decided and
filed 2026-08-14, as the forty-seventh:** the EVCC *is* bound by the same table — Table 216 gives it
**0,25 s** (`[V2G20-1499]`) — but Figure 212's legend sorts that threshold as a *performance* criterion
where the station's is an *error* one, so it is a deviation of 2,1× and not a violated timeout, and the
abort belongs to the station. [`josev-iso20-evcc-charge-loop-pacing.md`](docs/reports/josev-iso20-evcc-charge-loop-pacing.md);
[`…-d20-ac-reverse`](docs/interop-runs/2026-08-13-everest-d20-ac-reverse/notes.md).

**The first TLS session this project has run in the reverse direction, in any protocol** — and the
reason there had never been one was **our fixture**, not the counterparties. Their EV discovered our
station over SDP with the TLS security byte, handshook **mutual TLS 1.3** (`TLS_AES_256_GCM_SHA384`),
presented an OEM vehicle certificate of its own (`CN=WMIV1234567890ABCDEX, O=Pionix`, P-256, issued by
their `VehicleSubCA2`) against the CPO leaf we presented from their own PKI, and charged: **56 exchanges,
every response `OK`, 43 charge loops to `SessionStop`**. `InteropEnvironment.ServerTlsOrNull` has existed
since the tux-evse runs and the eVDriveFlow reverse fixture uses it; this one built a plain listener and
advertised `tls: false` as a constant — the third instance in a week of *a capability we already held
that no call site reached for*. It also **measured what [the forty-seventh filing](docs/reports/josev-iso20-evcc-charge-loop-pacing.md)
had to leave open**: their EV's charge-loop pacing over TLS is **≈544 ms** against ≈532 ms on plain TCP,
so the deviation a real `-20` deployment sees is the larger one.
[`…-d20-ac-reverse-tls`](docs/interop-runs/2026-08-14-everest-d20-ac-reverse-tls/notes.md).

**The AC charge loop's Dynamic answer, in front of a live peer for the first time — and an invariant
that closed a three-day loose end.** `ScheduleExchange` is shared between the power modes; the
charge-loop answer is not, and a Dynamic AC response carries a **mandatory** `EVSETargetActivePower` a
Scheduled one does not, so 40 and 41 completed Dynamic loops are the evidence our AC side answers in kind
(`[V2G20-1600]`) rather than only our DC side. The three arms are 56 exchanges each and differ in
*composition*: 5 / 4 `PowerDeliveryReq` before the loop in Dynamic against 1 in the Scheduled control.
Lined up with the two earlier AC reverse runs that makes five sessions in which **`PowerDelivery` before
the loop plus charge loops = 45, every time** — the car simulator fixes the window, not the exchange
count, so every message spent getting started is one not spent charging. That retires the extra
`PowerDeliveryReq` [noted on 08-14](docs/interop-runs/2026-08-14-everest-d20-ac-reverse-tls/notes.md) and
withdrawn on the same day: it is readiness polling, which our own `PowerOn` phase self-loops for and says
so, and it was never the transport.
[`…-ac-dynamic-reverse`](docs/interop-runs/2026-08-15-everest-d20-ac-dynamic-reverse/notes.md).
<br>**The reading offered there — that Dynamic makes the polling larger — was itself refuted hours later**
by the AC_BPT Dynamic run, whose Dynamic arms poll once. Across ten AC reverse sessions the invariant
holds every time and the count is 1 or 2 in nine of them; **nothing measured predicts the variation**, and
the `PowerOn` loop is designed to tolerate it. Two explanations refuted in two days from one three-line
observation, both hedged when written and both refuted by the next run — *an explanation offered for a
difference between two runs is a hypothesis about the next ten*.
