# The -20-faithful backend meets a foreign PKI — 2026-08-09

The last part of the chain validator that had only ever judged our own material. Closed the same day it
was written down as out of reach, because the obstacle turned out to be a missing option rather than a
missing capability.

**What was in the way.** `--tls-backend bc` accepted only `--pki-dir`, and that path calls
`SeccPki.Generate`: it mints a dev V2G hierarchy, presents its own SECC leaf, and pins the vehicle leaf
it has just minted. Against a foreign car both halves fail — we cannot serve a certificate their EV
trusts, and our leaf pin rejects their client certificate before `--trust-roots` is ever consulted.

**What changed.** `--server-cert` now works on that backend too, mirroring
`EvccPki.WithVehicleCertificate` on the car side: load the chain from a PKCS#12, pin nothing, and let
`--trust-roots` decide. Pinning nothing is the honest half — a station serving somebody else's
certificate has no expectation to check the peer against, so the program says whether the chain was
validated or nothing was.

## The run

| | |
|---|---|
| Counterparty | [`EDF-Lab/eVDriveFlow`](https://github.com/EDF-Lab/eVDriveFlow) @ `60249c3`, their regenerated PKI, `SECURITY_PROTOCOL = 0x00` |
| Ours | `--protocol 20 --mode dc --dynamic --no-pnc --tls-backend bc --server-cert <their SECC chain> --require-client-cert --trust-roots <their OEM root>` |

```
Trust roots: DC=OEM, C=FR, O=EDF, CN=OEMRootCA
Presenting server certificate: DC=CPO, C=FR, O=EDF, CN=SECCCert (+2 intermediate(s));
  requiring a client certificate and validating its chain
SECC listening on [::]:55000 (protocol -20, DC, TLS BouncyCastle)...
TLS client: chain valid, anchored at DC=OEM, C=FR, O=EDF, CN=OEMRootCA.
Energy transfer service: 6 (DC_BPT).
```

Their side: `TLS Session established: TLS Version: TLSv1.3`, `Cipher suite: TLS_AES_256_GCM_SHA384`,
then 62 messages to `DC_ChargeLoopReq` and their usual stop in
`ev_dummy_controller.get_target_energy` — [issue 2 of their
report](../../reports/evdriveflow-headless-session.md), untouched by any of this.

So the same verdict as the `SslStream` run earlier the same day
([`…-edf-chain-validation`](../2026-08-09-edf-chain-validation/notes.md)), through entirely different
code: that one goes through `TrustRoots.PeerIntermediates` and a `RemoteCertificateValidationCallback`,
this one through `BcTlsOptions.ValidatePeerChain` and BouncyCastle's own certificate message. **Both
backends now validate a foreign chain, and they agree.**

## Kept rather than fixed, and now documented

This backend's `CertificateRequest` carries ISO 15118-20's strict signature pair —
`ecdsa_secp521r1_sha512` and `ed448` — so **a car with a P-256 client certificate cannot answer it.**
That rules out Josev, EVerest and tux-evse, which is to say every counterparty here except this one. It
is the profile being kept rather than a defect, and it is why eVDriveFlow is the only stack this run
could have been done against at all: theirs is the only `-20` test PKI in the field that is what `-20`
describes.

`--help` and the station's README now say this, because the failure it produces — a handshake that ends
without a client certificate — reads like a defect otherwise.

## What this leaves

Nothing, for the chain validator. All four entry points — TLS on both backends, the `-20` contract
chain, the OEM provisioning chain — have now been measured against a counterparty's own hierarchy, with
a working negative control on each. What remains unverifiable by anyone here is the `-20` contract-key
*wrap*, and that is structural: no counterparty implements the response side, and none of their
provisioning leaves is on a curve that could join the key agreement.

Logs: [`secc-bc-oemroot.log`](secc-bc-oemroot.log), [`ev-bc-oemroot.log`](ev-bc-oemroot.log).
Rig and the IPv6 network switch: as in the
[SslStream run](../2026-08-09-edf-chain-validation/notes.md).
