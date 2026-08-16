# The multi-protocol SAP offer against EVerest's IsoMux

**Matrix cell:** EVCC · ISO 15118-20 · Multi-protocol SAP offer · EVerest

Back to the [interop matrix](../../README.md).

---

`IsoMux` routes on *"mentions -20 anywhere"* and never reads SAP `Priority` — confirmed on the wire
against 2025.10.0, 2026.02.1, and a third time over TLS, with the same request and answer bytes every
time. `[V2G2-169]` and `[V2G20-169]` make selecting by the EV's ranking a *shall*, so it is a defect and
not only a surprise: the **twentieth filing**,
[`everest-isomux.md`](docs/reports/everest-isomux.md). Both modules behind
their mux already implement the rule.

`IsoMux` terminates TLS at the **-2 profile** — 1.2 with the suite ISO 15118-2 prescribes, pinned in
code it shares with `EvseV2G` — and only then routes on the SAP offer. So a dual-stack EV gets a complete
**-20 session over TLS 1.2**, and a -20 EV that pins its own profile gets alert 70. It also corrected a
mirror of that layering on our side. `[V2G20-2356]` forbids the station to select -20 there, and between
the two halves their -20 backend is unreachable by any conformant EV: the **nineteenth filing**,
[`everest-isomux.md`](docs/reports/everest-isomux.md). The offer that
showed it was ours and broke the mirror requirement `[V2G20-1237]` — [our own item](docs/open-work.md).
