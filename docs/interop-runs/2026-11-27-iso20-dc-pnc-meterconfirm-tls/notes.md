# MeteringConfirmation over -20 TLS — our station, our car

**2026-11-27.** The `-20` `MeteringConfirmation` exchange had run in loopback tests, over plain TCP, and
as a recorded session ([`iso20-dc-pnc-meterconfirm`](../../../libs/EVSimulatorApp/vectors/Session.iso20-dc-pnc-meterconfirm.trace.json)),
but never inside a TLS session. This run closes that, between the stack's own two programs. It is not an
interop run: no other stack signs or verifies a `MeteringConfirmationReq` today (see
[`open-work.md`](../../open-work.md), *Never verified by anything but us*).

**Result: 2 of 2 runs, each with two confirmations, every check `OK`.**

## Setup

| | |
| --- | --- |
| Stack | WWCP_ISO15118 `23ed486`, via EVSimulatorApp `53335ed`, this repository at `f103962` |
| Runtime | .NET 11.0.100, Windows 11, both programs on `[::1]` |
| TLS | BouncyCastle backend (`--tls-backend bc`) on both sides: TLS 1.3 only for `-20` (`BcV2GTls.Tls13Only`), mutual, both certificates P-521 with `ecdsa_secp521r1_sha512`. The station mints the V2G hierarchy into `--pki-dir`, and each side pins the other's leaf from there |
| Contract | a throwaway P-521 MO hierarchy to the `-20` contract profile, leaf `CN=DEOCCC521000001`, minted the way [`mint-mo521.sh`](../2026-11-26-everest-2026.10.0-forward/scripts/mint-mo521.sh) mints it for EVerest |
| Station | DC, signing meter `VAN*M1`, `--metering-confirmation 1` (ask after every charge-loop response from the second on), `--require-metering-confirmation` (answer an invalid one `FAILED_MeteringSignatureNotValid`, which ends the session) |
| Script | [`scripts/run.sh`](scripts/run.sh), from `libs/EVSimulatorApp/libs/WWCP_ISO15118` after a Release build |

## What happened

Both runs are the same; the first is kept in [`sessions/`](sessions/).

- **TLS and authorization.** The session ran over the BouncyCastle TLS connection and authorized with
  Plug & Charge: challenge, digest and signature `OK` under `ecdsa-sha512`, with the standalone
  `SignedInfo` grammar. 19 exchanges, `SessionStop` reached.
- **Two confirmations, both on the side stream inside the TLS session.** The station asked at 1,600 and
  2,400 Wh. For each, session, reading, receipt, control mode, digest and signature were checked
  separately, and all passed. The station answered `OK` twice, and the car counted 2,400 Wh itself.

```
MeteringConfirmation: asked 2, answered 2, unanswered in time 0.
  1600 Wh: session OK, reading OK, receipt OK, control mode OK, digest OK, signature OK (grammar=xmldsig-standalone); answered OK.
  2400 Wh: session OK, reading OK, receipt OK, control mode OK, digest OK, signature OK (grammar=xmldsig-standalone); answered OK.
```

## What it does not show

- **Not a second implementation.** Our station checked our car. The three open choices in the concept
  paper (§11.6: the signed octets, the signature form, one side-stream request at a time) are exactly as
  open as before.
- **The contract chain was not validated.** `--pki-dir` mints the V2G hierarchy but writes no root, and
  `--trust-roots` judges the car's TLS chain as well as its contract chain. So the two cannot be combined
  here, and the station printed `chain not checked — no --trust-roots`. The signature was verified
  against the contract leaf the car presented. Validating the chain too needs both hierarchies from the
  PKI builder, with `--server-cert`/`--vehicle-cert` and `--trust-roots` on both sides.
- **The key-exchange group is not recorded.** The backend restricts the version and the signature
  schemes, but the group is BouncyCastle's default, and neither program prints what was negotiated.
- **No `frames.log`.** Neither program writes one, and the interop harness that does was not pointed at
  our own station. The console output in `sessions/` is the record. The bytes of the exchange itself
  are in the corpus trace, recorded over plain TCP.
