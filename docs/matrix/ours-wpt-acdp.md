# WPT and ACDP: codec only, and independently judged

**Matrix cell:** EVCC · ISO 15118-20 · WPT · ACDP · Ours

Back to the [interop matrix](../../README.md).

---

Still no session state machine anywhere but ours — but "codec only" no longer means "judged only by
its own generator". Since 2026-08-07 every WPT and ACDP frame in the corpus is decoded and re-encoded by
**EXIficient**, which shares no line with cbexigen, and since 2026-08-08 they agree to the octet. Getting
there cost two deliberate changes: these were the only message sets where this codec had been
reproducing cbexigen's grammar rather than ISO's, and where the two disagree we now follow the schema —
[`2026-08-08-schema-conformant-acdp-wpt`](docs/interop-runs/2026-08-08-schema-conformant-acdp-wpt/notes.md).
The failure that decision turned up is the reason it was not close: our `ACDP_ConnectRes` decoded
**cleanly, as `ACDP_DisconnectReq`** — the wrong message, with nothing to report it. Both deviations are
drafted for libcbv2g in [`docs/reports/`](docs/reports/libcbv2g-grammar-deviations.md).
