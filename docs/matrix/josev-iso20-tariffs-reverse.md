# A signed schedule consumed, and nothing that verifies it

**Matrix cell:** SECC · ISO 15118-20 · Signed tariffs · Josev

Back to the [interop matrix](../../README.md).

---

The one cell where `◐` is a missing **verifier**, not a missing session: their EV consumed our signed
`AbsolutePriceSchedule` and ran on it, but Josev's EVCC-side tariff check is a literal `# TODO`.
