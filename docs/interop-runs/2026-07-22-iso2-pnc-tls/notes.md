# Interop run — ISO 15118-**2** **Plug & Charge** over TLS, live in **both directions** vs Josev

- **Date:** 2026-07-22
- **Scope:** the full -2 PnC message set in a live session — `PaymentDetails` (contract chain ↔
  GenChallenge), the **signed `AuthorizationReq`**, and the **signed `MeteringReceiptReq`** — previously the
  last big codec-tested-only block.

## Reverse — Josev EVCC → our SECC (TLS unilateral, `--sdp`)

`secc --protocol 2 --mode ac --tls --server-cert secc.p12 --sdp`; Josev `evcc_config_pnc_ac.json`
(`useTls: true` — Josev picks Contract whenever it is offered **and** TLS is on). Outcome: ✅

```
-2 Plug & Charge: contract DC=MO, C=UK, O=Switch, CN=UKSWI123456791A;
                  challenge OK, digest OK, signature OK (grammar=xmldsig-standalone).
-2 MeteringReceipt: digest OK, signature OK (grammar=xmldsig-standalone).
✓ Session complete in 14061 ms.
```

Our SECC offers `Contract`, hands out the GenChallenge in `PaymentDetailsRes`, verifies the **signed
AuthorizationReq** (challenge echo + body-fragment digest + ECDSA, dual-grammar with the Josev
standalone-xmldsig fallback — Josev's `create_signature` is protocol-agnostic, so its -2 signatures use the
same standalone SignedInfo form as -20), demands **one** receipt via `ReceiptRequired` + `MeterInfo` in a
`ChargingStatusRes`, and verifies the EV's **signed MeteringReceiptReq**. Josev runs to `SessionStop`, exit 0.

## Forward — our EVCC → Josev SECC (TLS, `--contract-cert contract.p12`)

`evcc --protocol 2 --mode ac --tls-backend dotnet --contract-cert contract.p12`; Josev SECC
`SECC_ENFORCE_TLS=True`, `AUTH_MODES` default (EIM+PNC). Outcome: ✅

```
our EVCC : 12 exchanges … auth: pnc-signed.  ✓ Session complete in 2339 ms.
Josev    : PaymentDetailsReq received (contract chain accepted against its MO PKI)
           Verifying digest for element with ID 'id1'  => Match: True
           Signature verified successfully
```

Our EVCC selects Contract, sends `PaymentDetailsReq` (eMAID = contract CN `UKSWI123456791A`, chain from
`contract.p12`), and signs the `AuthorizationReq` in the Josev form (`XmlDsigInterop2`: SHA-256 fragment
digest, `Transforms`=[EXI C14N], SignedInfo over the standalone xmldsig grammar, ECDSA-P256 raw `r‖s`) —
verified by Josev's own EXIficient re-encoding. Josev's SECC hardcodes `receipt_required=False`, so the
forward run has no MeteringReceipt (covered by the reverse run).

## Three live findings (all fixed on our side)

1. **`SAScheduleList` is mandatory with `EVSEProcessing=Finished`** ([V2G2-905]): our
   `ChargeParameterDiscoveryRes` sent none — Josev crashed with
   `'NoneType' object has no attribute 'schedule_tuples'`. `Secc2` now offers one tuple / one 1-hour PMax
   entry. (Our loopback EVCC never read the schedules, which masked this.)
2. **Demand at most one receipt per session**: demanding one on every `ChargingStatusRes` loops a Josev
   EVCC forever (it only counts down its charge-loop cycles on receipt-free responses — observed live:
   1789 receipts before we pulled the plug). `Secc2.DemandReceipt()` fires exactly once.
3. **-2 SAP version is 2.0**: our EVCC offered `urn:iso:15118:2:2013:MsgDef` as version 1.0 —
   `Failed_NoNegotiation` from Josev (it matches namespace **and** major version; our own SECC matched
   namespace only). `SapHandshake` now offers 2.0 for -2, 1.0 for the -20 sets.

CI: `Secc2PnCTests` (signed auth + signed receipt + EIM untouched),
`Iso2LoopbackTests.AcPncSession_SignedAuthAndMeteringReceipts_VerifyAtSecc` (full loopback E2E).
Scripts: [`reverse-iso2-pnc-tls-sdp.sh`](../../../tools/interop-josev/reverse-iso2-pnc-tls-sdp.sh),
[`live-evcc-iso2-pnc-tls.sh`](../../../tools/interop-josev/live-evcc-iso2-pnc-tls.sh).
Logs: `secc-iso2-pnc.log`/`evcc-iso2-pnc.log` (reverse), `josev-secc-iso2-pnc.log`/`our-evcc-iso2-pnc.log`
(forward).

With this, every Plug &amp; Charge signature flow of **both** protocol generations is live-validated in
**both directions** against the independent Josev stack.
