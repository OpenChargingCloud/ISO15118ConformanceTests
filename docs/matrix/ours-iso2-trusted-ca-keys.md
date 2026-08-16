# `trusted_ca_keys` on the wire, `[V2G2-651]`

**Matrix cell:** EVCC · ISO 15118-2 · TLS 1.2 (unilateral) · Ours

Back to the [interop matrix](../../README.md).

---

**`[V2G2-651]` implemented, fifteen months after this project started citing it at other people.**
Every `-2` EV **shall** name the V2G roots it holds in RFC 6066's `trusted_ca_keys` ClientHello
extension; `grep -rn trusted_ca_keys` matched nothing in our stack until 2026-08-16, which is the
uncomfortable half of [`everest-isomux`](docs/reports/everest-isomux.md) §4 — we filed against a station
that disables support for an extension we could not send.
<br>**The extension decided the backend.** `SslStream` cannot add a ClientHello extension on any
platform, so the managed BouncyCastle stack — until now the `-20` TLS 1.3 profile Schannel cannot serve —
grew ISO 15118-2's transport: TLS 1.2 and the two `-2` suites. A `-2` session configured with roots on
the SslStream path is **refused**, not quietly run without them. Identifier type `cert_sha1_hash`, the
form EVerest's own parser documents; their parser takes all four, so nothing rides on it.
<br>Three tests read the named roots back off a live TLS 1.2 handshake — **two fail when the extension is
removed** — and one *old* cipher-suite test was updated rather than deleted, because catching the
widening is what it was written for. It also uncovered a real fault underneath: `BuildSigner` built the
TLS 1.3 certificate structure unconditionally, and TLS 1.2 answers that with `internal_error(80)`; its
comment had described half the rule since the `-20` work.
<br>**And it decided their §4 the same day.** `IsoMux` caps TLS at 1.2, which is exactly what this client
speaks — so the failing case ran at last, in four arms with the ClientHello on tape. Naming root **B**
while trusting only B is refused; naming root **B** while trusting only **A** completes a full DC
session; the control brackets both, first and last. The capture shows the car naming one authority,
`cert_sha1_hash EB:80:…:F5:A8` = `CN=V2GRootCA-B`, and their station answering with a chain that verifies
under root **A** — while `CN=SECCCert-B` sat installed and valid beside it. **§4's consequence is measured
rather than predicted from their boot line: the extension arrives and changes nothing.** The first attempt
that evening had failed on *our* validation, control included, which is what said the fault was ours
before it was theirs. [`…-isomux-section4`](docs/interop-runs/2026-08-16-everest-isomux-section4/notes.md).
