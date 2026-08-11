# Draft report to EVerest (`EvseV2G`) — `CertificateUpdateRes` is sent from the union slot the previous response left behind

Status: **draft, not sent**, and **not observed on the wire** — the first checklist item says so, and
the instrument to do it exists as of the same day. Post it under your own name; see *Before sending*.

Evidence in this repository:
[`2026-08-11-everest-iso2-cert-update-audit`](../interop-runs/2026-08-11-everest-iso2-cert-update-audit/notes.md).

---

**Title:** `handle_iso_certificate_update` is an empty `// TODO` that still answers — the response body
is a union member nothing ever writes, so `ResponseCode` is the preceding message's and the five other
mandatory elements are bytes of a different type (`[V2G2-556]`, `[V2G2-558]`, `[V2G2-736]`)

**Version:** everest-core **2026.02.1** (`b61bb12b8`), `modules/EVSE/EvseV2G/`.

## The defect

The handler:

```cpp
// modules/EVSE/EvseV2G/iso_server.cpp:1817-1820
static enum v2g_event handle_iso_certificate_update(struct v2g_connection* conn) {
    // TODO: implement CertificateUpdate handling
    return V2G_EVENT_NO_EVENT;
}
```

An unimplemented handler is a fair thing to have. What makes this a defect rather than a gap is that
**the caller sends anyway**:

```cpp
// modules/EVSE/EvseV2G/iso_server.cpp:2326-2332
} else if (exi_in->V2G_Message.Body.CertificateUpdateReq_isUsed) {
    dlog(DLOG_LEVEL_TRACE, "Handling CertificateUpdateReq");
    conn->ctx->current_v2g_msg = V2G_CERTIFICATE_UPDATE_MSG;

    exi_out->V2G_Message.Body.CertificateUpdateRes_isUsed = 1u;
    init_iso2_CertificateUpdateResType(&exi_out->V2G_Message.Body.CertificateUpdateRes);
    next_v2g_event = handle_iso_certificate_update(conn);   // [V2G2-556]
}
```

`V2G_EVENT_NO_EVENT` is `0` (`v2g.hpp:79`) — the ordinary carry-on value. The one return that would
suppress the answer is `V2G_EVENT_IGNORE_MSG`, and the tail of `iso_handle_request` tests for exactly
that before stamping the SessionID into the response. So the response goes out.

### What is in it

Four small facts, each one grep:

1. **`iso2_BodyType`'s message bodies are a `union`** — `iso2_msgDefDatatypes.h:2141-2178`, all 36 of
   them sharing one storage. `CertificateUpdateRes` is at `:2151`, `PaymentServiceSelectionRes` at
   `:2163`.
2. **`init_iso2_CertificateUpdateResType` clears one bit** — `iso2_msgDefDatatypes.c:418-420`, just
   `RetryCounter_isUsed = 0u`. It does not touch `ResponseCode` or the five other members.
3. **`init_iso2_exiDocument` is `(void) exiDoc;`**, and `init_iso2_BodyType` clears only the
   `_isUsed` bitfields — which live *outside* the union.
4. **Every `*ResType` begins with `iso2_responseCodeType ResponseCode`.** So the first bytes of that
   union are the previous response's response code.

Put together: nothing writes `CertificateUpdateRes`, so it reads as whatever the last response left
in the union. In any session that got as far as `CertificateUpdateReq` the previous response is
`PaymentServiceSelectionRes` — and a session that reached this point had `OK` there.

**So the station answers a contract-renewal request with the previous message's `OK`, and five
mandatory elements — `SAProvisioningCertificateChain`, `ContractSignatureCertChain`,
`ContractSignatureEncryptedPrivateKey`, `DHpublickey`, `eMAID` — reinterpreted from a differently
typed struct.**

### What it is *not*

**Not a memory disclosure**, and this report should say so before someone reads it as one.
`exi_basetypes_encoder_bytes` bounds-checks the length against the field's maximum
(`exi_basetypes_encoder.c:67-72`, `bytes_len > bytes_size → EXI_ERROR__BYTE_BUFFER_TOO_SMALL`), so a
stale length that is out of range fails the encode rather than copying past the field. Which of the
two happens — a garbage-but-encodable response, or an encode failure — depends on the bytes the
previous message left, and **we have not run it**. Both are wrong in the same way at the requirement
level; only the second is loud.

## What the standard asks

- **`[V2G2-556]`** — a *shall*: a `CertificateUpdateReq` that arrives must actually be **acted on**,
  its contents handled rather than merely acknowledged. Your dispatch cites this identifier on the
  very line that calls the stub.
- **`[V2G2-557]`** — answer `ResponseCode = OK` **only where that handling succeeded**.
- **`[V2G2-558]`** — answer `ResponseCode = FAILED` **where it did not**. Between them the standard
  leaves exactly two lawful answers and makes which one depends on work that here never happens.
  Not processing at all is `[V2G2-558]`'s case, and the code emitted is the other one.
- **`[V2G2-736]`** — whatever the outcome, the SECC fills the response's mandatory fields with
  schema-conformant values. Five of the six are stale bytes of another type.

**A conformant EV is protected, and that bounds the severity.** `[V2G2-891]` has the EVCC verify the
`CertificateUpdateRes` signature over `ContractSignatureCertChain`,
`ContractSignatureEncryptedPrivateKey`, `DHpublickey` and `ContractID`, check that the signer chains
to a V2G root and carries `DC=CPS`, and **discard the message** if any of that fails. The response
here carries no `Signature` at all, so a car that checks in that order throws it away. A car that
reads `ResponseCode` first — which is the order the field appears in — sees `OK`.

## Suggested fix

Two lines, and the second is the one that matters:

```cpp
static enum v2g_event handle_iso_certificate_update(struct v2g_connection* conn) {
    auto* res = &conn->exi_out.iso2EXIDocument->V2G_Message.Body.CertificateUpdateRes;
    // not implemented: answer per [V2G2-558] and fill the mandatory elements per [V2G2-736]
    res->ResponseCode = iso2_responseCodeType_FAILED;
    …                                   // schema-conformant placeholders for the five elements
    return V2G_EVENT_SEND_AND_TERMINATE;
}
```

`V2G_EVENT_SEND_AND_TERMINATE` is what your other refusal paths use, and it matches `[V2G2-539]`.
Whether to answer `FAILED` or to let the sequence guard produce `FAILED_SequenceError` is a
reasonable design choice — what is not is emitting the union's previous occupant.

**And the shape is bounded, so the fix is one function.** The risky pattern is *dispatch sets
`*_isUsed = 1`, calls a handler, handler returns without assigning `ResponseCode`*. We swept **all
seventeen** `handle_iso_*` handlers in `iso_server.cpp` for it:

| | assignments of `ResponseCode` | body |
|---|---:|---:|
| `handle_iso_payment_details` | 13 | 179 lines |
| `handle_iso_power_delivery` | 11 | 175 |
| `handle_iso_authorization` | 10 | 85 |
| …twelve more, between 2 and 9 | | |
| **`handle_iso_certificate_update`** | **0** | **3** |

Sixteen of seventeen always set it. There is exactly one outlier and this report is about it — worth
saying, because "an unset field somewhere in a union" reads like a class of problem, and it is not.

## Context: three ISO 15118-2 stacks

| stack | `-2` `CertificateUpdateReq` |
|---|---|
| Josev (SwitchEV) | **not implemented either — and answered correctly.** `secc/failed_responses.py:488-495` holds a prepared `CertificateUpdateRes(response_code=FAILED, …)` with every mandatory element filled with a schema-conformant placeholder. That is `[V2G2-558]` and `[V2G2-736]` in nine lines |
| **EVerest `EvseV2G`** | **empty `// TODO`, answer inherited from the union** |
| *(ours)* | implemented as of 2026-08-11, both `Install` and `Update` |

Josev is the useful row: **the fix here is not "implement contract renewal"** — nobody is asking for
that — it is "answer the way you already answer everything else you cannot do".

---

## Before sending

- [ ] **Put it on the wire — and there is a gate in front of it.** Still a source reading. The probe
      exists as of 2026-08-11 and was taken to their station the same day, but the request cannot be
      reached through the normal path: their `ServiceDiscoveryRes` advertises the certificate service
      with **parameter-set-ID 1 only** —
      `const int16_t cert_parameter_set_id[] = {1}; // parameter-set-ID 1: "Installation" service.
      TODO: Support of the "Update" service (parameter-set-ID 2)`
      (`charger/ISO15118_chargerImpl.cpp:226`) — so a car selecting set 2 is selecting a set that was
      never offered, and `PaymentServiceSelection` answers before `handle_iso_certificate_update` is
      ever called. Reaching the stub needs their advertisement changed, or the request injected past
      the gate. **The neighbouring run went green on the installation path**
      ([`…-iso2-cert-install`](../interop-runs/2026-08-11-everest-iso2-cert-install/notes.md)), which
      is what establishes that the rest of the route works and only this message is walled off.
      <br>Worth saying in the report itself: the handler is not merely unimplemented, it is
      **unreachable in the shipped configuration** — which lowers the severity and should be stated
      before a maintainer finds it and concludes the report was written without trying.
- [x] **Check that the message is reachable at all.** Your dispatch handles `CertificateUpdateReq`
      explicitly and cites `[V2G2-556]`; it is not dead code behind a config flag.
- [x] **Separate what is claimed from what is not.** Not a memory disclosure — the generated encoder
      bounds-checks lengths, and the report says so rather than leaving it to be assumed.
- [x] **Say where the requirement is not exotic.** Josev does not implement the feature either and
      still answers correctly, in nine lines.
- [ ] **Re-read the citations against the tree before posting.** Ten file:line references.
- [ ] **Ask, do not assert, about the intended answer.** `FAILED` versus letting the sequence guard
      produce `FAILED_SequenceError` is theirs to choose; the report offers the first and says so.
- [ ] **Post under your own name, in your own words.**
