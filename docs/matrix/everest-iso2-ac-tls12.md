# ISO 15118-2 AC over TLS 1.2, against EVerest

**Matrix cell:** EVCC · ISO 15118-2 · AC, EIM *and* TLS 1.2 (unilateral) · EVerest

Back to the [interop matrix](../../README.md).

---

**The last untried cell in this counterparty's column, and it found something in ours.** Four `-2` AC
sessions over TLS 1.2, 13 exchanges each, every response `OK`, against `EvseV2G` with one line changed
(`tls_security: force`). Their transport is conformant where it matters: **TLS 1.2 with
`TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256`** — one of the two ISO 15118-2 prescribes, and the one
[tux-evse's configs do not offer](docs/reports/tux-evse-tls.md) — and they send their **whole chain**,
unlike `Evse15118D20`. Being able to say that cost a fix: the arm anchored at the **V2G root alone** was
refused by us and accepted by `openssl s_client -CAfile` against the same station minutes apart, because
`InteropEnvironment.DevTlsOrNull` discarded the validation callback's `X509Chain` — **the same defect the
app fixed on 2026-08-09, in a second copy the fixtures had of their own**. Every TLS run before today had
its intermediates spoon-fed in the trust bundle, which passes either way; only a root-only anchor can tell
them apart. Fixed through `TrustRoots.PeerIntermediates`, the root-only session then ran complete, and the
regression is the one test of seven in `ChainValidationTests` that fails when the fix is removed. Negative
control: their **MO** root as the anchor is refused (their log records our `SSL alert number 42`), and
`EvseV2G` survives that refusal where `Evse15118D20`'s accept loop would not.
[`…-iso2-ac-tls12`](docs/interop-runs/2026-08-14-everest-iso2-ac-tls12/notes.md).
