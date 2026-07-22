# Interop run — ISO 15118-20 **contract provisioning** (CertificateInstallation), live: Josev EVCC → our SECC

- **Date:** 2026-07-22
- **Our side:** `secc --protocol 20 --mode dc --sdp --interface eth0` (plain TCP); the SECC now announces
  `CertificateInstallationService: true` and implements the full issue path.
- **Josev:** EVCC, docker host-mode, `evcc_config_dc.json` + `"isCertInstallNeeded": true`.
- **Outcome:** ✅ the live goal — our SECC **verified Josev's real signed CertificateInstallationReq and
  issued a signed, Josev-validated CertificateInstallationRes**:

  ```
  CertificateInstallation: OEM DC=OEM, C=UK, O=Switch, CN=OEMProvCert; digest OK,
  signature OK (grammar=xmldsig-standalone), contract issued
  (OEM key not P-521 — blob undecryptable for EV).
  ```

  Josev decoded **and model-validated** our response, then ended at its own hard limit:
  `NotImplementedError … CertificateInstallation not yet implemented` — Josev implements cert-install
  **neither** on the SECC side **nor** in its EVCC's response handling, so this run is the maximum any
  live exchange with Josev can reach. (Forward direction is impossible for the same reason.)

## Three real interop findings on the way

1. **Josev frames the req with the wrong V2GTP payload type.** Its `create_next_message` defaults to
   `ISOV2PayloadTypes.EXI_ENCODED` (0x8001 — an ISO 15118-**2** value) and the cert-install call site
   forgets to pass `ISOV20PayloadTypes.MAINSTREAM` (0x8002). Our dispatcher routed 0x8001 to the -2 codec
   → `Unknown document index 14`. Fix: a documented leniency in the -20 SECC read path
   (`Secc20Base.ReadFrame20Async`) decodes a 0x8001 frame inside a -20 session as CommonMessages.
2. **Our codec is byte-exact for the whole message.** The 1809-byte req (reproduced byte-identically from
   the live values with Josev's own EXIficient codec — `tools/interop-josev/certinstall-probe.py`) decodes
   and re-encodes identically (`JosevCertificateInstallationReqTests`), and our new
   `EncodeFragment_OEMProvisioningCertificateChain` reproduces Josev's exact 1476-byte signed fragment —
   digest and ECDSA signature of the real Josev material verify offline **and** live
   (standalone-xmldsig grammar, ecdsa-sha256 — same Josev form as PnC).
3. **Josev's pydantic `Reference` model requires `Transforms`** although the XSD makes it optional; our
   first res (no Transforms) was rejected with an (empty-messaged) `V2GMessageValidationError`.
   `V2GSignature.BuildSignedInfo` gained `includeExiTransform` and the cert-install res sets it — after
   which Josev accepts the res fully.

## What the SECC issues

A throwaway **P-521 dev contract**: fresh contract + CPS certs, the contract private scalar wrapped via
ephemeral **secp521r1 ECDH → ConcatKDF-SHA512 → AES-256-GCM** (`ContractProvisioning`;
`DHPublicKey` 133 B, `SECP521_EncryptedPrivateKey` 94 B = IV‖ct‖tag per the schema facets), and the
`SignedInstallationData` signed with the CPS leaf (P-521/SHA-512, combined grammar). Josev's OEM
provisioning cert is **P-256** (its -2-era PKI), which cannot take part in the -20 secp521r1 key
agreement — the response is well-formed but undecryptable for that EV, recorded as
`EncryptedForOem=false`. **Honesty note:** no independent stack implements -20 provisioning crypto, so
the KDF/wrap octets are self-consistent only (round-trip-tested); the *messages* remain byte-exact per
the usual oracles.

## The full roundtrip lives in-repo

`Iso20LoopbackTests.DcCertInstallSession_ProvisionsAWorkingContractKey`: our EVCC (P-521 OEM identity,
`CertInstallEvccOptions`) requests, the SECC verifies + issues (`EncryptedForOem=true`), the EVCC verifies
the CPS signature and **unwraps a working contract key** that signs against the issued certificate.
Also CI: `Secc20CertInstallTests` (real Josev frame → verify + signed res + session continues; crypto
round-trip + GCM tamper detection), `JosevCertificateInstallationReqTests` (byte-exactness, digest).

Script: [`reverse-certinstall-sdp.sh`](../../../tools/interop-josev/reverse-certinstall-sdp.sh); probes:
`certinstall-probe.py` (in-container reproduction). Logs: [`secc-certinstall.log`](secc-certinstall.log),
[`evcc-certinstall.log`](evcc-certinstall.log).
