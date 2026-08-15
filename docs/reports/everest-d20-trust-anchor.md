# Draft report to EVerest — `Evse15118D20` trusts the MO root for TLS client authentication, so it accepts contract certificates and refuses **OEM-rooted** vehicle certificates

> **Corrected 2026-08-15, before sending.** This draft called arm A's leaf *"the vehicle credential"* and
> concluded that the station *"refuses vehicle certificates"*. Arm A's leaf is `client/oem/OEM_LEAF.pem`
> — `CN=OEMProvCert`, the OEM **provisioning** certificate, which `[V2G20-2342]` makes a different
> credential from the vehicle certificate of `[V2G20-2339]`. At the time the installed `dist` tree had no
> vehicle leaf to use, which is why that leaf was picked; it is still the wrong name for it.
> <br>**And the broader conclusion did not survive the check.** `connection_ssl.cpp:270` loads
> `path_certificate_v2g_root` **as well as** the MO root, and their own `create_certs.sh` mints the
> vehicle branch under the **V2G** root (`CN=WMIV1234567890ABCDEX, O=Pionix` ← `VehicleSubCA1` ←
> `V2GRootCA`). So a vehicle certificate from their own PKI **would verify**. What arm A actually
> demonstrates is narrower and still worth filing: an **OEM-rooted** client chain is refused, and
> `[V2G20-2331]` names the OEM root first among the two permitted anchors. Arm B is untouched.
> <br>Found while auditing the same distinction in someone else's stack
> ([`josev-iso20-vehicle-certificate.md`](josev-iso20-vehicle-certificate.md)) — the report that made us
> re-read this one. Corrections are in place below rather than rewritten away.

Status: **draft, not sent.** Measured on the wire 2026-08-10 against everest-core **2026.02.1**
(`b61bb12b8`) built from source: two TLS 1.3 handshakes against their stock configuration, using **their
own unmodified test PKI's** client credentials — nothing of ours on the wire but the `openssl` flags.
Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-10-everest-d20-trust-anchor`](../interop-runs/2026-08-10-everest-d20-trust-anchor/notes.md) —
the run notes, both charger logs, both `openssl` captures and the chain listing of the PKI as installed.

Other reports go to everest-core:
[`everest-d20-client-auth.md`](everest-d20-client-auth.md) — **the same function, and read them
together**: that one is about *whether* the station asks for a certificate and what it names in the
`CertificateRequest`; this one is about *which root it checks the answer against* —
[`everest-d20-ocsp-absent.md`](everest-d20-ocsp-absent.md),
[`everest-d20-ac-namespace.md`](everest-d20-ac-namespace.md),
[`everest-iso20-ac-contactor-latch.md`](everest-iso20-ac-contactor-latch.md),
[`everest-loop-shutdown.md`](everest-loop-shutdown.md) — all libiso15118 or `Evse15118D20`, so **the
same reviewer** — plus [`everest-isomux.md`](everest-isomux.md),
[`everest-evsev2g-session-log-responses.md`](everest-evsev2g-session-log-responses.md),
[`everest-evse-security-ocsp-dropped.md`](everest-evse-security-ocsp-dropped.md),
[`pyevjosev-manifest-services.md`](pyevjosev-manifest-services.md) and one to your fork of Josev's
certificate script ([`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md)). The framing in
`everest-loop-shutdown.md` — what everest-core has been worth to this project, and why a report from us
is not a bug filed by a stranger — applies here unchanged and is not repeated.

---

**Title:** `Evse15118D20` loads the **MO** root as a trust anchor for the EV's TLS client certificate
and never loads an **OEM** root, so a contract certificate passes verification and is recorded as
*"Vehicle Cert"* while an OEM-rooted client chain fails — and `CaCertificateType` has no `OEM` value with
which to ask for the anchor `[V2G20-2331]` names first

**Version:** everest-core **2026.02.1** (`b61bb12b8`), native build, Debian 13, OpenSSL 3.5.6. Module
`Evse15118D20`, library `lib/everest/iso15118`, `tls_negotiation_strategy: ENFORCE_TLS`,
`enforce_tls_1_3: true`, your test PKI exactly as installed.

## What we saw

Two arms. Same station, same config, same TLS version, three seconds apart. The only variable is which
of **your own** client leaves the client presented:

| Arm | client certificate | chains to | your station |
|---|---|---|---|
| **A** | `client/oem/OEM_LEAF.pem` — `OEMProvCert`, the OEM **provisioning** credential | `OEMRootCA` | **`certificate verify failed`** |
| **B** | `client/mo/MO_LEAF.pem` — `UKSWI123456789A`, a **contract** credential | `MORootCA` | `Verify certificate result is okay` |

Arm A is an **OEM-rooted** client chain, which is what it demonstrates; it is not a vehicle certificate,
and the installed tree had none to offer (`client/` holds `cps csms cso mo oem v2g` and no `vehicle`).

Arm A, in full:

```
[INFO] iso15118_charge :: Client supports TLS1.3: Change verify mode to SSL_VERIFY_PEER and SSL_VERIFY_FAIL_IF_NO_PEER_CERT
[ERRO] iso15118_charge :: Shutdown loop() because of: Failed to SSL_accept(): 1:
        …:SSL routines:tls_process_client_certificate:certificate verify failed:…
```

Arm B, in full — and the third line is the one we would lead with:

```
[INFO] iso15118_charge :: Handshake complete!
[INFO] iso15118_charge :: Verify certificate result is okay
[INFO] iso15118_charge :: Vehicle Cert is available
```

Your station calls a **contract** certificate the vehicle certificate, and at `connection_ssl.cpp:499`
takes its SHA-512 as `vehicle_cert_hash` — the value the ISO 15118-20 pause/resume binding is built
from.

## Where it comes from

`modules/EVSE/Evse15118D20/charger/ISO15118_chargerImpl.cpp:201-202`:

```cpp
const auto v2g_root_cert_path = mod->r_security->call_get_verify_file(types::evse_security::CaCertificateType::V2G);
const auto mo_root_cert_path  = mod->r_security->call_get_verify_file(types::evse_security::CaCertificateType::MO);
```

`lib/everest/iso15118/src/iso15118/io/connection_ssl.cpp:269-276`:

```cpp
// Loading root certificates to verify client (only for tls 1.3)
if (SSL_CTX_load_verify_file(ctx, ssl_config.path_certificate_v2g_root.c_str()) == 0) {
    logf_error("Verify V2G root not found!");
}

if (SSL_CTX_load_verify_file(ctx, ssl_config.path_certificate_mo_root.c_str()) == 0) {
    logf_error("Verify OEM root not found!");            // ← but the member is mo_root
}
```

**Your error string is the clearest evidence of intent we have.** The member is
`path_certificate_mo_root` (`lib/everest/iso15118/include/iso15118/config.hpp:31`), the module fills it from `CaCertificateType::MO`, and the
message printed when it will not load calls it the OEM root. That reads like the right idea meeting an
interface that has no word for it.

Which is where it stops being a one-line fix:

| | |
|---|---|
| `types/evse_security.yaml:12-16` | `CaCertificateType` = `V2G`, `MO`, `CSMS`, `MF`. No `OEM` |
| `lib/everest/evse_security/include/evse_security/evse_types.hpp:26-31` | the same four in libevse-security |
| `modules/EVSE/EvseSecurity/manifest.yaml` | four bundles: `csms_ca_bundle`, `mf_ca_bundle`, `mo_ca_bundle`, `v2g_ca_bundle` |

So there is no way to request the OEM root through the security interface, even though your installed
tree has `ca/oem/OEM_ROOT_CA.pem`, `OEM_SUB_CA1.pem`, `OEM_SUB_CA2.pem` and `OEM_CERT_CHAIN.pem` sitting
beside the ones that are wired up. The material is there; the type is not.

## Why we think it is worth fixing

**Because the two credentials do different jobs, and the standard is explicit about which is which.**

- **`[V2G20-2331]`** — a vehicle certificate's chain uses an **OEM root CA** as trust anchor; a V2G root
  is the permitted alternative.
- **`[V2G20-2339]`** — the EVCC holds a vehicle certificate, and it is what establishes the TLS session.
- **`[V2G20-2401]`/`[V2G20-2402]`**, with **`[V2G20-2403]`** for the DN contents — the anchors an SECC
  advertises in `certificate_authorities` are its **V2G root and/or OEM root** certificates. `MO` is in
  none of them.
- Clause **7.3.1**'s overview draws the line the other way from this implementation: contract
  certificates authenticate at the **application** layer; OEM roots and vehicle certificates are the
  pair the SECC uses at the **TLS** layer to authenticate the EVCC.

We cite requirement identifiers and paraphrase what they oblige rather than quoting; the rule is
[`docs/normative-basis.md`](../normative-basis.md). All `-20` identifiers, no document caveat.

**And because of what each half costs.**

- *Refusing an OEM-rooted client chain* is an interoperability wall for half the fleet the standard
  permits: an EV whose vehicle certificate chains to an **OEM** root — which `[V2G20-2331]` names first —
  cannot complete the TLS handshake with your station at all. It is not a soft failure; arm A ends the
  handshake, and (separately) your accept loop with it. **A V2G-rooted vehicle certificate is fine**,
  including the one your own `create_certs.sh` mints, because `connection_ssl.cpp:270` loads the V2G root
  — so this half is about the anchor you do *not* load, not about vehicle certificates as such.
- *Accepting the contract certificate* is the part that would worry us more in a deployment. A contract
  certificate is issued by a mobility operator to a **contract**, and is designed to be installed into
  vehicles; treating it as proof of *which vehicle* is on the cable is a different claim than it was
  issued to support. It then becomes `vehicle_cert_hash`, and **`[V2G20-2677]`** builds the pause/resume
  binding on that hash — so a resumed session is bound to the contract rather than to the car.

We are not claiming an exploit and have not looked for one; we are saying the two credential classes are
not interchangeable and your code currently treats them as though they were.

## Suggested direction

Which shape belongs in your tree is yours to choose; the first step is not.

1. **Add `OEM` to `CaCertificateType`** and an `oem_ca_bundle` to `EvseSecurity`'s manifest, mirroring
   `mo_ca_bundle`. Nothing above can be fixed without it, and libevse-security already manages an
   `ca/oem/` directory in the layout it installs.
2. **Then ask for it**: `call_get_verify_file(CaCertificateType::OEM)` at
   `ISO15118_chargerImpl.cpp:202`, into a field named for what it holds. Renaming
   `path_certificate_mo_root` to `path_certificate_oem_root` would make `connection_ssl.cpp:275`'s
   existing message true.
3. **Decide deliberately whether MO stays.** If there is a reason to keep accepting contract
   certificates at the TLS layer — a deployment or a test fixture that depends on it — we would rather
   read that in a comment than guess. `[V2G20-2331]` also allows a **V2G**-rooted vehicle certificate,
   so V2G belongs there regardless.
4. **While you are in that function**, `everest-d20-client-auth.md` §2 is the other half of the same
   sentence: whatever anchors you end up trusting should also be the ones named in the
   `CertificateRequest`'s `certificate_authorities` extension, which is currently empty.

## Not part of this

- **Whether verification happens at all.** On a TLS 1.2 connection the verify mode stays
  `SSL_VERIFY_NONE` and none of this runs — that is
  [`everest-d20-client-auth.md`](everest-d20-client-auth.md) §1, and it is a separate issue on purpose.
- **The dead accept loop** after arm A's refusal is
  [`everest-loop-shutdown.md`](everest-loop-shutdown.md), reproduced again here and not re-filed.
- **Your test PKI's curves.** `prime256v1` throughout, outside the `-20` profile's Tables 6 to 8, is
  [`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md) and comes from `create_certs.sh`.
- **What a real OEM-issued fleet does.** We tested with your own `OEMProvCert` because it is the leaf
  your own tooling produces for that role. If your intent is that the OEM *provisioning* certificate is
  not the TLS credential, say so — that is a useful answer and it would narrow this report rather than
  close it.

---

## Before sending

- [x] **Reproduce it, with a control.** Two arms, fresh station each, one variable. Arm B is what makes
      arm A a swap rather than a missing anchor: the station *does* verify client chains, against the
      wrong root.
- [x] **Use their PKI, not ours.** Both leaves, both keys, both intermediates come from
      `dist/etc/everest/certs` as installed, unmodified — which is why the run took ten minutes and why
      the maintainer can repeat it in five.
- [x] **Check every line reference against the tree.**
      `ISO15118_chargerImpl.cpp:201-202`; `connection_ssl.cpp:269-276`, `:499`; `config.hpp:31`;
      `types/evse_security.yaml:12-16`; `evse_types.hpp:26-31`; `EvseSecurity/manifest.yaml` — read from
      the built 2026.02.1 source on 2026-08-10.
- [ ] **Lead with your own log line.** *"Vehicle Cert is available"*, printed for a contract
      certificate. One line, and it is the whole issue.
- [ ] **Say the fix starts outside libiso15118.** `CaCertificateType` has no `OEM`; a patch to the `-20`
      module alone cannot be written. That is worth knowing before anyone starts.
- [ ] **Ask whether MO was deliberate.** There may be a fixture or a deployment behind it, and the
      answer changes the fix rather than the finding.
- [ ] **Mention `[V2G20-2677]`.** The accepted certificate becomes the resume binding, so this reaches
      further than the handshake.
- [ ] **File one issue, this one**, and link the client-auth one rather than merging with it.
- [ ] **Post under your own name, in your own words.**
