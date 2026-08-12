# Issue 3 of 3 — the station never reads the EV's `certificate_authorities`, so it serves a chain the EV cannot verify

> **Post this as-is under your own name, or edit freely.** It is one of three
> ([index](README.md)). This one has the strongest evidence in the set and the largest fix.

**Title:** `lib/everest/tls` fixes the server chain at `cfg.chains[0]` and never reads the
`certificate_authorities` extension the EV is obliged to send, so a station holding two roots presents
the wrong one

**Version:** everest-core `main` (`ebcd36d`) built from source, `Evse15118D20`,
`config-sil-dc-d20.yaml` as shipped, **two V2G roots installed with a valid SECC chain under each**.

## What happens

Three arms, same station, same process. One variable: what the client puts in
`certificate_authorities`.

| arm | the EV asks for | the station serves |
|---|---|---|
| **A** | root **A** — and you hold a valid chain under it | `SECCCert-B` ← `CPOSubCA-B` ← **`V2GRootCA-B`** |
| **B** | root **B** | the same chain B |
| **C** (control) | *no extension at all* | the same chain B |

Byte-identical chains in all three, and `Verify return code: 20` in every arm is our client refusing
what it got.

**Arm A is the finding on its own**: the EV named a root, you hold a valid chain under exactly that
root, and you sent the other one. Arm C shows the request has no influence at all.

**The right answer was available** — this is not an impossibility argument:

```
$ openssl verify -CAfile ca/v2g/V2G_ROOT_CA.pem \
      -untrusted client/cso/CPO_CERT_CHAIN.pem client/cso/SECC_LEAF.pem
client/cso/SECC_LEAF.pem: OK
```

and your own log names the one that went to the TLS layer instead:

```
evse_security:E :: Requesting leaf certificate info: V2G
evse_security:E :: Found valid leaf: [".../client/cso/CPO_CERT_CHAIN_B.pem"]
```

**One leaf, chosen before any `ClientHello` exists.** `get_leaf_certificate_info` runs while the TLS
server is being built in response to the SDP request; the EV's list arrives a flight later with nowhere
to go.

## What the standard asks

`-20` runs the whole certificate-authority conversation over RFC 8446's `certificate_authorities`, and
this is the direction issue 2 is *not* about:

- **`[V2G20-1006]`** — an EVCC not in CPM4PE **shall** list every V2G and PE private root it holds in a
  `certificate_authorities` extension in its `ClientHello`. Unconditional — the data is always there.
- **`[V2G20-1007]`** — a public SECC **shall** send a chain up to a root **the EV named**.
- **`[V2G20-2379]`** — when the EV's list is non-empty, the SECC **shall** use the received
  DistinguishedNames to choose a chain originating from one of them.
- **`[V2G20-2378]`** — free choice is allowed **only** when the list is empty.
- **`[V2G20-2382]`**–**`[V2G20-2384]`** say the same for a private SECC not in CPM4PE;
  **`[V2G20-2385]`** is the CPM4PE exception.

Requirement identifiers and a paraphrase of what they oblige; we do not reproduce the text. All `-20`,
no revision caveat.

## Where it comes from

Nothing reads it. `lib/everest/tls/src/tls.cpp` contains no `SSL_get0_peer_CA_list`, no
`TLSEXT_TYPE_certificate_authorities`, no DistinguishedName handling of any kind. The chain is fixed at
init:

```cpp
// tls.cpp:1052-1054
// use the first server chain
const ssl_ctx_params params{true, cfg.ciphersuites, cfg.cipher_list, true, cfg.enforce_tls_1_3};
result = configure_ssl_ctx(ctx, cfg.chains[0], params);
```

The one selection mechanism that exists — `ServerTrustedCaKeys` — is driven by **`trusted_ca_keys`**,
RFC 6066, the ISO 15118-**2** extension, which plays no part in `-20`. So the `-20` station inherited a
selector for a protocol it does not speak and has none for the one it does.

And `Evse15118D20` gives it one chain to choose from anyway — a single `chains.push_back` in
`ISO15118_chargerImpl.cpp`.

## Why this is worth raising

**Because you have already written down that it should work this way.** `ChainConfig`'s own doc comment
says multiple chains exist to support *TLS 1.3 multi-chain selection driven by the peer's
`certificate_authorities` extension, RFC 8446 §4.2.4* (`iso15118/config.hpp:26-27`). The vector is
there and the intent is recorded; the selection step between them is what is missing.

**And because of where it bites.** With one root, a station that ignores the extension is
indistinguishable from one that honours it — there is nothing to choose. It bites when an operator
holds two: mid-rotation, or serving two roots. That is the configuration nobody tests, and the arms
above are it.

## Suggested fix

1. **Read the list.** In the certificate callback, `SSL_get0_peer_CA_list()` gives the
   DistinguishedNames; match against each configured chain's root issuer and install the first that
   matches, falling back to `chains[0]` when the list is empty — which is `[V2G20-2378]` exactly.
2. **Then give the module more than one chain to offer**, or the selection has nothing to work with.
   That is a `libevse-security` question rather than a TLS one: `get_leaf_certificate_info` returns the
   newest single chain, not all valid ones.

(1) without (2) is still worth having — it makes the station correct for the empty-list case by
construction and puts the mechanism where the next chain can use it.

## The other two issues

- **Issue 1** — *whether* the EV is asked to authenticate.
- **Issue 2** — what the `CertificateRequest` carries, the same extension in the other direction.

Fixing either leaves this one standing.

---

### Before you post

- [ ] Re-read the `main` line numbers on the day.
- [ ] Include arm C. Without the no-extension control, *"it served chain B"* reads as a coincidence.
- [ ] Say that the second root and its chain were minted for the test — an operator's own two roots is
      the realistic case, and ours is a stand-in for it.
- [ ] Their own `ChainConfig` comment is the friendliest way in. Lead with it rather than with the
      clause.
