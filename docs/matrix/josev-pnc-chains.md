# Josev's inbound Plug & Charge chains, anchored

**Matrix cell:** SECC · ISO 15118-2 and -20 · Plug & Charge, Mutual TLS 1.3 · Josev

Back to the [interop matrix](../../README.md).

---

**Both Josev inbound Plug & Charge cells, re-taken — and these two were stale for a different reason
than the one above.** `-2` over unilateral TLS 1.2 and `-20` over mutual TLS 1.3, each against a control:
*chain valid, anchored at `CN=MORootCA`* with the MO root in the store, *REJECTED — unable to get local
issuer certificate* without it, and the three signature checks identical in all four arms. Each pair of
station logs differs in **exactly two lines**, the store and the verdict. The `-2` arm covers the signed
`MeteringReceiptReq` as well, through the same contract key — no other counterparty has produced one.
<br>**Nothing was unreachable here; the claim simply outlived the run that earned it.** Every Josev
Plug & Charge run is dated 2026-07-22, and `--trust-roots` with the station's contract-chain validation
arrived on 2026-08-08 — six weeks in which the cell read as more than it had ever measured, with nothing
to flag it. **A capability the harness gains does not reach back through the matrix**, and that is a
second staleness mechanism beside the *value no caller could reach* of the footnote below.
<br>These two were also the **last** of it, which the footnote below got wrong on the same day and in
the same breath as stating the rule: it named a third cell, at eVDriveFlow, that does not exist.
<br>The `-20` arm settled one more thing unasked: with the store configured their car's **TLS client
chain** is validated instead of accept-any, and it anchors at `CN=OEMRootCA` — the class `[V2G20-2331]`
and clause 7.3.1 ask for, and the exact inverse of the [EVerest station](docs/reports/everest-d20-trust-anchor.md)
that took a contract certificate for the job. The *leaf* was left open and **audited the same day**: it
is `CN=OEMProvCert`, the provisioning certificate, where `[V2G20-2339]` and `[V2G20-2342]` put two
different credentials — the class is absent from their stack entirely, their own downstream fork already
implements it, and it is now the **forty-eighth filing**⁴¹.
[`…-josev-reverse-pnc-chain`](docs/interop-runs/2026-08-15-josev-reverse-pnc-chain/notes.md).
