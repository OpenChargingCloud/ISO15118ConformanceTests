# 2026-08-16 — `CertificateUpdateRes` on the wire: `ResponseCode = OK` from a handler that does nothing

[`everest-evsev2g-certificate-update`](../../reports/everest-evsev2g-certificate-update.md) was a source
reading for five days, with one unticked box: *"put it on the wire — and there is a gate in front of
it."* **The gate was ours.** The report is now confirmed in both halves, measured.

| | |
|---|---|
| Counterparty | everest-core **2026.02.1** (`b61bb12`), `config-dc2-pnc-validator-ours.yaml`, `-2` DC over TLS 1.2 |
| Probe | our EVCC names parameter set **1** — the one they advertise — and sends `CertificateUpdateReq` |
| Result | `CertificateUpdateRes` with **`ResponseCode = OK`** and a `DHpublickey` that is not a point |

## The gate was ours, not theirs

The report said the handler is *"unreachable in the shipped configuration"*, because their
`ServiceDiscoveryRes` advertises the certificate service with **parameter-set-ID 1 only** (`Update` is an
explicit `TODO` at `ISO15118_chargerImpl.cpp:226`), so a car selecting set 2 is answered
`FAILED_ServiceSelectionInvalid` at `PaymentServiceSelection`.

That is true of a **conformant** car, and our car is one: `Evcc2` paired *Update → set 2*. But their own
state table does not pair them. The state after `SelectedPaymentOption = Contract` is called

```cpp
iso_dc_state_id::WAIT_FOR_PAYMENTDETAILS_CERTINST_CERTUPD    // iso_server.cpp:948
```

and its mask admits `V2G_CERTIFICATE_UPDATE_MSG` alongside `V2G_CERTIFICATE_INSTALLATION_MSG`
(`iso_server.hpp:87` for AC, `:112` for DC) — **the state is chosen by the payment option, not by the
parameter set**, and the dispatch keys on `CertificateUpdateReq_isUsed` in the incoming EXI and on
nothing else. So naming the set they offer and sending the other message reaches the handler their own
dispatch says should handle it.

`Evcc2.CertificateParameterSetId` decouples the two, and **the car is deliberately non-conformant while
it is set.** That belongs in the report, and is in it.

## What came back

| arm | selects | sends | their side | our side |
|---|---|---|---|---|
| **install** (control) | set 1 | `CertificateInstallationReq` | `CertificateInstallation-phase started`, **4 500 ms**, then a response | `FAILED` — no MO backend behind it, as on 2026-08-11 |
| **update** (probe) | set 1 | `CertificateUpdateReq` | **no phase at all**, response in **~134 ms** | `ResponseCode = OK`, then a `DHpublickey` that is not a P-256 point |

The contrast is the direct evidence: the Installation path opens a phase and waits 4,5 s for a backend;
the Update path answers immediately, because `handle_iso_certificate_update` returns
`V2G_EVENT_NO_EVENT` without writing anything.

**And what it answered is the previous message's response code.** `PaymentServiceSelectionRes` was `OK`,
`CertificateUpdateRes` shares its union storage, and `init_iso2_CertificateUpdateResType` clears one bit
that is not `ResponseCode`:

```
| 4 | CertificateUpdateReq | CertificateUpdateRes | OK |
```

**A contract-renewal request was answered `OK` by a handler that is an empty `// TODO`.**

The mandatory elements are the same union's leftovers, and our EVCC found out the hard way — which is
the second half of the finding, and better evidence than a byte dump:

```
System.Security.Cryptography.CryptographicException :
    DHpublickey: expected a 65-byte uncompressed P-256 point.
```

The report named two possible outcomes — *"a garbage-but-encodable response, or an encode failure"* — and
said which one happens was unmeasured. **It is the first.** The response encodes, reaches a car, and
claims success.

## What this does not say

- **Not a memory disclosure**, and the report already argued that from their encoder's bounds check. This
  run does not change it: what arrived was wrong, not privileged.
- **Not a conformant client's experience.** A car that pairs the sets as the standard intends is refused
  at `PaymentServiceSelection` and never sees this. The severity that follows — the handler is
  unreachable in their shipped advertisement — is *lowered* by that and the report says so; what this run
  removes is the claim that it therefore cannot be observed.
- **The control failed too**, for a known and unrelated reason: no MO backend was standing behind the
  Installation path, so their station timed out at 4 500 ms and answered `FAILED`. Its value here is the
  *shape* of that path, not its verdict.

## Artifacts

[`ours.update.log`](ours.update.log) · [`ours.install.log`](ours.install.log) ·
[`flow.update.md`](flow.update.md) · [`flow.install.md`](flow.install.md) ·
[`frames.update.log`](frames.update.log) · [`their-station.log`](their-station.log)

## Next

- Nothing on this report: its last technical box is ticked and the remaining ones are a person's.
- The knob is `-2`-only. `-20` puts certificate provisioning behind `AuthorizationSetupRes` rather than a
  parameter set, so there is no equivalent gate to step around.
