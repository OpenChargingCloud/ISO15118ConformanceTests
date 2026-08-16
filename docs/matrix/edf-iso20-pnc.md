# Plug & Charge at eVDriveFlow: none implemented

**Matrix cell:** EVCC and SECC · ISO 15118-20 · Plug & Charge · eVDriveFlow

Back to the [interop matrix](../../README.md).

---

**Structural, and now established rather than assumed.** This cell held a `▢` and a condition — *first find out whether they do contract certificates at all.* They do not: no `CertificateInstallation` handler in either role's state machine, and the whole Plug & Charge vocabulary (`ContractCertificateChain`, `PnC_AReqAuthorizationMode`, `SignedInstallationData`, `OEMProvisioningCert`) occurs only in the xsdata-generated bindings, ISO's schema and the Sphinx output of both — plus two Table 214 timeout keys with no handler to time. Their README's *Supported features* does not list it, and `PnC` appears nowhere in their documentation. Both halves ship `authorization_services = [EIM]`. The already-recorded bytes agree: their `AuthorizationSetupRes` is 20 payload bytes against our PnC-offering 38, with no room for a `GenChallenge` and none in it. The audit also turned up a latent SECC defect — the authorization *mode* is hardcoded to EIM whatever the configurable service list says, which `[V2G20-1219]` and `[V2G20-2568]` each forbid — recorded as a note on [the existing filing](docs/reports/evdriveflow-authorization-setup.md) rather than raised, since it is unreachable in their shipped configuration and they claim no PnC. [`…-edf-pnc-source-audit`](docs/interop-runs/2026-08-11-edf-pnc-source-audit/notes.md).
