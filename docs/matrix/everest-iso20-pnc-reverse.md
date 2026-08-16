# EVerest's signed AuthorizationReq, verified by us

**Matrix cell:** SECC · ISO 15118-20 · Plug & Charge · EVerest

Back to the [interop matrix](../../README.md).

---

Their `Evse15118D20` has -20 PnC commented out, so the `EV→` leg is theirs to fix; the `←SECC` leg
ran as a by-product of the MCS reverse session. The `EV→` result for **-2** is the separate cell above.

**"Verified by our SECC" meant the signature.** Every inbound Plug & Charge result in this matrix was
recorded with `ChainResult.NotConfigured` — the ECDSA signature checked against the leaf the car
presented, with nobody asking who issued it — because both station classes have carried a
`ContractChainValidator` that no interop run could set, and because the `-20` report line printed the
three signature checks and not the chain. Both are fixed; the `-20` EVerest cell is **re-taken** over
mutual TLS 1.3 with the anchor configured — *chain trusted, anchored at `CN=MORootCA`*, their EV's own
`SubCertificates` walked to it — against a control at the **V2G** root that refuses the chain while the
signature still verifies. **Earlier recordings are not retroactively upgraded**, and the Josev `←SECC`
cells still carried the weaker claim, which is why they went `◐`: same one variable, against their own MO
root. They were closed the same night⁴⁰, and with them **every inbound Plug & Charge result in this
matrix is anchored** — the two Josev cells, this one, and the `-2` EVerest cell of the same day³⁸.
<br>~~*and eVDriveFlow's*~~ — **withdrawn 2026-08-15, hours after it was written.** This footnote named
an eVDriveFlow `←SECC` PnC cell as carrying the same overstatement. There is no such cell:
[they implement no Plug & Charge](docs/interop-runs/2026-08-11-edf-pnc-source-audit/notes.md), established
by source audit four days earlier, and the matrix row has said `— they implement none`²⁸ ever since. The
claim never matched the table it was written under.
[`…-d20-reverse-pnc-chain`](docs/interop-runs/2026-08-15-everest-d20-reverse-pnc-chain/notes.md).
