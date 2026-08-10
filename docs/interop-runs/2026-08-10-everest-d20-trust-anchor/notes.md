# 2026-08-10 — their `-20` station rejects the vehicle certificate and accepts a contract certificate

`Evse15118D20` loads two trust anchors for the EV's TLS client certificate: the **V2G root** and the
**MO root**. The MO root certifies *contract* certificates, which ISO 15118-20 places at the
**application** layer. The anchor that certifies the **vehicle** certificate — the credential the SECC
is supposed to authenticate the EV with — is the **OEM root**, and it is never loaded, because
`CaCertificateType` has no `OEM` value to ask for.

Measured with their own unmodified test PKI, two arms, one variable:

| Arm | client certificate | chains to | their station |
|---|---|---|---|
| **A** | `client/oem/OEM_LEAF.pem` — `OEMProvCert`, the **vehicle** credential | `OEMRootCA` | **`certificate verify failed`** |
| **B** | `client/mo/MO_LEAF.pem` — `UKSWI123456789A`, a **contract** credential | `MORootCA` | `Verify certificate result is okay` · **`Vehicle Cert is available`** |

The last line is their own. The station calls the contract leaf the vehicle certificate, hashes it, and
keeps that hash as the `-20` session-resume binding.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), source build in WSL2 Debian 13, OpenSSL 3.5.6 |
| Their module | `Evse15118D20` / `libiso15118`, `config-d20-tls-ours.yaml` (`ENFORCE_TLS`, `enforce_tls_1_3: true`), their stock PKI as installed |
| Ours | `openssl s_client -tls1_3` with **their** client credentials. Nothing of ours on the wire but the flags |
| Outcome | **The two credential classes are swapped: the vehicle certificate is refused, the contract certificate is accepted and recorded as the vehicle's** |
| Artifacts | [`their-charger.oem-vehicle.log`](their-charger.oem-vehicle.log) · [`openssl.oem-vehicle.log`](openssl.oem-vehicle.log) · [`their-charger.mo-contract.log`](their-charger.mo-contract.log) · [`openssl.mo-contract.log`](openssl.mo-contract.log) · [`their-pki-chains.txt`](their-pki-chains.txt) |
| Filed | [`everest-d20-trust-anchor.md`](../../reports/everest-d20-trust-anchor.md) |

## The two arms in full

**A — the vehicle credential.** `OEMProvCert` → `OEMSubCA2` → `OEMSubCA1` → `OEMRootCA`, all from
their own `etc/everest/certs`:

```
[INFO] iso15118_charge :: Client supports TLS1.3: Change verify mode to SSL_VERIFY_PEER and SSL_VERIFY_FAIL_IF_NO_PEER_CERT
[ERRO] iso15118_charge :: Shutdown loop() because of: Failed to SSL_accept(): 1:
        …:SSL routines:tls_process_client_certificate:certificate verify failed:…
```

**B — the contract credential.** `UKSWI123456789A` → `PKI-Ext_CRT_MO_SUB2_VALID` →
`PKI-Ext_CRT_MO_SUB1_VALID` → `MORootCA`:

```
[INFO] iso15118_charge :: Handshake complete!
[INFO] iso15118_charge :: Verify certificate result is okay
[INFO] iso15118_charge :: Vehicle Cert is available
```

Same station, same configuration, same TLS version, same three seconds apart. The only difference is
which of their own leaves the client presented.

## Where it comes from

`modules/EVSE/Evse15118D20/charger/ISO15118_chargerImpl.cpp:201-202`:

```cpp
const auto v2g_root_cert_path = mod->r_security->call_get_verify_file(types::evse_security::CaCertificateType::V2G);
const auto mo_root_cert_path  = mod->r_security->call_get_verify_file(types::evse_security::CaCertificateType::MO);
```

and `lib/everest/iso15118/src/iso15118/io/connection_ssl.cpp:269-276`:

```cpp
// Loading root certificates to verify client (only for tls 1.3)
if (SSL_CTX_load_verify_file(ctx, ssl_config.path_certificate_v2g_root.c_str()) == 0) {
    logf_error("Verify V2G root not found!");
}

if (SSL_CTX_load_verify_file(ctx, ssl_config.path_certificate_mo_root.c_str()) == 0) {
    logf_error("Verify OEM root not found!");            // ← the field is mo_root
}
```

**The error string says what was meant.** The member is `path_certificate_mo_root`
(`config.hpp:31`), the module fills it from `CaCertificateType::MO`, and the message printed when it
fails to load calls it the OEM root. Somewhere between intent and interface the two became the same
thing.

And the interface is where it stops being a one-line fix:

| | |
|---|---|
| `types/evse_security.yaml:12-16` | `CaCertificateType` is `V2G`, `MO`, `CSMS`, `MF` — **no `OEM`** |
| `lib/everest/evse_security/include/evse_security/evse_types.hpp:26-31` | the same four in libevse-security |
| `modules/EVSE/EvseSecurity/manifest.yaml` | four bundles configured: `csms_ca_bundle`, `mf_ca_bundle`, `mo_ca_bundle`, `v2g_ca_bundle`. No OEM bundle |

So the OEM root cannot be *asked for* through the security interface at all — even though their own
installed PKI has `ca/oem/OEM_ROOT_CA.pem`, `ca/oem/OEM_SUB_CA1.pem`, `OEM_SUB_CA2.pem` and
`OEM_CERT_CHAIN.pem` sitting beside it. The material is there; the type to request it is not.

## What the standard separates, and this joins

- **`[V2G20-2331]`** — a vehicle certificate's chain uses an **OEM root CA** as trust anchor (a V2G
  root is the permitted alternative).
- **`[V2G20-2339]`** — the EVCC shall hold a vehicle certificate; it is what establishes the TLS
  session.
- **`[V2G20-2401]`/`[V2G20-2402]`** — the anchors an SECC advertises in `certificate_authorities` are
  its **V2G root CA and/or OEM root CA** certificates, with **`[V2G20-2403]`** for the DN contents.
  `MO` appears in none of them.
- Clause **7.3.1**'s own overview draws the line the other way round from this implementation: contract
  certificates authenticate at the **application** layer; OEM roots and vehicle certificates are the
  pair the SECC uses at the **TLS** layer to authenticate the EVCC. (Paraphrase — see the citing rule in
  [`normative-basis.md`](../../normative-basis.md).)
- **`[V2G20-2677]`** is what makes arm B worse than a permissive anchor list: the `-20` resume binding
  is over the **vehicle** certificate, and `connection_ssl.cpp:499` hashes whatever passed verification.
  In arm B that is a contract leaf.

All `-20` identifiers; no document caveat.

## Why none of our own runs caught this

Worth recording, because we have had mutual TLS working against this station since 2026-08-08
([`…-pause-resume-tls-rerun`](../2026-08-08-everest-pause-resume-tls-rerun/notes.md)) and it never
looked wrong.

`install-pki.sh` mints an ISO 15118-20 tree and builds `vehicle.p12` **inside their layout**, and that
vehicle credential chains to the **V2G root** — which is the *other* anchor `[V2G20-2331]` allows and
the one `Evse15118D20` does load. So every mutual-TLS session we have run went down the branch that
works, and the OEM branch — the one an EV from an actual OEM uses — was never exercised.

That is the same lesson as the AC-namespace run in a different coat: **a rig built to make a session
succeed will not find the credential class it never presents.** The two arms here cost ten minutes
because they used *their* PKI instead of ours.

## How it was run

```bash
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-d20-tls-ours.yaml &   # fresh per arm
SECURITY=00 bash sdp-probe.sh eth0
C=~/everest/dist/etc/everest/certs
openssl s_client -tls1_3 -connect "$EP" \
    -cert $C/client/oem/OEM_LEAF.pem -key $C/client/oem/OEM_LEAF.key \
    -cert_chain $C/ca/oem/INTERMEDIATE_OEM_CA.pem -pass pass:123456     # arm A
openssl s_client -tls1_3 -connect "$EP" \
    -cert $C/client/mo/MO_LEAF.pem  -key $C/client/mo/MO_LEAF.key \
    -cert_chain $C/ca/mo/INTERMEDIATE_MO_CA_CERTS.pem -pass pass:123456 # arm B
```

Both key passwords are in `*_LEAF_PASSWORD.txt` beside the keys — `123456` in the shipped tree. The PKI
was read, never modified.
