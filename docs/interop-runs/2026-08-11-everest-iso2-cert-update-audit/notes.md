# 2026-08-11 — a static sweep of `EvseV2G`'s seventeen `-2` handlers, and the one that answers from a union

Third finding of the day and the only one with **no measurement at all** — a source audit, said so on
the first line of the filing. What makes it worth having anyway is that the sweep is complete: every
`handle_iso_*` handler in the module was checked for the same property, and exactly one fails it.

| | |
|---|---|
| Requirement | `[V2G2-556]` the SECC **shall act on** a received `CertificateUpdateReq` rather than merely acknowledge it; `[V2G2-557]`/`[V2G2-558]` answer `OK` on success and `FAILED` otherwise; `[V2G2-736]` fill the mandatory fields with schema-conformant values regardless; `[V2G2-891]` what a conformant EV then does |
| Read | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), `modules/EVSE/EvseV2G/`; [SwitchEV/iso15118](https://github.com/SwitchEV/iso15118) `d645255` |
| Measured | **nothing.** No session was run |
| Outcome | `handle_iso_certificate_update` is an empty `// TODO` whose response is sent anyway, from a union member nothing writes. Filed: [`everest-evsev2g-certificate-update.md`](../../reports/everest-evsev2g-certificate-update.md) |

## Why this became askable today

Our own `-2` station and EVCC learned contract provisioning this morning
(`WWCP_ISO15118` `c1a7989`, *"Teach the -2 station to hand out contracts"*) — `CertificateInstallation`
and `CertificateUpdate`, the service advertised as a `-2` value-added service and selected by id. Until
then **our car could not ask any station for a contract**, so no counterparty's `-2` provisioning path
had ever been looked at by this project.

**Fifth time this month that a capability of ours opened a question about somebody else.** The
running-limit clamp, the MeterInfo request, the wrong SessionID, the charge-loop silence — and now
this. The difference is that the previous four were *measured* the same day and this one was not: the
capability exists, the rig session does not, and the filing's first checklist item is that run.

## The finding, in four greps

1. `iso_server.cpp:1817-1820` — the handler is `// TODO: implement CertificateUpdate handling` and
   `return V2G_EVENT_NO_EVENT;`.
2. `iso_server.cpp:2326-2332` — the dispatch has already set `CertificateUpdateRes_isUsed = 1u` and
   called `init_iso2_CertificateUpdateResType`, and cites `[V2G2-556]` on the line calling the stub.
   `V2G_EVENT_NO_EVENT` is `0` (`v2g.hpp:79`); the only value that suppresses the answer is
   `V2G_EVENT_IGNORE_MSG`, which the tail of `iso_handle_request` tests for.
3. **`iso2_BodyType`'s bodies are a `union`** (`iso2_msgDefDatatypes.h:2141-2178`), and none of the
   three inits touches the members: `init_iso2_exiDocument` is `(void) exiDoc;`,
   `init_iso2_BodyType` clears only the `_isUsed` bitfields (which sit outside the union), and
   `init_iso2_CertificateUpdateResType` clears only `RetryCounter_isUsed`
   (`iso2_msgDefDatatypes.c:418-420`).
4. Every `*ResType` starts with `iso2_responseCodeType ResponseCode`, so the first bytes of the union
   are the **previous** response's code — `OK`, in any session that reached this message.

## The sweep, which is the part worth keeping

The tempting way to write this up is *"an unset field in a union"*, which sounds like a class of
problem. It is not one. All seventeen `handle_iso_*` handlers were checked for whether they assign
`ResponseCode` at all:

| handler | assignments | body |
|---|---:|---:|
| `handle_iso_payment_details` | 13 | 179 |
| `handle_iso_payment_service_selection` | 8 | 128 |
| `handle_iso_power_delivery` | 11 | 175 |
| `handle_iso_authorization` | 10 | 85 |
| `handle_iso_charge_parameter_discovery` | 9 | 265 |
| `handle_iso_session_setup` | 4 | 59 |
| `handle_iso_cable_check` · `certificate_installation` · `current_demand` · `pre_charge` · `service_detail` · `session_stop` · `welding_detection` | 3 each | 29–137 |
| `handle_iso_charging_status` · `metering_receipt` · `service_discovery` | 2 each | 42–73 |
| **`handle_iso_certificate_update`** | **0** | **3** |

Sixteen of seventeen, between 2 and 13 assignments. One outlier. A filing that can say how far it
looked is worth more than one that cannot — same argument as the
[libcbv2g sweep](../2026-08-11-libcbv2g-grammar-sweep/notes.md), at a much smaller scale.

## What was deliberately not claimed

**It is not a memory disclosure.** The obvious escalation — stale length fields causing the encoder to
copy past the element — does not happen: `exi_basetypes_encoder_bytes` bounds-checks
(`exi_basetypes_encoder.c:67-72`, `bytes_len > bytes_size → EXI_ERROR__BYTE_BUFFER_TOO_SMALL`). So a
wild stale length fails the encode instead. The report says this before someone reads it the other
way, which is the same discipline as the `[V2G2-904]`-is-a-*may* paragraph in
[the metering filing](../2026-08-11-everest-iso2-metering-receipt/notes.md) earlier the same day.

**Which of the two outcomes is real was not determined.** Either the stale bytes encode — a
`CertificateUpdateRes` carrying the previous message's `OK` and five garbage elements — or they do not
and the encode fails. That depends on what the preceding response left behind, and settling it needs
the session nobody has run.

**The severity is bounded by the EV.** `[V2G2-891]` makes a conformant EVCC verify the response
signature over four named elements, require the signer to chain to a V2G root with `DC=CPS`, and
discard the message if any of it fails. There is no `Signature` here at all, so a car that checks in
that order throws it away. A car that reads `ResponseCode` first sees `OK`.

## The contrast that decides the filing

**Josev does not implement `-2` `CertificateUpdate` either** — and answers correctly.
`secc/failed_responses.py:488-495` holds a prepared response:

```python
CertificateUpdateReq: CertificateUpdateRes(
    response_code=ResponseCodeV2.FAILED,
    cps_cert_chain=CertificateChainV2(certificate=bytes(1)),
    contract_cert_chain=CertificateChainV2(certificate=bytes(1)),
    encrypted_private_key=EncryptedPrivateKey(id="", value=bytes(1)),
    dh_public_key=DHPublicKey(id="1", value=bytes(1)),
    emaid=EMAID(id="1", value="123456789ABCDE"),
),
```

That is `[V2G2-558]` and `[V2G2-736]` in nine lines, from a stack that has no more intention of
renewing a contract than EVerest does. It is what makes the filing *"answer the way you already answer
everything else you cannot do"* rather than *"implement contract renewal"* — a one-function fix
instead of a feature request, which is the difference between a report that gets acted on and one that
gets a roadmap answer.

## Reproduce

No script; four greps against `everest-core` at 2026.02.1:

```bash
sed -n '1817,1820p;2326,2332p' modules/EVSE/EvseV2G/iso_server.cpp
sed -n '2141,2178p'            lib/everest/cbv2g/include/cbv2g/iso_2/iso2_msgDefDatatypes.h
sed -n '418,420p'              lib/everest/cbv2g/lib/cbv2g/iso_2/iso2_msgDefDatatypes.c
sed -n '67,72p'                lib/everest/cbv2g/lib/cbv2g/common/exi_basetypes_encoder.c
```

The sweep table above is one loop over `handle_iso_*` counting `ResponseCode` assignments per body;
it is in the filing rather than in a tool because it is six lines of shell and answers one question.
