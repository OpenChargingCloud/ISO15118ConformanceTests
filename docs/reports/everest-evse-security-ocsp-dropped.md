# Draft report to EVerest — `to_everest(CertificateInfo)` drops `ocsp`, so no station ever staples

Status: **draft, not sent.** Measured 2026-08-10 against everest-core **2026.02.1** (`b61bb12b8`) built
from source: their own MQTT reply, their own boot warning, their own test PKI, no EV and no session
required. Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-10-everest-ocsp-stapling`](../interop-runs/2026-08-10-everest-ocsp-stapling/notes.md) — the
run notes, with [`their-evse-security-mqtt.log`](../interop-runs/2026-08-10-everest-ocsp-stapling/their-evse-security-mqtt.log)
(the request and the reply that is missing the field),
[`their-charger.log`](../interop-runs/2026-08-10-everest-ocsp-stapling/their-charger.log) and
[`their-cert-layout.txt`](../interop-runs/2026-08-10-everest-ocsp-stapling/their-cert-layout.txt).

Five other reports for the same project are in
[`everest-loop-shutdown.md`](everest-loop-shutdown.md),
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md),
[`everest-isomux-iso20-over-tls12.md`](everest-isomux-iso20-over-tls12.md),
[`everest-isomux-sap-priority.md`](everest-isomux-sap-priority.md) and
[`everest-evsev2g-session-log-responses.md`](everest-evsev2g-session-log-responses.md), plus
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) and one to your fork of Josev's
certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)). **File them separately.**
The framing in `everest-loop-shutdown.md` — what everest-core has been worth to this project, and why a
report from us is not a bug filed by a stranger — applies here unchanged and is not repeated.

---

**Title:** `conversions::to_everest(evse_security::CertificateInfo)` does not copy the `ocsp` member, so
`EvseV2G`'s TLS server receives an empty OCSP list, caches nothing, and never staples an OCSP response —
which `[V2G2-871]` and `[V2G20-2388]` both require, and `[V2G2-873]` makes a conformant EVCC close the
connection over

**Version:** everest-core **2026.02.1** (`b61bb12b8`), native build, Debian 13, OpenSSL 3.5.6. Modules
`EvseV2G` + `EvseSecurity`, library `lib/everest/tls`, your own `config-sil-dc`-shaped config with
`tls_security: allow` and your unmodified test PKI.

## Summary

The stapling path is complete and correct at both ends. `EvseV2G` asks for the OCSP data belonging to
its chain; libevse-security assembles it; `lib/everest/tls` implements `status_request` and
`status_request_v2` and has unit tests for both. Between the two there is one type conversion, and it
copies six of the seven members.

The result is not a partial staple. It is none, on every station, whatever the operator provisions.

## What we saw

At boot, with no EV anywhere:

```
[INFO] iso15118_charge :: TLS server on eth0 is listening on port [fe80::…%2]:64109
[WARN] iso15118_charge :: <n> certificates != <n> OCSP responses
```

The `<n>` are literal — `tls.cpp:1039` logs that string unsubstituted, so the warning never says which
two numbers disagreed. They are **three and zero**: three certificates in `CPO_CERT_CHAIN.pem`, and an
OCSP list that arrived empty.

`EvseV2G` requested it (`EvseV2G/connection/tls_connection.cpp:298`):

```cpp
const auto cert_info =
    ctx->r_security->call_get_all_valid_certificates_info(LeafCertificateType::V2G, EncodingFormat::PEM, true);
                                                                                          // include_ocsp ↑
```

and this is the whole reply, off the broker:

```json
{"retval": {"info": [{
    "certificate": "…/client/cso/CPO_CERT_CHAIN.pem",
    "certificate_count": 3,
    "certificate_root": "-----BEGIN CERTIFICATE-----\nMIIBxTCC…",
    "certificate_single": "…/client/cso/SECC_LEAF.pem",
    "key": "…/client/cso/SECC_LEAF.key",
    "password": "123456"
}], "status": "Accepted"}}
```

No `ocsp` — the member your own interface documents as *"Certificate related OCSP data, if requested"*.

It was requested, and libevse-security built it. `evse_security.cpp:1586-1604` pushes **one entry per
certificate in the chain file** when `include_ocsp` is set — `ocsp_path` unset where no response is
cached, an empty-hash entry where the hierarchy cannot place the certificate, with the comment *"Always
add to preserve file order"* — and `:1633-1634` assigns the vector to `info.ocsp`. A correct reply
would have carried three entries. The list was built and then lost.

## Where it comes from

`lib/everest/conversions/evse_security/src/conversions.cpp`. The two directions are a few lines apart
and disagree with each other:

```cpp
// :192  MQTT type → internal type — handles it
evse_security::CertificateInfo from_everest(types::evse_security::CertificateInfo other) {
    …
    if (other.ocsp.has_value()) {                       // :199
        for (auto& ocsp_data : other.ocsp.value()) {
            lhs.ocsp.push_back(from_everest(ocsp_data));
        }
    }
    …
}

// :429  internal type → MQTT type — does not
types::evse_security::CertificateInfo to_everest(evse_security::CertificateInfo other) {
    types::evse_security::CertificateInfo lhs;
    lhs.key = other.key;
    lhs.certificate_root = other.certificate_root;
    lhs.certificate = other.certificate;
    lhs.certificate_single = other.certificate_single;
    lhs.password = other.password;
    lhs.certificate_count = other.certificate_count;
    return lhs;                                          // ← ocsp
}
```

Both types carry the member — `evse_security/evse_types.hpp:193` and the generated
`types/evse_security.hpp:1056`. The target one is a `std::optional`, so it stays unset and `to_json`
omits the key entirely, which is what the capture shows.

Everything after that is your code behaving correctly on empty input:

| | |
|---|---|
| `EvseV2G/connection/tls_connection.cpp:318` | `if (chain.ocsp)` is false → `ocsp_response_files` stays empty |
| `lib/everest/tls/src/tls.cpp:1026` | `certs.size() == i.ocsp_response_files.size()` is `3 == 0` |
| `lib/everest/tls/src/tls.cpp:1039` | the warning — and `entries` is left untouched, so **nothing is cached, not even for certificates that do have a response**. It is all-or-nothing |
| `lib/everest/tls/src/tls.cpp:1084`, `:1089` | `m_cache.load(entries)` loads that empty list, and it is the only writer of the cache |
| `lib/everest/tls/extensions/status_request.cpp:168`, `:250` | every handshake lookup misses — `OcspCache::lookup: not in cache: …`, which our TLS runs recorded |
| `lib/everest/tls/extensions/status_request.cpp:269` | *"don't include the extension when there are no OCSP responses"* |

## Why we think it is worth fixing

**Because the standard requires the answer, and one of the protocols requires the question.**

- **ISO 15118-2.** `[V2G2-871]`: a station outside a private environment owes the EV its chain, and —
  once the EV has asked for OCSP data — one OCSP response per certificate it puts into the handshake,
  in the IETF RFC 6961 form, which is the `status_request_v2` your TLS layer already implements.
  `[V2G2-872]` is the carve-out for a private environment. `[V2G2-875]` puts the EV under a duty to
  verify those responses and to abandon the TLS setup when that verification does not succeed.
- `[V2G2-873]` is what turns this from an audit finding into an interoperability failure. It says what
  a conformant EV does when it asked and nothing came back: for a chain rooted at a **V2G root**, it
  **closes the connection**. So such an EV cannot establish TLS with an EVerest station at all — and
  therefore cannot do Plug & Charge with one.
- **ISO 15118-20.** `[V2G20-2372]` removes the "if" entirely: the EV is required to carry
  `status_request` in its `ClientHello`, so the question is always asked. `[V2G20-2388]` then puts a
  public station under a duty to answer with one response per certificate of the chain it presents,
  `[V2G20-2391]` extends that to a private station that supports PnC, and `[V2G20-1021]` caps how long
  a response may be reused at a week. This reaches `EvseV2G` whenever `IsoMux` fronts it, since
  `IsoMux` terminates TLS itself for both protocols.

We are citing requirement identifiers and paraphrasing what they oblige, not quoting the text. The `-2`
identifiers are read from the 2022 DIS revision; ISO 15118-2:2014 is what most deployed stacks target,
and while this material is old and stable, that difference is worth one sentence in the issue.

**And because nothing tells the operator.** The one signal is a warning whose two numbers are literal
`<n>`, at boot, in a stack that logs a good deal at boot. Everything downstream then behaves exactly as
if the operator had chosen not to staple.

## Suggested direction

1. **Copy the member.** One line in `to_everest`, mirroring the `from_everest` a few lines above:

   ```cpp
   for (const auto& ocsp_data : other.ocsp) {
       lhs.ocsp.emplace(…);   // or build the vector, then assign
   }
   ```

   Whether the optional should be left unset when the vector is empty is your call; the caller only
   tests `if (chain.ocsp)`.

2. **Say the two numbers.** `tls.cpp:1039` costs nothing to make useful:
   `log_warning(std::to_string(certs.size()) + " certificates != " + std::to_string(i.ocsp_response_files.size()) + " OCSP responses")`.
   With that line, this report would not have needed a capture.

3. **Consider not making it all-or-nothing.** `tls.cpp:1026` discards the OCSP data for every
   certificate when the counts disagree for one. Caching what matches, and warning about the rest,
   fails softer — and `[V2G2-871]` asks for a response per certificate, so a partial answer is at least
   closer to the requirement than none.

4. **`IsoMux` needs its own fix**, and (1) alone does not give it one:
   `IsoMux/connection/tls_connection.cpp:291` calls
   `call_get_leaf_certificate_info(V2G, PEM, false)` — `include_ocsp = false` — so it never asks. Since
   `IsoMux` is the deployment that serves ISO 15118-20 over a connection it terminates itself, it is the
   one where `[V2G20-2388]` bites hardest.

## Not part of this

`Evse15118D20` does not staple either, but for an unrelated reason: `libiso15118` contains no OCSP
handling at all — the only matches in that tree are `authorityInfoAccess` lines in test certificate
configs. That is a missing feature, not a lost field, and it deserves its own issue if you want one.

The unit tests in `lib/everest/tls/tests/` are not at fault and are worth mentioning kindly: they set
`ocsp_response_files` directly (`tls_connection_test.cpp:42`), so they exercise the stapling correctly
and pass. The defect lives exactly at the seam between two components that are each tested alone.

---

## Before sending

- [x] **Measure it, do not infer it.** The reply with no `ocsp` key is in the capture, against your
      station, your PKI, at 2026.02.1 — and it needed no EV and no session, which is worth saying in
      the issue so a maintainer can reproduce it in one minute.
- [x] **Check every line reference against the tree.**
      `conversions.cpp:192`, `:199-201`, `:429-437`; `evse_types.hpp:193`;
      generated `types/evse_security.hpp:1056`; `evse_security.cpp:1586-1604`, `:1633-1634`;
      `EvseV2G/connection/tls_connection.cpp:298`, `:318-324`;
      `IsoMux/connection/tls_connection.cpp:291`, `:315`;
      `tls.cpp:1026`, `:1034`, `:1039`, `:1084`, `:1089`;
      `status_request.cpp:168`, `:250`, `:269`; `tests/tls_connection_test.cpp:42` — read from the
      built 2026.02.1 source on 2026-08-10.
- [x] **Rule out the rig.** Our test PKI carries no OCSP data at all, and that is *not* the cause: with
      `include_ocsp` set, the reply should still have carried three entries with `ocsp_path` unset. Say
      this in the issue, because it is the first thing a maintainer will suspect.
- [ ] **Provision one OCSP response and show the warning survive.** Not done. It would turn "the drop
      is unconditional in the source" into "the drop is unconditional, measured" — strictly stronger,
      and the only part of this report that is still a code reading rather than an observation.
- [ ] **Lead with `[V2G2-873]`, not with the missing line.** A dropped struct member reads as tidiness
      until you say that a conformant EV closes the connection over it.
- [ ] **Decide how to raise the `IsoMux` half.** Same symptom, different cause; possibly the same
      issue, possibly its own. Do not let a fix to `to_everest` close both.
- [ ] **Mention the `-2` document caveat once.** The `[V2G2-…]` identifiers here are read from the 2022
      DIS revision.
- [ ] **File one issue, this one.**
- [ ] **Post under your own name, in your own words.**
