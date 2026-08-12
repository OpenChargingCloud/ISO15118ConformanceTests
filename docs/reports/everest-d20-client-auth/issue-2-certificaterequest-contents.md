# Issue 2 of 3 — the `-20` `CertificateRequest` carries no `certificate_authorities`, and the TLS profile stops at cipher suites

> **Post this as-is under your own name, or edit freely.** It is one of three
> ([index](README.md)). This one is the conformance-profile issue; it survives whatever you decide
> about issue 1.

**Title:** `Evse15118D20` sets exactly the Table 6 cipher suites and then leaves the
`certificate_authorities` extension, the signature-algorithm list and the named-group preference at
OpenSSL's defaults

**Version:** everest-core `main` (`ebcd36d`) built from source, `Evse15118D20`,
`config-sil-dc-d20.yaml` as shipped, your own test PKI, OpenSSL 3.5.6. Also measured on `2026.02.1`.

## The `CertificateRequest`, byte for byte

Your station sends one when the client offers TLS 1.3 (which is issue 1's subject). Captured with
`openssl s_client -tls1_3 -msg`:

```
<<< TLS 1.3, Handshake [length 003e], CertificateRequest
    0d 00 00 3a 00 00 37 00 0d 00 2a 00 28 09 05 09
    06 09 04 04 03 05 03 06 03 08 07 08 08 08 1a 08
    1b 08 1c 08 09 08 0a 08 0b 08 04 08 05 08 06 04
    01 05 01 06 01 00 1b 00 05 04 00 01 00 03
```

which parses exactly:

| bytes | |
|---|---|
| `0d 00 00 3a` | CertificateRequest, body 58 |
| `00` | `certificate_request_context`: empty |
| `00 37` | extensions block, 55 bytes |
| `00 0d 00 2a` … | extension **13** `signature_algorithms`, 42 bytes — inner list `00 28` = 40 bytes = **20 algorithms** |
| `00 1b 00 05 04 00 01 00 03` | extension **27** `compress_certificate` (RFC 8879) |

1 + 2 + 55 = 58 and 46 + 9 = 55, so the message is fully accounted for. **Two extensions, and neither
is number 47** — `certificate_authorities`, RFC 8446 §4.2.4.

## Against the profile

| | what `-20` asks | your handshake |
|---|---|---|
| cipher suites, Table 6 | `TLS_AES_256_GCM_SHA384`, `TLS_CHACHA20_POLY1305_SHA256`, in that order | **exactly that** |
| `certificate_authorities` | `[V2G20-2401]`/`[V2G20-2402]`: the V2G and/or OEM roots the SECC holds | **absent** |
| signature algorithms, Table 8 | `[V2G20-1667]`: two entries, in Table 8's order | OpenSSL's default list — `id-ml-dsa-65`, `id-ml-dsa-87`, `id-ml-dsa-44`, ECDSA ×3, `ed25519`, `ed448`, brainpool ×3, RSA-PSS ×6, RSA ×3 |
| named group, Table 7 | `[V2G20-2460]`: `secp521r1`, then `x448` | `Negotiated TLS1.3 group: X25519MLKEM768` |

**The cipher suites are right and that is worth saying** — the profile was clearly consulted once. The
other three were not carried across.

**One number moved between our two runs, and it is the useful one.** The signature-algorithm count went
**19 → 20**. Nothing in your code changed; the linked OpenSSL's defaults did. A conformance property
that shifts when a dependency is upgraded is not being met on purpose.

`[V2G20-2404]` is the only exemption for an empty authority list, and it is for a SECC that holds no
roots. This one holds two — it loads them and logs no complaint.

## Where it comes from

Nothing configures them. In the whole of `lib/everest/tls/src/tls.cpp` there is **no client-CA-list
call of any kind** — no `SSL_CTX_set_client_CA_list`, no `SSL_CTX_add_client_CA`, no
`SSL_CTX_set0_CA_list` — and no `set1_groups`/`set1_curves` and no `set1_sigalgs` on the server path.
`configure_verify_locations` (`tls.cpp:667-695`) loads the V2G and MO roots into the **verify store**,
which is a different thing from the list advertised in the `CertificateRequest`.

### Two things that look like this and are not

Both are in your tree and both would make a reasonable person say *"but we do handle
`certificate_authorities`"*:

- **`ChainConfig`'s doc comment** (`iso15118/config.hpp:26-27`) describes multi-chain selection driven
  by *the peer's* `certificate_authorities`. That is the server **reading** what the EV sent — the
  opposite direction from this issue, and the subject of issue 3.
- **`m_server_trusted_ca_keys.init_ssl(ctx)`** (`tls.cpp:1087`) initialises `trusted_ca_keys`,
  **RFC 6066** — a different extension, and one ISO 15118-20 does not use at all.

## Suggested fix

1. `SSL_CTX_set_client_CA_list(ctx, …)` built from the same two root files the verify store already
   loads. `[V2G20-2403]` specifies which RDNs belong in each DistinguishedName and in what order;
   `[V2G20-2404]` says an empty list is correct only when there are no roots.
2. `SSL_CTX_set1_groups_list(ctx, "secp521r1:X448")` and
   `SSL_CTX_set1_sigalgs_list(ctx, "ecdsa_secp521r1_sha512:ed448")`, beside the `set_ciphersuites`
   calls that already carry Table 6 — and if you would rather keep the defaults reachable, gate them on
   `enforce_tls_1_3` as the rest of that function is.
3. A comment naming Tables 6, 7 and 8 beside those four calls would keep the next person from having
   to rediscover which values are deliberate.

## Severity, honestly

**The `certificate_authorities` one has a practical cost**: `[V2G20-2401]` exists so the EV can pick
which of its certificates to present, and an EV holding certificates under more than one root has no
way to guess well.

**The other two are conformance rather than interop** and we say so — OpenSSL negotiated something
perfectly sound in security terms. They are still `shall`s, the fix is one call each, and a test house
will look at both.

## The other two issues

- **Issue 1** — *whether* the `CertificateRequest` is sent at all: only when the EV offers TLS 1.3.
- **Issue 3** — which chain the server presents, the same extension read in the other direction.

---

### Before you post

- [ ] Re-read the `main` line numbers on the day.
- [ ] Keep the byte parse. It is the difference between *"openssl says no CA names"* and *"the message
      has two extensions and 47 is not one of them"*.
- [ ] Keep the sentence crediting the cipher suites. Overstating a small finding is how the next one
      gets ignored.
