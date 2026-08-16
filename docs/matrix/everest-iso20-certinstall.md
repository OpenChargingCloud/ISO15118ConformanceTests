# EVerest's real OEM chain, and the wall behind it

**Matrix cell:** SECC · ISO 15118-20 · CertificateInstallation · EVerest

Back to the [interop matrix](../../README.md).

---

The last chain our validator knew only from material we minted ourselves. Their `PyEvJosev` with
`is_cert_install_needed: true` sends a signed `CertificateInstallationReq` carrying
`OEMRootCA → OEMSubCA1 → OEMSubCA2 → OEMProvCert` — a **third** self-signed root in their PKI, after the
V2G one their TLS uses and the MO one their contract chain is anchored at. Their OEM root **alone**
suffices, because their car ships its Sub-CAs in the message; their two Sub-CAs **without** the root do
not, `CustomRootTrust` refusing a non-self-signed anchor at message level exactly as it does at TLS; and
their **V2G** root — which their own request names in `RootCertificateIDList` — is refused while the
signature still verifies. That field is the car saying which roots it can check, not which root vouches
for it. The wall after that is Josev's, in this fork as in SwitchEV's, and the contract key we wrap
stays self-checked: their P-256 OEM leaf cannot join `-20`'s secp521r1 ECDH
([`2026-08-08-everest-oem-provisioning-chain`](docs/interop-runs/2026-08-08-everest-oem-provisioning-chain/notes.md)).
