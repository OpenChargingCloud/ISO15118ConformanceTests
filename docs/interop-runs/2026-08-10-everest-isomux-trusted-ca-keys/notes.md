# 2026-08-10 — `IsoMux` disables `trusted_ca_keys`, and `EvseV2G` behind it does not

Four lines from one station boot, already in the log of
[the short-read run](../2026-08-10-everest-isomux-shortread/their-charger.log) — the same process, the
same test PKI, two TLS servers 4 ms apart:

```
15:41:01.165  iso_mux:IsoMux   TLS server on eth0 is listening on port [fe80::…%2]:64110
15:41:01.165  iso15118_2:Evse  TLS server on lo   is listening on port [::1%0]:64109
15:41:01.256  iso_mux:IsoMux   <n> certificates != <n> OCSP responses
15:41:01.257  iso_mux:IsoMux   No trust anchors for certificate: C:DE CN:SECCCert DC:CPO O:EVerest
15:41:01.257  iso_mux:IsoMux   trusted_ca_keys support disabled
15:41:01.261  iso15118_2:Evse  <n> certificates != <n> OCSP responses
                               ← and nothing more from EvseV2G
```

Both lose their OCSP data, which is [the twenty-fourth filing](../../reports/everest-evse-security-ocsp-dropped.md).
Only the multiplexer loses its **trust anchors**, and with them the certificate-selection machinery
that ISO 15118-2 obliges the station to use.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), source build in WSL2 |
| Their modules | `IsoMux` (TLS terminator) in front of `EvseV2G` + `Evse15118D20`, `config-mux-ours.yaml`, their own test PKI |
| Ours | nothing — one station boot, their own log |
| Outcome | **`IsoMux` serves `chains[0]` whatever roots the EV says it trusts; `EvseV2G` selects** |
| Artifacts | [`boot-a-b.log`](boot-a-b.log), and the full log in [the neighbouring run](../2026-08-10-everest-isomux-shortread/their-charger.log) |
| Filed | [`everest-isomux.md`](../../reports/everest-isomux.md) — the twenty-sixth |

## What the extension is for, and what the standard makes of it

- **`[V2G2-651]`** — the EVCC **shall** send a `trusted_ca_keys` extension (IETF RFC 6066) listing the
  V2G root certificates it holds. Not optional, and not conditional: every conformant EV sends it.
- **`[V2G2-871]`** — a station outside a private environment owes the EV its certificate and a chain up
  to a root, and that root **shall be one of the ones the EV signalled**.
- **`[V2G2-923]`** — only when it cannot match may it present a chain to some other root.
- **`[V2G2-924]`** with **`[V2G2-875]`** — the EV receiving a chain that does not trace to a root it
  trusts must treat it as unvalidated unless it can validate out of band, and abandon the TLS setup.
- **`[V2G2-878]`** — up to ten concurrently valid V2G root certificates per root CA, so the multi-root
  case the extension exists for is expressly contemplated.

So the extension is the mechanism by which `[V2G2-871]`'s selection duty is discharged, and a station
that switches it off cannot discharge it.

Carries the `-2` document caveat in [`normative-basis.md`](../../normative-basis.md).

## Why the multiplexer switches it off

Not by choice — by an argument, and then by an API.

`IsoMux/connection/tls_connection.cpp:292` asks libevse-security for its certificate with

```cpp
call_get_leaf_certificate_info(LeafCertificateType::V2G, EncodingFormat::PEM, false);
```

and then fills `certificate_chain_file`, `private_key_file`, `private_key_password` — and **never
`trust_anchor_pem` or `trust_anchor_file`**, because it has nothing to put there:
`get_leaf_certificate_info` runs with `include_root = false`
(`evse_security.cpp:1371-1374`), so `certificate_root` is not in the reply.

`EvseV2G/connection/tls_connection.cpp:298` asks the other way —
`call_get_all_valid_certificates_info(V2G, PEM, true)`, which runs with `include_root = true` — and
sets `ref.trust_anchor_pem = root_pem.c_str()` at `:316`.

In `lib/everest/tls/src/tls.cpp:1048-1069` the consequence is mechanical: with no trust anchors the
chain is neither verified (`openssl::verify_chain` is not reached) nor added to `chains`, the
*"No trust anchors for certificate"* warning is logged, and at `:1077-1080` an empty `chains` produces
*"trusted_ca_keys support disabled"* and `m_server_trusted_ca_keys.update({})`.

## What is actually lost

`ServerTrustedCaKeys::handle_certificate_cb` (`extensions/trusted_ca_keys.cpp:368-407`, wired at `:335`) is where the
station would honour the extension: when the EV sent one, it calls `select()` over its chains and
installs the match with `use_certificate_and_key`. With an empty list `select()` returns `nullptr`,
nothing is installed, and the handshake proceeds with whatever `init_ssl` configured statically —
`cfg.chains[0]` (`tls.cpp:940-941`).

**Two things are therefore impossible in `IsoMux`, not one:**

1. It has no trust anchor, so no chain ever enters the selectable list.
2. It called `get_leaf_certificate_info`, which returns the **newest single** chain rather than all
   valid ones — so even with (1) fixed there would be exactly one chain to choose between.

`EvseV2G` gets both right through the one call it makes.

**With a single V2G root, none of this shows.** The station serves its only chain, which is what
`[V2G2-923]` would have it do anyway, and that is why this has been sitting in our logs unread since
2026-08-06. With two — a CPO mid-rotation, or one serving two roots, which `[V2G2-878]` plainly
expects — the multiplexer serves the first and an EV holding only the other root abandons the
handshake, correctly, per `[V2G2-924]`.

## What was not established

We have **not** run the failing case. It needs a station provisioned with two V2G chains under
different roots and an EV that sends `trusted_ca_keys` naming only the second; our own EVCC does not
send the extension, and `openssl s_client` does not send it either without patching. The finding is the
station's own two log lines plus the source that produces them — which is why the report says so
plainly rather than implying a session that never happened.
