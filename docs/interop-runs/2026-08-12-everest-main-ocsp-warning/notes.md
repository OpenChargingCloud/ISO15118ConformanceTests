# 2026-08-12 — the predicted OCSP warning, measured on an everest-core `main` station

**The first run in this directory against everest-core `main` rather than a release tag.** It exists to
answer one checklist item in [`everest-d20-ocsp-absent`](../../reports/everest-d20-ocsp-absent.md):
*does the `<n> certificates != <n> OCSP responses` warning we predicted from the source actually
appear?*

**It does. 2 of 2, and not where the report first said it would.** It also produced a second
observation the run was not looking for.

everest-core `main` **`ebcd36d`** built from source in a worktree, installed to its own prefix so the
`2026.02.1` build every other measurement in this project rests on stays untouched.
Config `config-sil-dc-d20.yaml` as shipped, their own test PKI copied in.

## The result

```
[INFO] iso15118_charge  :: Got SDP request from fe80::215:5dff:fe6b:3d4%eth0
[INFO] iso15118_charge  :: Start TLS server [fe80::215:5dff:fe6b:3d4%eth0]:50000
<n> certificates != <n> OCSP responses
No trust anchors for certificate: C:DE CN:SECCCert DC:CPO O:EVerest
trusted_ca_keys support disabled
```

Twice, from two independent plug-ins, counted rather than eyeballed: the warning count went 0 → 1 → 2,
one per TLS session setup ([`station-tls-session-starts.log`](station-tls-session-starts.log)).

So the report's claim moves from *read* to *measured*, and its suggested first check for a maintainer —
*"one SDP request, one grep"* — is now something this project has done rather than something it asked
somebody else to do.

## Two things the reading got wrong first, and how

Both were corrected **before** the station was started, by tracing the call graph rather than the leaf
line. Recording them because the run then confirmed the corrected version, which is the only reason the
result is worth anything.

**1. "On every start of every `-20` station" was wrong.** `ConnectionSSL` is constructed in
`TbdController::handle_sdp_server_input()` (`tbd_controller.cpp:357-367`), not at process start. At
startup the log contains **no OCSP line at all** — verified before plugging anything in, and the
original phrasing would have been falsified by that alone.

**2. An SDP request is not enough either.** The first probe was answered
*"Ignoring SDP request because dlink is not ready"* — the guard at the top of the same function, ahead
of the connection factory. The warning needs a **plugged-in car and a TLS SDP request**, which is one
step further in than either version of the claim.

Their own SIL car never sent an SDP request in any run here — SLAC matched, D-LINK went ready, and the
station's 18-second `V2G communication setup timeout` expired unused. Rather than debug their EV, the
probe sends its own SDP datagram inside the window, which is a smaller instrument and made the
`dlink is not ready` guard visible in the first place.

## Also seen — and it is somebody else's report

`trusted_ca_keys support disabled` on **`Evse15118D20`**, once per TLS session, on `main`.

That line is the subject of [`everest-isomux.md`](../../reports/everest-isomux.md) §4, where it was
found on the **multiplexer**. On `main` it is true of the `-20` station as well, and the cause is
visible three lines up in the same log: `No trust anchors for certificate: … CN:SECCCert …`.
`Server::init_certificates` only adds a chain to the `trusted_ca_keys` set when that chain has trust
anchors (`tls.cpp:1145-1167`); with none, it warns and skips, and an empty set disables the extension
(`tls.cpp:1173-1175`).

Why it is empty here is structural rather than a configuration mistake: `iso15118::config::ChainConfig`
has **four members** — chain path, key path, key password, OCSP files — and **no trust-anchor member**
(`config.hpp:29-34`). So the `-20` module has nothing to fill it with.

**Not filed, and deliberately not.** `[V2G2-651]`, which the `IsoMux` report cites, is an ISO 15118-2
requirement, and `trusted_ca_keys` is RFC 6066 — the `-20` profile's certificate-authority signalling
is RFC 8446's `certificate_authorities`, a different extension, which is
[`everest-d20-client-auth`](../../reports/everest-d20-client-auth.md) §2's subject. **Whether a `-20`
SECC is obliged to support `trusted_ca_keys` at all is a question this run does not answer**, and
answering it needs the requirement text rather than another session. Until then it belongs here, as an
observation with a cause, and as a note on the `IsoMux` report that its §4 has a `-20` sibling.

## What this cost, and the one thing worth copying

A `main` build, because `dependencies.yaml` differs from the tag by 130 lines and nothing could be
reused. Built in a **git worktree** with its own install prefix — the `2026.02.1` tree and its `dist/`
are what every other EVerest measurement in this project rests on, and switching that checkout to
`main` to save twenty minutes would have quietly invalidated the baseline for all of them.

## Reproduce

```bash
# one plug-in, one SDP request asking for TLS, count the warning before and after
bash tools/interop-everest/ocsp-warning-probe.sh
```
