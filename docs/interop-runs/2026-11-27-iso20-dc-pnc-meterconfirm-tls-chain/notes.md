# MeteringConfirmation over -20 TLS, every chain to a root — our station, our car

**2026-11-27.** The [first TLS run](../2026-11-27-iso20-dc-pnc-meterconfirm-tls/notes.md) of the `-20`
`MeteringConfirmation` exchange left the contract chain unvalidated. `--pki-dir` writes no root, and
`--trust-roots` judges the car's TLS chain as well as its contract chain. This run replaces `--pki-dir`
with a full hierarchy from the stack's own PKI builder. Both programs bring their chains as PKCS#12, and
both validate the peer against roots. It is still not an interop run: no other stack signs or verifies
a `MeteringConfirmationReq` (see [`open-work.md`](../../open-work.md), *Never verified by anything but us*).

**Result: every chain anchored, and both confirmations passed every check, in 3 of 3 runs. Two negative
runs show the checks can fail. A wrong root on the car's side ends the handshake. A missing MO root on the
station's side rejects the contract chain, but the session goes on, and the confirmations are still
answered `OK`. That second point was a limit of our station. It was fixed the same evening
([WWCP_ISO15118#58](https://github.com/OpenChargingCloud/WWCP_ISO15118/pull/58)), and with the fix the
confirmation is refused ([below](#after-the-fix-the-untrusted-contract-is-refused)).**

## Setup

| | |
| --- | --- |
| Stack | WWCP_ISO15118 `a6259a4`, via EVSimulatorApp `8fccda1`, this repository at `bfffe63` |
| Runtime | .NET 11.0.100, Windows 11, both programs on `[::1]` |
| PKI | `V2GPKI_CLI --algo p521 --profile strict-20 --separate-roots --no-revocation-uris`: a P-521 hierarchy with the three anchors of `-20` Annex C. The **V2G root** is above the CPO Sub-CAs and the SECC leaf, the **OEM root** above the vehicle Sub-CAs and the vehicle leaf, and the **MO root** above the MO Sub-CAs and the contract leaf. The builder verified its own five chains `OK` |
| Credentials | Each leaf in a PKCS#12 with its two Sub-CAs, leaf-issuer first. The station uses `--server-cert`, and the car uses `--vehicle-cert` and `--contract-cert` |
| TLS | BouncyCastle backend on both sides: TLS 1.3 only for `-20`, mutual (`--require-client-cert`), P-521 with `ecdsa_secp521r1_sha512` |
| Trust | The station's `--trust-roots` holds OEM + MO (the car's TLS chain and its contract chain), and the car's holds V2G (the station's TLS chain). Nothing is pinned: without `--pki-dir` neither side has a leaf to pin, so the roots are the only check |
| Station | DC, signing meter `VAN*M1`, `--metering-confirmation 1`, `--require-metering-confirmation` |
| Script | [`scripts/run.sh`](scripts/run.sh), from `libs/EVSimulatorApp/libs/WWCP_ISO15118` after a Release build of the SECC, the EVCC and `V2GPKI_CLI`. It mints, packages and runs all three cases |

## What happened

The script's run is kept in [`sessions/`](sessions/). Two runs by hand before it, on their own hierarchy,
gave the same lines.

### Every chain to its root

```
TLS client: chain valid, anchored at CN=OEM Root CA, DC=OEM, DC=V2G, O=V2G PKI, C=DE.
Plug & Charge: contract CN=Contract, …; challenge OK, digest OK, signature OK (…#ecdsa-sha512,
  grammar=xmldsig-standalone); chain valid (anchored at CN=MO Root CA, DC=MO, DC=V2G, O=V2G PKI, C=DE).
MeteringConfirmation: asked 2, answered 2, unanswered in time 0.
  1600 Wh: session OK, reading OK, receipt OK, control mode OK, digest OK, signature OK (grammar=xmldsig-standalone); answered OK.
  2400 Wh: session OK, reading OK, receipt OK, control mode OK, digest OK, signature OK (grammar=xmldsig-standalone); answered OK.
```

The car's side:

```
TLS station: chain valid, anchored at CN=V2G Root CA, DC=Root, DC=V2G, O=V2G PKI, C=DE.
  19 exchanges, 4004 bytes on the wire (request side), auth: pnc-signed, session setup: OK_NewSessionEstablished.
  MeteringConfirmation: signed 2, last 2400 Wh at VAN*M1 (this vehicle counted 2400 Wh); answers: OK, OK.
```

Three anchors in one session, each for the branch the profile puts under it. The contract the
confirmations were verified against is the one that chained to the MO root.

### The car trusts the wrong root

The car was given only the MO root. It rejected the station's TLS chain, and both sides aborted with
`bad_certificate(42)` before any V2G message. So the car's check refuses as well as passes.

### The station lacks the MO root

The station was given only the OEM root. The car's TLS chain still anchored there, and the contract
chain was **rejected**: `chain REJECTED — Eine Zertifikatkette zu einer vertrauenswürdigen
Stammzertifizierungsstelle konnte nicht aufgebaut werden.` The session went on anyway. The station
granted authorization and charged, and it answered both confirmations `OK`, every check passed, although
`--require-metering-confirmation` was set.

That is how our station is built, not a fault of this run:

- **The station observes the Plug & Charge result and does not act on it.**
  `Secc20Base.Auth` records challenge, digest, signature and chain, and answers `OK` whatever they say.
  This keeps a live interop session going and its verdict visible, as its own comment says.
- **A confirmation is not judged by the contract chain.** `ConfirmMetering` checks session, reading,
  receipt, control mode, digest and signature against the contract leaf the car authorised with.
  `--require-metering-confirmation` refuses a confirmation that fails one of those checks. A good
  signature from a contract the station could not trust is none of them.

So with `--require-metering-confirmation` set, the station refuses a forged confirmation but not one
from an untrusted contract. A station that charged on that contract has accepted it already, so this is
consistent. It is still a gap for a station meant to *require* a confirmation it can rely on.

### After the fix: the untrusted contract is refused

Fixed the same evening: [WWCP_ISO15118#58](https://github.com/OpenChargingCloud/WWCP_ISO15118/pull/58)
(`2917c0a`, merged as `0b050d3`), followed by EVSimulatorApp `664945b` and by this repository. Each `MeteringConfirmationCheck` now carries the contract's chain verdict from
`AuthorizationReq`, and a confirmation holds only if that chain reached a configured root. A station
with no roots behaves as before. `Auth` still grants whatever the chain verdict. The same script, on
that commit, before the merge, logs in [`sessions/after-fix/`](sessions/after-fix/):

```
Plug & Charge: contract CN=Contract, …; … chain REJECTED — …
MeteringConfirmation: asked 1, answered 1, unanswered in time 0.
  1600 Wh: session OK, reading OK, receipt OK, control mode OK, digest OK, signature OK (grammar=xmldsig-standalone), contract chain REJECTED; answered FAILED_MeteringSignatureNotValid.
```

The car stops with `FAILED_MeteringSignatureNotValid` at the first confirmation. The `ok` case is
unchanged apart from `contract chain OK` on each line, and the wrong-root case still ends in the
handshake. The car's line now reads `validating the station's chain against --trust-roots (no leaf to pin
without --pki-dir)`.

## What it does not show

- **Not a second implementation.** Our station checked our car. The three open choices in the concept
  paper (§11.6: the signed octets, the signature form, one side-stream request at a time) are exactly as
  open as before.
- **No revocation.** The hierarchy was minted with `--no-revocation-uris`, and nothing checked a CRL or
  OCSP. The chain validation proves anchoring and path, not that a certificate is unrevoked.
- **The contract leaf's subject is the builder's `CN=Contract`**, not an EMAID. Nothing in this run
  checked it. The first TLS run used an EMAID-shaped one (`DEOCCC521000001`), and its confirmations
  passed the same checks.
- **The key-exchange group is not recorded.** As before, neither program prints what was negotiated.
- **A misleading line on the car's side, before the fix.** With `--vehicle-cert` and no `--pki-dir`,
  the car printed `WARNING: … accepting ANY station certificate`, and with `--trust-roots` that was not
  true. The next line, `TLS station: chain valid, anchored at …`, and the refusal in the wrong-root run
  show the chain was checked. WWCP_ISO15118#58 fixes the line.
- **No `frames.log`.** The console output in `sessions/` is the record. The bytes of the exchange are in
  the corpus trace, recorded over plain TCP.
