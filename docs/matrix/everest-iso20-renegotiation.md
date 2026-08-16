# Renegotiation in reverse against EVerest's fork

**Matrix cell:** SECC · ISO 15118-20 · Renegotiation · EVerest

Back to the [interop matrix](../../README.md).

---

Their `PyEvJosev` EV is EVerest's fork of Josev, so this is the **same defect the Josev column carries**, now seen in **DC** and against the fork at `26f7988` rather than in AC against upstream. Our station signalled `ServiceRenegotiation` once mid-charge; their EV stopped the charge, ran welding detection and sent `SessionStopReq(ServiceRenegotiation)` — a frame *upstream cannot produce*, since its `DCWeldingDetection` hardcodes `Terminate` — and then closed the connection after our `SessionStopRes(OK)` left the session open. So the fork has fixed half of it. See [`…-iso20-renegotiation-reverse`](docs/interop-runs/2026-08-10-everest-iso20-renegotiation-reverse/notes.md) and [`josev-iso20-renegotiation.md`](docs/reports/josev-iso20-renegotiation.md).
