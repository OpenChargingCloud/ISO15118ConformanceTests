# Draft report to EVerest — `IsoMux` starts with `trusted_ca_keys support disabled`, `EvseV2G` does not

Status: **draft, not sent.** Read off one station boot on 2026-08-10 against everest-core **2026.02.1**
(`b61bb12b8`) built from source, and traced to two argument values in the same call. Post it under your
own name; see *Before sending* at the bottom — the second item is the one this report cannot tick.

Evidence in this repository:
[`2026-08-10-everest-isomux-trusted-ca-keys`](../interop-runs/2026-08-10-everest-isomux-trusted-ca-keys/notes.md)
— the run notes and [`boot-a-b.log`](../interop-runs/2026-08-10-everest-isomux-trusted-ca-keys/boot-a-b.log),
extracted from the full station log of
[the neighbouring run](../interop-runs/2026-08-10-everest-isomux-shortread/their-charger.log).

Seven other reports for the same project are in
[`everest-loop-shutdown.md`](everest-loop-shutdown.md),
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md),
[`everest-evsev2g-session-log-responses.md`](everest-evsev2g-session-log-responses.md),
[`everest-evse-security-ocsp-dropped.md`](everest-evse-security-ocsp-dropped.md) and — **all `IsoMux`,
and all four could reasonably be one issue if you prefer** —
[`everest-isomux-iso20-over-tls12.md`](everest-isomux-iso20-over-tls12.md),
[`everest-isomux-sap-priority.md`](everest-isomux-sap-priority.md),
[`everest-isomux-continues-after-read-failure.md`](everest-isomux-continues-after-read-failure.md),
plus [`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) and one to your fork of Josev's
certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)). The framing in
`everest-loop-shutdown.md` — what everest-core has been worth to this project, and why a report from us
is not a bug filed by a stranger — applies here unchanged and is not repeated.

---

**Title:** `IsoMux` requests its certificate with `get_leaf_certificate_info(…, include_ocsp=false)`,
which also carries `include_root = false`, so it never sets `trust_anchor_pem`, its chain is never
verified or registered, and the TLS server starts with `trusted_ca_keys support disabled` — the
extension `[V2G2-651]` makes every EVCC send and `[V2G2-871]` makes the station act on

**Version:** everest-core **2026.02.1** (`b61bb12b8`), native build, Debian 13, OpenSSL 3.5.6. Modules
`IsoMux` + `EvseV2G` + `Evse15118D20`, `config-sil-dc-isomux`-shaped config, your unmodified test PKI.

## What we saw

One station process, one PKI, two TLS servers, four milliseconds apart:

```
15:41:01.165  iso_mux:IsoMux   TLS server on eth0 is listening on port …:64110
15:41:01.165  iso15118_2:Evse  TLS server on lo   is listening on port …:64109
15:41:01.256  iso_mux:IsoMux   <n> certificates != <n> OCSP responses
15:41:01.257  iso_mux:IsoMux   No trust anchors for certificate: C:DE CN:SECCCert DC:CPO O:EVerest
15:41:01.257  iso_mux:IsoMux   trusted_ca_keys support disabled
15:41:01.261  iso15118_2:Evse  <n> certificates != <n> OCSP responses
                               ← and nothing more from EvseV2G
```

Both modules lose their OCSP data — that is
[a separate report](everest-evse-security-ocsp-dropped.md) and a different cause. Only the multiplexer
loses its trust anchors.

## Where it comes from

Two calls that differ in what they ask for.

```cpp
// IsoMux/connection/tls_connection.cpp:292
call_get_leaf_certificate_info(LeafCertificateType::V2G, EncodingFormat::PEM, false);

// EvseV2G/connection/tls_connection.cpp:298
call_get_all_valid_certificates_info(LeafCertificateType::V2G, EncodingFormat::PEM, true);
```

`get_leaf_certificate_info` runs with `include_root = false`
(`evse_security.cpp:1371-1374`, the `{…, false, false, false}` tail), so `certificate_root` is not in
the reply and `IsoMux` has nothing to put in `trust_anchor_pem` — it sets `certificate_chain_file`,
`private_key_file` and `private_key_password`, and no trust anchor at all. `EvseV2G` gets the root and
sets it at `:316`.

From there `lib/everest/tls/src/tls.cpp` does the rest by the book:

| | |
|---|---|
| `:1048` | `if (!tas.empty())` — false for `IsoMux` |
| `:1057` | so `openssl::verify_chain(chain)` is never reached, and the chain is not added to `chains` |
| `:1062` | *"No trust anchors for certificate: …"* |
| `:1077-1080` | `chains` is empty → *"trusted_ca_keys support disabled"*, and `m_server_trusted_ca_keys.update({})` |

`ServerTrustedCaKeys::handle_certificate_cb` (`extensions/trusted_ca_keys.cpp:368-407`, installed at
`:335`) is where the extension would be honoured: when the client sent one it calls `select()` over the
registered chains and installs the match. With an empty list `select()` returns `nullptr`, nothing is
installed, and the handshake continues with what `init_ssl` configured statically — `cfg.chains[0]`
(`tls.cpp:940-941`).

**So two things are impossible in `IsoMux`, not one.** No chain ever enters the selectable list; and
`get_leaf_certificate_info` returns the **newest single** chain rather than all valid ones, so even
with a trust anchor there would be exactly one chain to choose between. `EvseV2G` gets both right
through the one call it makes.

## Why we think it is worth fixing

**Because the EV always asks.** `[V2G2-651]` obliges the EVCC to send a `trusted_ca_keys` extension
(IETF RFC 6066) listing the V2G roots it holds — not optionally and not conditionally. `[V2G2-871]`
then obliges a station outside a private environment to present a chain up to *one of the roots the EV
signalled*; `[V2G2-923]` allows some other root only when it cannot match. An EV that receives a chain
not tracing to a root it trusts must treat it as unvalidated (`[V2G2-924]`) and abandon the TLS setup
(`[V2G2-875]`). And `[V2G2-878]` puts the ceiling on concurrently valid V2G root certificates at ten
per root CA, so the multi-root case is not hypothetical.

We are citing requirement identifiers and paraphrasing what they oblige, not quoting the text. These
`-2` identifiers are read from the 2022 DIS revision; most deployed stacks target ISO 15118-2:2014, and
that difference is worth a sentence in the issue.

**With one V2G root nothing shows** — the station serves its only chain, which is what `[V2G2-923]`
would have it do anyway, and that is why these two lines sat in our logs from 2026-08-06 unread. With
two, `IsoMux` serves the first one it was handed and an EV holding only the other walks away. That is
the case the extension exists for, and the module that terminates TLS for both protocols is the one
that cannot do it.

## Suggested direction

1. **Ask for the root.** The smallest change is to have `IsoMux` call the same thing `EvseV2G` does —
   `get_all_valid_certificates_info(V2G, PEM, …)` — and set `trust_anchor_pem` from
   `certificate_root`. That fixes the trust anchor and the single-chain limitation together, which is
   why it is preferable to threading an `include_root` flag into `get_leaf_certificate_info`.
2. **Or say it is deliberate.** If the multiplexer is meant to be single-chain by design, the
   `trusted_ca_keys` machinery it inherits from `lib/everest/tls` is dead weight there and the two
   warnings are noise on every boot — worth a comment at the call site either way, since the next
   reader will ask the same question we did.
3. **Consider making the warning name the module's consequence.** *"trusted_ca_keys support disabled"*
   is accurate and gives no hint that a certificate-selection duty just went away; a station operator
   with two roots would want to know at that line rather than at the first refused handshake.

## Not part of this

The OCSP warning on the line above is [the other report](everest-evse-security-ocsp-dropped.md): a
dropped struct member in a type conversion, affecting `EvseV2G` as well, and a fix there does not touch
this. Worth keeping the two apart even though they surface four milliseconds apart on the same boot.

---

## Before sending

- [x] **Read it off their own log, at their own release.** Two warnings, one boot, with the module that
      does it correctly logging neither in the same process 4 ms later — the A/B is theirs, not our
      construction.
- [ ] **Run the failing case.** *Not done, and the report says so.* It needs a station provisioned with
      two V2G chains under different roots and an EV sending `trusted_ca_keys` naming only the second;
      our EVCC does not send the extension, and `openssl s_client` will not send it unpatched. Until
      someone does, this is a station-side reading with a station-side log behind it and no session.
      **Say that in the issue** rather than letting a maintainer infer a session that never happened.
- [x] **Check every line reference against the tree.** `IsoMux/connection/tls_connection.cpp:292`;
      `EvseV2G/connection/tls_connection.cpp:298`, `:316`; `evse_security.cpp:1371-1374`;
      `tls.cpp:940-941`, `:1048`, `:1057`, `:1062`, `:1077-1080`;
      `extensions/trusted_ca_keys.cpp:335`, `:368-407` — read from the built 2026.02.1 source on
      2026-08-10.
- [ ] **Lead with `[V2G2-651]`.** *Every* EV sends this extension; that is what makes a station that
      cannot act on it worth ten minutes, and it is the first sentence a maintainer needs.
- [ ] **Decide whether this joins the other three `IsoMux` reports.** Four findings, one module; a
      maintainer may well prefer one issue with four headings.
- [ ] **Mention the `-2` document caveat once.** These `[V2G2-…]` identifiers are read from the 2022 DIS
      revision.
- [ ] **File one issue, this one.**
- [ ] **Post under your own name, in your own words.**
