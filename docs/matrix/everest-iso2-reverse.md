# The first ISO 15118-2 reverse session against EVerest

**Matrix cell:** SECC · ISO 15118-2 · AC, EIM *and* Plug & Charge · EVerest

Back to the [interop matrix](../../README.md).

---

**The first ISO 15118-2 reverse session against this counterparty, in any transport — and their car
is a different car over TLS.** Same config, one variable (the SDP security byte): over plain TCP it
authorizes by **EIM**; over **TLS 1.2** an extra `PaymentDetailsReq` appears, the `AuthorizationReq` is
signed, and a `MeteringReceiptReq` arrives inside the charge loop. That is `-2`'s own *no Contract
without TLS* rule, applied by the car — the same rule this project met from their **station** on
2026-08-03, now seen from the other end. It needed no PKI regeneration: `-2` TLS is unilateral, and
`enable_tls_1_3: false` is what makes their EV present no client certificate.
<br>**Two things of ours came out of it.** `Secc2` verifies the contract signature and every signed
metering receipt, and the interop fixture reported neither — so the run would have been judged on
`IsDone`, which a session with an unverifiable signature reaches just as well (our station reports the
verdict, it does not refuse on it). **Fifth instance in three days of *a value our own side already held
that no caller could reach*, and the first where the discarded value is the result of the run.** And the
first TLS arm then said `chain not validated`: both station classes have carried a
`ContractChainValidator` all along and **no interop run could set it**, so *every* inbound Plug & Charge
result in this matrix checked the signature against the leaf the car presented and never asked who issued
it. `V2G_INTEROP_CONTRACT_ROOTS` now sets it — verdict *trusted, anchored at `CN=MORootCA`*, with the V2G
root as a negative control that refuses the chain while the signature still verifies.
[`…-iso2-ac-reverse-tls12`](docs/interop-runs/2026-08-15-everest-iso2-ac-reverse-tls12/notes.md).
