# 2026-11-27 — `isomux` §4 against 2026.10.0: the car names root A, the station serves root B's chain

The last of the forty-nine drafted issues that had not been looked at since August
([reports index](../../reports/README.md)). The 2026-08-16 design
([run](../2026-08-16-everest-isomux-section4/notes.md)) was run unchanged against the **2026.10.0**
release. A second V2G root (B) and an SECC chain under it were installed beside the release's own PKI (A).
Our `-2` DC car went through `IsoMux` over TLS 1.2 with RFC 6066 `trusted_ca_keys`. Each arm got a
fresh manager start, and arm a ran again under a packet capture.

| | |
|---|---|
| Counterparty | EVerest **2026.10.0** (`74845c578`), `~/everest/dist-2610`, `config-mux-tls-fwd2610.yaml` (`IsoMux` + `EvseV2G`, DC) |
| Ours | `EverestInteropTests.OurEvcc`, `-2` DC, `V2G_INTEROP_TLS_TRUST` and `V2G_INTEROP_TRUSTED_CA_KEYS` set per arm |
| Their PKI | four files added for the run (`V2G_ROOT_CA_B.pem`, `SECC_LEAF_B.{pem,key}`, `CPO_CERT_CHAIN_B.pem`) and the same four removed by an `EXIT` trap; nothing of theirs modified |
| Result | **§4 is live in the release.** The chain is chosen without regard to the root the car names |

## The arms

| arm | names in `trusted_ca_keys` | trusts | outcome |
|---|---|---|---|
| **a** | root A | root A | **`bad_certificate(42)`**, the session over in 255 ms |
| **b** | root B | root B | complete DC session, `SessionStop` |
| **c** | root B | root A | **`bad_certificate(42)`** |
| **a2** | root A | root A | **`bad_certificate(42)`** |
| **acap** | root A | root A | `bad_certificate(42)`, under `tcpdump` |

**It is the mirror image of August, for the same reason.** On 2026.02.1 the station served chain A to
every arm, so naming B and trusting B was refused. On 2026.10.0 it serves chain B to every arm, so
naming A and trusting A is refused. In both releases one chain is served whatever the car names, and
only the default changed. Arm a is the proof this time: the car names A, trusts A, and is given a chain
it cannot verify.

## The bytes

From [`capture/acap.pcap`](capture/acap.pcap), decoded by [`tls-handshake.py`](scripts/tls-handshake.py)
([`acap.decoded.txt`](capture/acap.decoded.txt)):

```
ClientHello extensions: 22 23 5 13 10 11 3
trusted_ca_keys extension: 00150328537d28c2d29c08e9b9a63a81b97d4eb38af2ae
  cert_sha1_hash 28537d28c2d29c08e9b9a63a81b97d4eb38af2ae      = SHA-1 of root A
server Certificate: 2 certificate(s)
  CN=SECCCert-B  <-  CN=CPOSubCA-B
under root A (the one the car named): verification failed
under root B (not named):             OK
```

## Why, in the release

Their own log, every arm ([`arms/*.their-side.txt`](arms/)):

```
evse_security  Found valid leaf: CPO_CERT_CHAIN_B.pem, CPO_CERT_CHAIN.pem, SECC_LEAF_B.pem, SECC_LEAF.pem
evse_security  Not requiring all valid leafs, returning
iso_mux        No trust anchors for certificate: C:DE CN:SECCCert-B DC:CPO O:EVerest
iso_mux        trusted_ca_keys support disabled
iso_mux        no configured chain passed verification; the SSL_CTX default certificate remains chains[0] …
iso_mux        trusted_ca_keys: no configured chain matched the peer's trusted_ca_keys; serving default chain
```

Read against the tag ([`release-source.txt`](release-source.txt)):

- **`IsoMux` asks for one leaf and gives it no anchor.**
  - `IsoMux/connection/tls_connection.cpp:295` calls `call_get_leaf_certificate_info(V2G, PEM, false)`,
    which returns a single chain: *"Not requiring all valid leafs, returning"*.
  - Lines `:307-310` emplace that one chain with its certificate, key and password.
  - Neither line sets `trust_anchor_file` or `trust_anchor_pem`.
- **The TLS library has the matching now, and it is gated on those anchors.**
  - `lib/everest/tls/src/tls.cpp:1380-1390` keeps a chain without anchors as *"No trust anchors for
    certificate"*.
  - `:1401-1406` then turns the feature off for the whole server (*"trusted_ca_keys support disabled"*).
  - The per-connection line *"no configured chain matched … serving default chain"* is new in the
    release. The extension is now read on arrival, but it can never match anything.
- **The `-20` module does what `IsoMux` does not.** `Evse15118D20/charger/evse_security_ssl_config.cpp:77`
  sets `chain_config.trust_anchor_pem = chain.certificate_root` per chain, over all valid chains. That
  is the shape of the fix: `get_all_valid_certificates_info` and one anchor per chain.

`IsoMux` and `tls.cpp` are unchanged between the tag and `main` `8abc3f2` (2026-11-27).

## What this changes in the report

Nothing in the claim, and one sentence in the argument. §4 already said both halves: no chain ever
enters the selectable list, and `get_leaf_certificate_info` returns a single chain. The release adds
two things worth one line each:
- The library now reads and logs the extension per connection, so the gap is visible in their own log
  at the moment it matters.
- The default chain depends on which leaf `evse_security` returns first. With two roots installed that
  was A on 2026.02.1 and B on 2026.10.0, so a station's behaviour towards a car naming its real root can
  flip with a PKI change and no code change.

The table in [`reports/README.md`](../../reports/README.md) moves §4 from *not re-read* to *live*.

## Not measured

- Two names in one `trusted_ca_keys`, or identifier types other than `cert_sha1_hash`. As in August,
  these only matter once a chain can match.
- Which leaf `evse_security` returns first in general. This run shows only the two outcomes the rig
  produced.

## Artifacts

[`arms/`](arms/) holds our log, their mux and security lines, and the flow where a session ran, per arm.
[`capture/`](capture/) holds the pcap, the decoded handshake and the served chain as PEM.
[`scripts/`](scripts/) holds the driver ([`isomux4-2610.sh`](scripts/isomux4-2610.sh)), the root capture
and the decoder. No code changed.
