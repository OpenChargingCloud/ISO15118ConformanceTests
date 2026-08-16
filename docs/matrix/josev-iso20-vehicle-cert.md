# Josev's EVCC presents its OEM chain as a TLS credential

**Matrix cell:** SECC · ISO 15118-20 · Mutual TLS 1.3 · Josev

Back to the [interop matrix](../../README.md).

---

**The forty-eighth filing, and it corrected the fortieth.** Josev's EVCC loads its **OEM provisioning**
chain as the TLS client credential (`security.py:209`), and the same leaf is the `OEMProvisioningCert` of
`CertificateInstallationReq` — one leaf, two jobs, where `[V2G20-2339]` and `[V2G20-2342]` are two
*shall*s for two credentials. `grep -c VEHICLE shared/security.py` returns **0** and nothing matches
`vehicle_cert`/`VEHICLE_LEAF` anywhere under `iso15118/`: the class is absent, not mis-wired. `[V2G20-2598]`
decides it — a vehicle certificate carries the **EVCCID** as Common Name, and this one reads
`CN=OEMProvCert`. **Their own downstream fork `26f7988` already has the whole thing**, mints
`CN=WMIV1234567890ABCDEX, O=Pionix` under the V2G root, so the ask is a port.
<br>**And pointing the distinction at our own drafts broke one.** `everest-d20-trust-anchor.md` had
called an OEM *provisioning* leaf "the vehicle credential" and concluded their station *"refuses vehicle
certificates"* — but it loads the V2G root too, and EVerest's own vehicle chain is V2G-rooted, so a real
one verifies. The OEM-rooted half stands and the generalisation does not; corrected in place. The
measurement was right and the sentence ran past it — **a distinction is only as sharp as the third case
you test it on**.
[`…-josev-tls-vehicle-cert-audit`](docs/interop-runs/2026-08-15-josev-tls-vehicle-cert-audit/notes.md).
