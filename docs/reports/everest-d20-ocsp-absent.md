# Draft report to EVerest — `Evse15118D20` has the OCSP stapling machinery wired up and passes it an empty list

Status: **draft, not sent.** Measured 2026-08-10 against everest-core **2026.02.1** (`b61bb12b8`) built
from source: `openssl s_client -status` against their `-20` station on both TLS versions, plus a control
that proves the request reached them. No EV, no session, no client PKI. **Re-argued 2026-08-11 against
`main`** — the measurement stands, the reason for it changed completely. Post it under your own name;
see *Before sending* at the bottom.

> **What changed, and why this is now a better issue than it was.** This report used to say the `-20`
> stack has *no OCSP handling of any kind* — not requested, nowhere to carry it, nothing to send it.
> On `main` (`ebcd36d`) **two of those three are no longer true**: the `-20` TLS layer was rebased onto
> `lib/everest/tls`, which is the implementation this report used to point at as the one that already
> works, and `SSLConfig` grew a `chains` vector whose `ChainConfig` carries `ocsp_response_files`.
>
> The observable result is unchanged — still no staple — but the ask has gone from *"implement OCSP"*
> to **three one-line changes in three files, which only work in a particular order**. That is a
> materially easier thing to agree to, and a materially easier thing to get wrong. The order is the
> report now.
>
> Read from the source on 2026-08-11; **not re-measured against a `main` build**. Say so if you post it.
> ([Audit notes](../interop-runs/2026-08-11-reports-upstream-audit/notes.md).)

**This is a different issue from
[`everest-evse-security-ocsp-dropped.md`](everest-evse-security-ocsp-dropped.md), and the relationship
is now the whole point.** That one is a lost struct member on the conversion boundary — the data
libevse-security collected never reaches the caller. This one is a module that does not ask for the
data and does not pass it on. **Landing either patch alone still produces no staple**, which is
exactly the trap a maintainer closing one of them would fall into.

Two other reports go to the same module:
[`everest-d20-client-auth.md`](everest-d20-client-auth.md) — the same TLS setup, and it needs the same
re-anchoring against `lib/everest/tls` — and [`everest-d20-trust-anchor.md`](everest-d20-trust-anchor.md).

---

**Title:** `Evse15118D20` asks libevse-security for its certificate with `include_ocsp = false` and
hands `lib/everest/tls` an empty `ocsp_response_files`, so the station never staples — `[V2G20-2372]`
makes every `-20` EV ask, `[V2G20-2388]` obliges a public SECC to answer

**Version:** measured on everest-core **2026.02.1** (`b61bb12b8`), native build, Debian 13,
OpenSSL 3.5.6, `tls_negotiation_strategy: ENFORCE_TLS`, your own test PKI; control `IsoMux` +
`EvseV2G` in the same build. The code below is read from **`main` (`ebcd36d`, 2026-08-11)** — source
only, not re-run.

## What we saw

```
openssl s_client -status -tls1_2 -connect [fe80::…%eth0]:50000   → OCSP response: no response sent
openssl s_client -status -tls1_3 -connect [fe80::…%eth0]:50000   → OCSP response: no response sent
```

Both reached the certificate exchange — `TLSv1.2` / `ECDHE-ECDSA-AES128-SHA256` and `TLSv1.3` /
`TLS_AES_256_GCM_SHA384`. On 2026.02.1 the charger log contained no line about OCSP or `status_request`
in either arm.

### The control — because "your client did not ask" is the first thing to rule out

Same client, same `-status` flag, against a module in the same build whose TLS layer *does* implement
the extension. `IsoMux`:

```
[INFO] iso_mux:IsoMux :: Incoming TLS connection
[ERRO] iso_mux:IsoMux :: OcspCache::lookup: not in cache: d8817041a94bb65646ea392c812fcb4978ae4cf6
```

`OcspCache::lookup` is reached only from your `status_request` handlers. It ran, with the digest of
your own SECC leaf. So the extension was on the wire and the client asked correctly — and
`Evse15118D20`, given the same `ClientHello`, said nothing at all.

## Where it comes from on `main` — the machinery is yours and it is complete

**The server side is done.** `ServerStatusRequestV2::init_ssl` (`lib/everest/tls/extensions/status_request.cpp:141`)
installs the callback; `status_request_cb` (`:184`) looks the certificate digest up in the `OcspCache`
and returns `SSL_TLSEXT_ERR_OK` with the response, or `SSL_TLSEXT_ERR_NOACK` when there is none
(`:190`, `:213`, `:221`). `Server::init_ssl` calls `m_status_request_v2.init_ssl(ctx)`
(`lib/everest/tls/src/tls.cpp:1086`) for **every** server, including this one. The cache is filled in
`Server::init_certificates` (`tls.cpp:1106-1137`) from `ChainConfig::ocsp_response_files`.

**The carrier is done.** `iso15118/config.hpp:29-34`:

```cpp
struct ChainConfig {
    std::string path_certificate_chain;
    std::string path_certificate_key;
    std::optional<std::string> private_key_password{};
    std::vector<std::string> ocsp_response_files{};    //!< OCSP DER files in chain order
};
```

**What is missing is two values in one file.** `modules/EVSE/Evse15118D20/charger/ISO15118_chargerImpl.cpp`:

```cpp
// :243-244 — the data is never requested
const auto certificate_response = mod->r_security->call_get_leaf_certificate_info(
    types::evse_security::LeafCertificateType::V2G, types::evse_security::EncodingFormat::PEM, false);
//                                                                                            ^^^^^ include_ocsp

// :276-281 — and an empty list is passed on
ssl_for_controller.chains.push_back(iso15118::config::ChainConfig{
    path_chain,
    certificate_info.key,
    certificate_info.password,
    {}, // ocsp_response_files — none for the single-chain leaf path
});
```

**And a third value in a third file.** Even with both of those changed, `to_everest(CertificateInfo)`
(`lib/everest/conversions/evse_security/src/conversions.cpp:429`) copies six of seven members and drops
`ocsp`, so `certificate_info` arrives without it — that is the sibling report, and it is **still
present on `main`**.

### Your own log already says this, once a start

`tls.cpp:1124-1137` compares the two lists and warns when they disagree:

```cpp
if (certs.size() == i.ocsp_response_files.size()) {
    …
} else {
    log_warning("<n> certificates != <n> OCSP responses");
}
```

An `Evse15118D20` chain has at least one certificate and its `ocsp_response_files` is `{}`, so **that
warning should fire on every start of every `-20` station on `main`**. We have not seen it — this is a
prediction from the source, not an observation, and it is the cheapest thing in this report for you to
check: one restart, one grep. If it is there, it is your own code reporting this defect to your own
operators already.

## Why we think it is worth fixing

- **`[V2G20-2372]`** — the EVCC **shall** include `status_request` in its `ClientHello`, with
  **`[V2G20-2373]`** a zero-length `responder_id_list`. There is no configuration in which a conformant
  `-20` EV does not ask, so this is not a case that only arises with unusual peers.
- **`[V2G20-2388]`** — the **public** SECC shall include an OCSP response for each certificate in the
  chain it sent. Unqualified.
- **`[V2G20-2391]`** — a private SECC **supporting PnC**, likewise. **`[V2G20-1021]`** caps reuse of a
  response at one week, which is what makes a cache the right shape — and you have the cache.

We cite requirement identifiers and paraphrase what they oblige rather than quoting; the rule is
[`docs/normative-basis.md`](../normative-basis.md). All `-20` identifiers, no document caveat.

### Two things that cut the other way, and we would rather say them than have you find them

- **`[V2G20-2398]` may cover you.** A private SECC that does **not** support PnC may ignore
  `status_request` and carry on. `Evse15118D20` warns *"Currently Plug&Charge is not supported and
  ignored"*, so a private, non-PnC deployment is exactly the permitted case. What we cannot judge from
  outside is whether this module is meant for public charging — if it is, `[V2G20-2388]` applies and
  the exemption does not.
- **`-20` is softer than `-2` about the consequence.** In ISO 15118-2, `[V2G2-873]` makes an EV that
  asked and got nothing **close the connection**. The `-20` equivalent, `[V2G20-2411]`, says the EVCC
  **may** contact the OCSP responder itself instead. So a `-20` EV meeting your station is not obliged
  to give up.
  <br>It is still obliged to check: **`[V2G20-1240]`** makes the revocation check a `shall`, performed
  via an OCSP response. Stapling exists precisely because an EV on the end of a charging cable may have
  no route to a responder. The honest severity is *"the EV must now do something it may not be able to
  do"*, not *"the handshake fails"*.

## Suggested direction — and the order matters more than any of the steps

Three one-line changes in three files. **Any one or two of them alone still produces no staple**, which
is why we would rather describe the chain than send a patch for one link in it.

| | change | file | alone? |
|---|---|---|---|
| 1 | `to_everest` copies `ocsp` | `conversions.cpp:429` | nothing — nobody asks for it yet |
| 2 | `include_ocsp = false` → `true` | `ISO15118_chargerImpl.cpp:244` | nothing — the data is dropped at (1) |
| 3 | pass `certificate_info.ocsp` instead of `{}` | `ISO15118_chargerImpl.cpp:280` | nothing — the field is empty without (1) and (2) |

With all three, the existing `OcspCache` → `status_request_cb` path should staple with no further work.
**We have not built that and cannot claim it**; the claim is that the three gaps are what stands
between the current behaviour and the machinery already in the tree.

One decision comes before all of them: **is `Evse15118D20` meant for public charging?** If it is meant
to stay private and PnC-less, `[V2G20-2398]` covers it, and a comment where the empty list is passed
would be worth more than the code — it would also stop the next person filing this.

## Not part of this

- **`status_request_v2` / RFC 6961**, which ISO 15118-2 asks for and `lib/everest/tls` implements. This
  report is about `-20`, where RFC 8446's `status_request` per certificate is what applies.
- **What your station does with an EV's OCSP data.** `[V2G20-2407]` says the SECC shall *not* put
  `status_request` in its own `CertificateRequest`; we did not check, because it sends no useful
  `CertificateRequest` contents at all — [`everest-d20-client-auth.md`](everest-d20-client-auth.md) §2.
- **Whether a real `-20` EV gives up.** `[V2G20-2411]` says it may fetch the response itself, so the
  behaviour is EV-specific and ours is not evidence about anyone else's. We did not run one.
- **Multi-chain selection.** `ChainConfig` is a vector on `main` and `Server::init_ssl` uses
  `cfg.chains[0]` with the comment *"use the first server chain"* (`tls.cpp:1052`). Whatever that is
  for, it is not this, and we are not reporting it.

---

## Before sending

- [x] **Measure it, do not infer it.** `OCSP response: no response sent` on TLS 1.2 and TLS 1.3, against
      your station, your PKI, at 2026.02.1 — no EV, no session, one command.
- [x] **Rule out our own client.** Same client, same flag, against `IsoMux`: your own
      `OcspCache::lookup` ran with the digest of your own leaf, so the extension was on the wire.
      Put this in the issue; it is the first objection.
- [x] **Check the argument against the current tree, not just the line numbers — done 2026-08-11, and
      two thirds of it had to go.** The "three absences" this report was built on are now one module
      passing two empty values into machinery that works. Filing the old version would have been
      answered *"that is not where it lives any more"*, and rightly.
- [x] **Check every line reference.** The 2026.02.1 references were verified on 2026-08-10 and again on
      2026-08-11 in the sweep over every `file:line` in this directory. **The `main` references in
      *Where it comes from* were read on 2026-08-11** — `main` moves daily; re-read them on the day you
      post, and note that this report cites two revisions and labels which is which.
- [ ] **Lead with the empty list, not with the standard.** *Your `-20` module passes an empty
      `ocsp_response_files` into a TLS server that knows how to staple.* One sentence, it is concrete,
      and it is theirs.
- [ ] **Check the startup warning before posting.** If `<n> certificates != <n> OCSP responses` really
      does appear on every `-20` start, that belongs in the first paragraph and it is worth more than
      the requirement citations. If it does **not**, find out why before claiming the rest — we would be
      wrong about the wiring.
- [ ] **Ask the scope question early.** *Is `Evse15118D20` intended for public charging?*
      `[V2G20-2398]` is a real exemption and the answer decides whether this is a defect or a
      documentation line.
- [ ] **Say plainly that this is not the other OCSP issue, and give the order.** A maintainer who lands
      the `to_everest` patch has every reason to think this closed. The table is there to stop that.
- [ ] **Do not overstate the consequence.** `-20` lets the EV fetch the response itself; `-2` does not.
      The claim is that the mandatory revocation check is pushed onto an EV that may have no network.
- [ ] **File one issue, this one** — separate from the `to_everest` one, and cross-reference both ways.
- [ ] **Post under your own name, in your own words.**
