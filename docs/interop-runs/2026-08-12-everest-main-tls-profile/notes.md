# 2026-08-12 — §2 re-measured on `main`, and this time at the byte level

`client-auth` §2 was measured on **`2026.02.1`** from an `openssl s_client` summary. Re-run against
everest-core `main` (**`ebcd36d`**), `Evse15118D20`, `config-sil-dc-d20.yaml` as shipped, stock PKI.
**All three findings reproduce** — and adding `-msg` turned the first one from an inference into a
parse.

## The CertificateRequest, byte for byte

The station only sends one when the client offers TLS 1.3 (that is §1), so this is the TLS 1.3 arm; it
ends in `certificate required` because we present none, long after the message below has been read
([`certificaterequest.bytes.log`](certificaterequest.bytes.log)):

```
<<< TLS 1.3, Handshake [length 003e], CertificateRequest
    0d 00 00 3a 00 00 37 00 0d 00 2a 00 28 09 05 09
    06 09 04 04 03 05 03 06 03 08 07 08 08 08 1a 08
    1b 08 1c 08 09 08 0a 08 0b 08 04 08 05 08 06 04
    01 05 01 06 01 00 1b 00 05 04 00 01 00 03
```

parses exactly:

| bytes | meaning |
|---|---|
| `0d 00 00 3a` | CertificateRequest, body 58 bytes |
| `00` | `certificate_request_context`: empty |
| `00 37` | extensions block, 55 bytes |
| `00 0d 00 2a` … | extension **13** `signature_algorithms`, 42 bytes — inner list `00 28` = 40 bytes = **20 algorithms** |
| `00 1b 00 05 04 00 01 00 03` | extension **27** `compress_certificate` (RFC 8879) |

1 + 2 + 55 = 58 and 46 + 9 = 55, so the message is fully accounted for. **Two extensions, and neither
is number 47** — `certificate_authorities`, RFC 8446 §4.2.4, the one `[V2G20-2401]` requires. That is
the finding as a byte count rather than as openssl's *"No client certificate CA names sent"*, which the
same transcript also prints.

## The other two

| | `[V2G20-…]` | measured on `main` |
|---|---|---|
| signature algorithms | `[V2G20-1667]`, Table 8: two entries in Table 8's order | **20 entries**, OpenSSL's default list — `id-ml-dsa-65`, `id-ml-dsa-87`, `id-ml-dsa-44`, ECDSA ×3, `ed25519`, `ed448`, brainpool ×3, RSA-PSS ×6, RSA ×3 |
| named group | `[V2G20-2460]`, Table 7: `secp521r1`, then `x448` | `Negotiated TLS1.3 group: **X25519MLKEM768**` |
| cipher suite | `[V2G20-2459]`, Table 6 | `TLS_AES_256_GCM_SHA384` — **correct**, and worth saying |

**The signature-algorithm count changed, 19 → 20**, between the original run and this one. Nothing in
their code changed; OpenSSL's default list did. That is the strongest evidence in this run for what §2
actually claims: the list is not a list anybody chose, it is whatever the linked OpenSSL ships that
week. A conformance requirement that moves when you upgrade a dependency is not being met by accident
either.

## What this run does not touch

Whether any of it breaks interop. It does not: OpenSSL negotiated something perfectly sound, and an EV
that supports the same defaults will complete the handshake. `[V2G20-1667]` and `[V2G20-2460]` are
conformance points, `[V2G20-2401]` is the one with a practical cost — an EV holding certificates under
more than one root has nothing to select on. §2 says so and this run does not change it.

## Reproduce

```bash
bash tools/interop-everest/tls-profile-arm.sh /tmp/transcript.txt
```

Keeps the **whole** `-state -msg` transcript, not a grep of it. The 2026-08-10 run stored only a
summary and only the responses, and that cost a wrong experiment two days later
([why](../2026-08-12-everest-main-client-auth/notes.md)).
