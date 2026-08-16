# ISO 15118-2 contract provisioning against EVerest

**Matrix cell:** EVCC · ISO 15118-2 · Contract provisioning · EVerest

Back to the [interop matrix](../../README.md).

---

**On the wire 2026-08-16, and the gate turned out to be ours.** Their station's certificate service
advertises **parameter-set-ID 1 only** — `Update` is an explicit `TODO` — so a conformant car, which pairs
*Update → set 2*, is answered `FAILED_ServiceSelectionInvalid` and never reaches the handler. Ours was one.

Their **state table does not pair them**: the state after a Contract selection is
`WAIT_FOR_PAYMENTDETAILS_CERTINST_CERTUPD` and admits `CertificateUpdateReq` whatever set was named.
Naming set 1 and sending the Update reaches the handler their own dispatch says should handle it — an
**off-profile probe**, and every use of it says so.

**Installation** works through the real path: their station publishes our EXI over MQTT and waits 4 500 ms,
so with our own MO backend behind it the contract came back `OK` and the key unwrapped. **Update** answers
in ~134 ms with no phase opened at all:

```
| 4 | CertificateUpdateReq | CertificateUpdateRes | OK |

CryptographicException: DHpublickey: expected a 65-byte uncompressed P-256 point.
```

`ResponseCode = OK` from an empty `// TODO`, and five mandatory elements that are the previous response's
bytes out of a shared union. Filed as
[`everest-evsev2g-certificate-update`](../reports/everest-evsev2g-certificate-update.md);
runs: [`…-cert-install`](../interop-runs/2026-08-11-everest-iso2-cert-install/notes.md),
[`…-cert-update-live`](../interop-runs/2026-08-16-everest-iso2-cert-update-live/notes.md).

