# Draft report to EVerest — `Evse15118D20` never staples an OCSP response, and has nowhere to put one

Status: **draft, not sent.** Measured 2026-08-10 against everest-core **2026.02.1** (`b61bb12b8`) built
from source: `openssl s_client -status` against their `-20` station on both TLS versions, plus a control
that proves the request reached them. No EV, no session, no client PKI. Post it under your own name; see
*Before sending* at the bottom.

**This is a different issue from
[`everest-evse-security-ocsp-dropped.md`](everest-evse-security-ocsp-dropped.md), and the difference is
the point.** That one is a lost struct member on the `EvseV2G` path — the stapling machinery exists,
the data never arrives. This one is a module with no stapling machinery at all, and it needs its own
issue because **fixing the conversion does nothing here, and fixing this does nothing without the
conversion.** Both are needed for a `-20` station to staple; neither is sufficient. If they are filed as
one, whichever is fixed first will look like the whole job.

Evidence in this repository:
[`2026-08-10-everest-d20-ocsp-absent`](../interop-runs/2026-08-10-everest-d20-ocsp-absent/notes.md) —
the run notes, both `openssl` captures, and the control against `IsoMux`.

Other reports go to everest-core:
[`everest-evse-security-ocsp-dropped.md`](everest-evse-security-ocsp-dropped.md) (**read it beside
this one**),
[`everest-d20-client-auth.md`](everest-d20-client-auth.md) — **the same TLS server, two issues about
what it asks the EV for** —
[`everest-d20-ac-namespace.md`](everest-d20-ac-namespace.md),
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md),
[`everest-loop-shutdown.md`](everest-loop-shutdown.md) — all four also libiso15118 or `Evse15118D20`,
so **the same reviewer** — plus [`everest-isomux.md`](everest-isomux.md),
[`everest-evsev2g-session-log-responses.md`](everest-evsev2g-session-log-responses.md),
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) and one to your fork of Josev's
certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)). The framing in
`everest-loop-shutdown.md` — what everest-core has been worth to this project, and why a report from us
is not a bug filed by a stranger — applies here unchanged and is not repeated.

---

**Title:** `libiso15118` has no OCSP handling of any kind, so an `Evse15118D20` station answers the
mandatory `status_request` extension with nothing — `[V2G20-2372]` makes every `-20` EV ask, and
`[V2G20-2388]` obliges a public SECC to answer with one response per certificate in its chain

**Version:** everest-core **2026.02.1** (`b61bb12b8`), native build, Debian 13, OpenSSL 3.5.6. Module
`Evse15118D20`, library `lib/everest/iso15118`, `tls_negotiation_strategy: ENFORCE_TLS`, your own test
PKI. Control: `IsoMux` + `EvseV2G` in the same build.

## What we saw

```
openssl s_client -status -tls1_2 -connect [fe80::…%eth0]:50000   → OCSP response: no response sent
openssl s_client -status -tls1_3 -connect [fe80::…%eth0]:50000   → OCSP response: no response sent
```

Both reached the certificate exchange — `TLSv1.2` / `ECDHE-ECDSA-AES128-SHA256` and `TLSv1.3` /
`TLS_AES_256_GCM_SHA384`. Your charger log contains no line about OCSP or `status_request` in either
arm, because there is nothing in that module to log one.

### The control — because "your client did not ask" is the first thing to rule out

Same client, same `-status` flag, against a module in the same build whose TLS layer *does* implement
the extension. `IsoMux`:

```
[INFO] iso_mux:IsoMux :: Incoming TLS connection
[ERRO] iso_mux:IsoMux :: OcspCache::lookup: not in cache: d8817041a94bb65646ea392c812fcb4978ae4cf6
```

`OcspCache::lookup` is reached only from your `status_request` handlers
(`lib/everest/tls/extensions/status_request.cpp:168`, `:250`; the miss is logged at `:117`). It ran,
with the digest of your own SECC leaf. So the extension was on the wire and the client asked correctly —
and `Evse15118D20`, given the same `ClientHello`, said nothing at all.

`IsoMux` answers `no response sent` as well, for the unrelated reason in the other report. Two modules,
the same silence, two different causes, and only one of them has a handler that speaks up about it.

## Where it comes from — three places, each sufficient on its own

**1. It is never requested.** `modules/EVSE/Evse15118D20/charger/ISO15118_chargerImpl.cpp:181-182`:

```cpp
const auto certificate_response = mod->r_security->call_get_leaf_certificate_info(
    types::evse_security::LeafCertificateType::V2G, types::evse_security::EncodingFormat::PEM, false);
                                                                                  // include_ocsp ↑
```

The third argument is `include_ocsp` (`interfaces/evse_security.yaml:153`). `EvseV2G` passes `true` on
its own path; this module passes `false`.

**2. There is nowhere to carry it.** `lib/everest/iso15118/include/iso15118/config.hpp:22-36` —
`SSLConfig` carries the chain, the key, the password, two roots, three flags, a logging path, a backend
and a config string. Eleven members, none for OCSP, so `ISO15118_chargerImpl.cpp:207-223` could not pass
one if it had it.

**3. Nothing would send it.** `connection_ssl.cpp:220-300`, `init_ssl()`, never calls
`SSL_CTX_set_tlsext_status_cb`. Grep across the whole of `lib/everest/iso15118` for `status_request`,
`tlsext_status`, `OCSP` and `ocsp`: **no matches**. The only hits in that tree are `authorityInfoAccess`
lines in test certificate configs.

**And a fourth, outside this module.** Even with (1) set to `true`, `to_everest(CertificateInfo)` drops
the `ocsp` member (`lib/everest/conversions/evse_security/src/conversions.cpp:429`), so the reply would
arrive empty — the other report. That is the ordering point worth stating in both issues.

Worth saying kindly: `lib/everest/tls`, which `EvseV2G` and `IsoMux` use, has `status_request.cpp`, an
`OcspCache`, `status_request_v2` and unit tests for all of it. The capability is in this repository
already. The `-20` stack has its own, younger TLS implementation, and it did not get it.

## Why we think it is worth fixing

- **`[V2G20-2372]`** — the EVCC **shall** include `status_request` in its `ClientHello`, with
  **`[V2G20-2373]`** a zero-length `responder_id_list`. There is no configuration in which a conformant
  `-20` EV does not ask, so this is not a case that only arises with unusual peers.
- **`[V2G20-2388]`** — the **public** SECC shall include an OCSP response for each certificate in the
  chain it sent. Unqualified.
- **`[V2G20-2391]`** — a private SECC **supporting PnC**, likewise. **`[V2G20-1021]`** caps reuse of a
  response at one week, which is what makes a cache the right shape.

We cite requirement identifiers and paraphrase what they oblige rather than quoting; the rule is
[`docs/normative-basis.md`](../normative-basis.md). All `-20` identifiers, no document caveat.

### Two things that cut the other way, and we would rather say them than have you find them

- **`[V2G20-2398]` may cover you.** A private SECC that does **not** support PnC may ignore
  `status_request` and carry on. `Evse15118D20` warns *"Currently Plug&Charge is not supported and
  ignored"* (`ISO15118_chargerImpl.cpp:714`), so a private, non-PnC deployment is exactly the permitted
  case. What we cannot judge from outside is whether this module is meant for public charging — if it
  is, `[V2G20-2388]` applies and the exemption does not.
- **`-20` is softer than `-2` about the consequence.** In ISO 15118-2, `[V2G2-873]` makes an EV that
  asked and got nothing **close the connection** — which is what makes the `EvseV2G` report an
  interoperability failure rather than an audit finding. The `-20` equivalent, `[V2G20-2411]`, says the
  EVCC **may** contact the OCSP responder itself instead. So a `-20` EV meeting your station is not
  obliged to give up.
  <br>It is still obliged to check: **`[V2G20-1240]`** makes the revocation check a `shall`, performed
  via an OCSP response. Stapling exists precisely because an EV on the end of a charging cable may have
  no route to a responder. The honest severity is *"the EV must now do something it may not be able to
  do"*, not *"the handshake fails"* — and that is why we would file this as a gap to close rather than
  as a break.

## Suggested direction

More than one shape is reasonable; which belongs in your tree is yours to choose.

1. **Decide the scope question first.** If `Evse15118D20` is meant for public charging, this is
   `[V2G20-2388]` and needs (2)–(4). If it is meant to stay private and PnC-less, `[V2G20-2398]` covers
   it and a comment beside `init_ssl()` saying so would be worth more than code.
2. **Ask for the data**: `include_ocsp = true` at `ISO15118_chargerImpl.cpp:182`, or switch to
   `get_all_valid_certificates_info` as `EvseV2G` does — and note that this needs the `to_everest` fix
   to produce anything.
3. **Carry it**: an OCSP member on `SSLConfig`, mirroring how the chain and key already travel.
4. **Send it**: `SSL_CTX_set_tlsext_status_cb` in `init_ssl()`. `lib/everest/tls/extensions/` is a
   working implementation of the same thing in the same repository; whether to reuse it or to write a
   smaller one for libiso15118 is a question about how far the two TLS stacks should converge, and that
   is a bigger decision than this issue.
5. **Say something when there is nothing to staple.** The `EvseV2G` path at least warns
   `<n> certificates != <n> OCSP responses`. Here an operator gets silence.

## Not part of this

- **`status_request_v2` / RFC 6961**, which ISO 15118-2 asks for and `lib/everest/tls` implements. This
  report is about `-20`, where RFC 8446's `status_request` per certificate is what applies. If the two
  stacks converge, that question comes with them.
- **What your station does with an EV's OCSP data.** `[V2G20-2407]` says the SECC shall *not* put
  `status_request` in its own `CertificateRequest`; we did not check whether it does, because it sends
  no useful `CertificateRequest` contents at all — [`everest-d20-client-auth.md`](everest-d20-client-auth.md) §2.
- **Whether a real `-20` EV gives up.** `[V2G20-2411]` says it may fetch the response itself, so the
  behaviour is EV-specific and ours is not evidence about anyone else's. We did not run one.

---

## Before sending

- [x] **Measure it, do not infer it.** `OCSP response: no response sent` on TLS 1.2 and TLS 1.3, against
      your station, your PKI, at 2026.02.1 — no EV, no session, one command.
- [x] **Rule out our own client.** Same client, same flag, against `IsoMux`: your own
      `OcspCache::lookup` ran with the digest of your own leaf, so the extension was on the wire.
      Put this in the issue; it is the first objection.
- [x] **Check every line reference against the tree.**
      `ISO15118_chargerImpl.cpp:181-182`, `:207-223`, `:714`; `interfaces/evse_security.yaml:153`;
      `config.hpp:22-36`; `connection_ssl.cpp:220-300`;
      `conversions.cpp:429`; `status_request.cpp:117`, `:168`, `:250` — read from the built 2026.02.1
      source on 2026-08-10.
- [ ] **Ask the scope question in the first paragraph.** *Is `Evse15118D20` intended for public
      charging?* `[V2G20-2398]` is a real exemption and the answer decides whether this is a defect or a
      documentation line.
- [ ] **Say plainly that this is not the other OCSP issue**, and that neither fix alone produces a
      staple. A maintainer who lands the `to_everest` patch has every reason to think this closed.
- [ ] **Do not overstate the consequence.** `-20` lets the EV fetch the response itself; `-2` does not.
      The claim is that the mandatory revocation check is pushed onto an EV that may have no network.
- [ ] **File one issue, this one** — separate from the `to_everest` one.
- [ ] **Post under your own name, in your own words.**
