# 2026-08-15 — trying to run isomux §4's failing case, and finding out why it cannot be run

[`everest-isomux.md`](../../reports/everest-isomux.md) §4 says the multiplexer's TLS server boots with
`trusted_ca_keys support disabled`, reasons the consequence from their source, and carries one open
box: *"§4's failing case has not been run. **Two roots and an EV that sends `trusted_ca_keys`.**"*

**It still has not been run, and now the report can say why instead of leaving it open.** Two things
were measured on the way, and one of them is what §4 most needed.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `IsoMux` + `EvseV2G`, `config-mux-tls-ours.yaml` |
| Attempted | two V2G roots installed with a valid SECC chain under each, then a client naming one of them |
| Outcome | **the client half is unrunnable here — and the reason is §2 of the same report** |

## What was measured, and it is the falsification test §4 was missing

The 2026-08-10 boot measurement was taken on their **stock single-root** PKI, where *"No trust anchors
for certificate"* invites the obvious rebuttal: *then configure one.* So this run configured one — a
second V2G root minted in their own test-PKI style, a SECC chain under it, installed beside the first,
with chain A verifying under root A:

```
$ openssl verify -CAfile ca/v2g/V2G_ROOT_CA.pem \
      -untrusted client/cso/CPO_CERT_CHAIN.pem client/cso/SECC_LEAF.pem
client/cso/SECC_LEAF.pem: OK
```

The station then booted with the same two lines, unchanged:

```
16:39:16.106  iso_mux:IsoMux  TLS server on eth0 is listening on port …:64110
16:39:16.153  iso_mux:IsoMux  No trust anchors for certificate: C:DE CN:SECCCert DC:CPO O:EVerest
16:39:16.153  iso_mux:IsoMux  trusted_ca_keys support disabled
```

**That is the point of the arm.** §4's mechanism — `get_leaf_certificate_info` runs with
`include_root = false`, so `trust_anchor_pem` is never set — predicts that adding roots changes nothing,
because the module never asks for the one it has. It changed nothing. The *misconfiguration* reading is
now closed by measurement rather than by argument.

Their own source says the same thing one line above where it happens, and this is the friendliest way
into the issue:

```cpp
// lib/everest/tls/src/tls.cpp:1042-1045
/*
 * If there are no trust anchors then the chain can't be verified
 * it also means that trusted_ca_keys can't be supported for the
 * chain.
 */
```

## And why the client half cannot be run — §2 closes the door on §4

Three TLS 1.3 connection attempts, three identical refusals from their own log:

```
iso_mux:IsoMux  Incoming TLS connection
iso_mux:IsoMux  SSL_read: -1 1
  error:0A000102:SSL routines:tls_early_post_process_client_hello:unsupported protocol
```

`IsoMux` refuses TLS 1.3 at the ClientHello. That is §2 of this report — *TLS is capped at 1.2* —
arriving from the wire instead of from a reading, and its mechanism is four lines up the same file:

```cpp
// lib/everest/tls/src/tls.cpp:438-447
SSL_CTX_set_min_proto_version(ctx, TLS1_2_VERSION);
if ((ciphersuites != nullptr) && (ciphersuites[0] == '\0')) {
    // no cipher suites configured - don't use TLS 1.3
    SSL_CTX_set_max_proto_version(ctx, TLS1_2_VERSION);
}
```

**The two sections are coupled, and the report does not say so.** The instrument that measured chain
selection against `Evse15118D20` on 2026-08-12 is `openssl s_client -requestCAfile`, which sets the
**TLS 1.3** `certificate_authorities` extension — and TLS 1.3 is exactly what `IsoMux` will not accept.
The extension §4 is actually about, `trusted_ca_keys`, is the **TLS 1.2** one (RFC 6066) that
`[V2G2-651]` obliges every `-2` EV to send:

| client | can send `trusted_ca_keys`? |
|---|---|
| `openssl s_client` | **no** — there is no flag for RFC 6066 `trusted_ca_keys` |
| our own EVCC | **no** — `grep trusted_ca_keys` matches nothing in our stack |
| their own `Evse15118D20` route | irrelevant — different module, and TLS 1.3 |

So the failing case needs a `-2` EV that sends the extension, and this project does not have one. That
is a statement about our instruments, not about their code, and it belongs in the report as such.

## What this changes in the filing

- §4's box goes from *"has not been run"* to **"cannot be run with any client here, and here is why"**,
  which is a better sentence than the one it replaces because it names the obstacle.
- §4 gains the **two-root arm**, which closes the *"then configure a trust anchor"* reading.
- §2 gains a wire measurement it did not have: the ClientHello refusal, three times.
- And the report should say that **§2 and §4 are coupled**: while the cap stands, the extension cannot
  be exercised even by a conformant `-2` EV, so fixing §4 alone would be unobservable.

## What was not done

**No patch to their tree, and no client written.** Writing `trusted_ca_keys` into our EVCC to measure
their handling of it is a day of work on our side to produce one line of their log, and the mechanism is
already visible in their own comment. If it is ever wanted, `[V2G2-651]` makes it a conformance gap of
**ours** too, and that is the honest reason to do it — not this report.

**Their PKI is back as it was.** Only files were added, never modified; all four are removed and chain A
verifies under root A again, root `F6:BA:4E:BD…`. Worth recording that the run's own backup was
**contaminated** — the second attempt backed up a tree the first had already modified, so the restore
had to be *remove what was added* rather than *put the backup back*. A backup taken inside a script that
has already run once is not a backup.

## Reproduce

```bash
OUT=~/everest/pki-rootb bash tools/interop-everest/mint-second-root.sh
cp ~/everest/pki-rootb/V2G_ROOT_CA_B.pem     <certs>/ca/v2g/
cp ~/everest/pki-rootb/SECC_LEAF_B.{pem,key} <certs>/client/cso/
cp ~/everest/pki-rootb/CPO_CERT_CHAIN_B.pem  <certs>/client/cso/
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-mux-tls-ours.yaml
```

Their boot lines say the rest. To probe the endpoint,
[`chain-selection-arm.sh`](../../../tools/interop-everest/chain-selection-arm.sh) now takes
`STATION_EP` and matches both log wordings — `main` says *"Start TLS server"*, 2026.02.1 says *"TLS
server on … is listening on port"*, and matching only the first is how the instrument silently produced
an empty endpoint the first time it met a release build. **Take `IsoMux`'s line, not the last one:**
`EvseV2G` logs the same sentence four milliseconds later for its internal `lo` leg, and connecting there
measures the backend rather than the mux.

## Artifacts

[`their-manager.log`](their-manager.log) — the boot with two roots installed, and the three refusals.

Offline gate: **1 405 green**, four assemblies, exit code 0.

## Next

- **Nothing here.** The remaining way to close §4 is a `-2` EV that sends `trusted_ca_keys`, which is a
  capability of ours to build and is worth its own decision rather than being smuggled in as a probe.
