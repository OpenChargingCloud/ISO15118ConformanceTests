# Draft report to EVerest (`EvseV2G`) — the signed-metering path is open at both ends

Status: **draft, not sent.** Post it under your own name; see *Before sending* at the bottom.

Evidence in this repository:
[`2026-08-11-everest-iso2-metering-receipt`](../interop-runs/2026-08-11-everest-iso2-metering-receipt/notes.md).

**Read the second paragraph of *What the standard asks* before deciding how to weigh this.** One of
the three things below is a `shall`, one is expressly a `may`, and the report is written that way on
purpose.

---

**Title:** ISO 15118-2 `MeterInfo` goes out with two of its five elements — the meter's own signature
is dropped a field earlier — and the EV's signed `MeteringReceiptReq` for it reaches an empty
function

**Version:** everest-core **2026.02.1** (`b61bb12b8`), `modules/EVSE/EvseV2G/`.

## The defect, in two halves

### Going out: the meter signs, and the signature never reaches the message

`handle_update_meter_info` is handed a whole `types::powermeter::Powermeter` and reads two fields out
of it:

```cpp
// modules/EVSE/EvseV2G/charger/ISO15118_chargerImpl.cpp:528-543
void ISO15118_chargerImpl::handle_update_meter_info(types::powermeter::Powermeter& powermeter) {
    v2g_ctx->meter_info.meter_info_is_used = 1;
    v2g_ctx->meter_info.meter_reading = powermeter.energy_Wh_import.total;

    if (powermeter.meter_id) { … }
}
```

`powermeter.energy_Wh_import.total` is a plain number. **The same object carries the signed reading
beside it** — `types/powermeter.yaml:265-273`, under your own comment *"Extension for individual
signed meter values"*:

```yaml
energy_Wh_import_signed:
  $ref: /units_signed#/Energy       # .total is a SignedMeterValue:
                                    #   signed_meter_data, signing_method,
                                    #   encoding_method, public_key, timestamp
```

It is never read. There is nowhere to put it if it were — `v2g_ctx->meter_info` (`v2g.hpp:259-263`)
has three members: `meter_info_is_used`, `meter_reading`, `meter_id`. And **`SigMeterReading` does
not occur anywhere in the `EvseV2G` module**, in any file.

So `iso_server.cpp:1717-1726` (`ChargingStatusRes`) and `:2041-2050` (`CurrentDemandRes`) fill
`MeterID` and `MeterReading`, and leave `SigMeterReading`, `MeterStatus` and `TMeter` unset.

### Coming back: the EV signs, and nothing reads it

`[V2G2-903]` makes every PnC EV sign the `MeteringReceiptReq` acknowledging that record. Your station
receives it here:

```cpp
// modules/EVSE/EvseV2G/iso_server.cpp:1766-1808
static enum v2g_event handle_iso_metering_receipt(struct v2g_connection* conn) {
    …
    publish_iso_metering_receipt_req(req);
    dlog(DLOG_LEVEL_TRACE, "EVSE side: meteringReceipt called");
    …                                    // five TRACE lines of individual fields
    res->ResponseCode = iso2_responseCodeType_OK;
```

and `publish_iso_metering_receipt_req` is:

```cpp
// modules/EVSE/EvseV2G/iso_server.cpp:498-500
static void publish_iso_metering_receipt_req(struct iso2_MeteringReceiptReqType const* const v2g_metering_receipt_req) {
    // TODO: publish PnC only
}
```

Three facts follow, each checkable in one grep:

- **`check_iso2_signature` has exactly one call site in the whole module** — `iso_server.cpp:1187`,
  the `AuthorizationReq`. Nothing verifies the receipt.
- **`FAILED_MeteringSignatureNotValid` occurs nowhere in production code** — only in
  `tests/din_server_test.cpp:414`, as a value assigned to a temporary.
- **The receipt is not forwarded either.** The publish function is empty, so after
  `handle_iso_metering_receipt` returns, the signature exists nowhere in EVerest.

The `MeterInfo` the receipt acknowledges, the `SAScheduleTupleID` and the body `SessionID` are not
compared against what the station itself sent, either.

## What the standard asks — and what it does not

| | | |
|---|---|---|
| **`[V2G2-902]`** | **shall** | the `MeterInfo` the SECC sends in `ChargingStatusRes` shall be the meter's own output and nothing else, unconditionally — the raw reading, in a form a machine can generally read |
| `[V2G2-903]` | shall | the EVCC shall sign the `MeteringReceiptReq` body with the contract certificate's private key |
| `[V2G2-904]` | **may** | the SECC/SA **may** verify that signature. Its NOTE 1 gives the alternative the *may* presupposes: the SECC may hand the signed receipt, with the contract certificate, to a **secondary actor** that re-verifies it later |
| `[V2G2-481]` | shall | `MeteringReceiptRes` carries `FAILED_MeteringSignatureNotValid` where the SECC cannot validate the signature or the reading does not match — qualified by *and the SECC requires that the signature is valid* |

**So not verifying is permitted, and this report does not claim otherwise.** `[V2G2-904]` is a `may`,
and `[V2G2-481]`'s duty is conditional on requiring validity. What is left is:

1. **`[V2G2-902]`, a plain `shall`**, and the one this report is actually about. A powermeter that
   produces a signed reading, forwarded as a bare integer, is not the meter's own output — and the
   signed value is not somewhere else in the system, it is a sibling field on the argument the handler
   already has.
2. **`[V2G2-904]`'s `may` has no second door here.** Neither verified nor forwarded nor stored. The
   permission to skip verification exists because someone else can do it later; nobody can.

## Measured

Not a source reading: this is off your station's wire, decoded by a codec that is neither yours nor
ours — V2Gdecoder, which is RISE-V2G plus EXIficient. Both frames come from a complete `-2` DC charge
recorded on 2026-08-02, entries `[30]` and `[31]` of the same session:

```xml
<!-- frame [30], 85 B -->
<ns5:SAScheduleTupleID>1</ns5:SAScheduleTupleID>
<ns5:MeterInfo>
  <ns6:MeterID>DC_POWERMETER</ns6:MeterID>
  <ns6:MeterReading>0</ns6:MeterReading>
</ns5:MeterInfo>
```

**Two of five.** `MeterInfoType` carries `MeterID`, `MeterReading`, `SigMeterReading`, `MeterStatus`
and `TMeter`; your `CurrentDemandRes` carries the first two. The next response, `[31]` at 68 bytes,
has **no `MeterInfo` at all** — `meter_info_is_used` is a one-shot flag reset at `iso_server.cpp:2048`,
which is a separate design question and not this report's.

`DC_POWERMETER` is readable as plain ASCII in the raw frame, so the presence of the element needs no
decoder to confirm; the decode is what shows precisely which of its children are absent.

**One thing to be exact about, because it is the obvious way to deflect this.** `[V2G2-902]` names
`ChargingStatusRes` — the **AC** message — and nothing in the text to hand states the same obligation
for `CurrentDemandRes`. The frame above is a DC session, because that is the session we had recorded.
That does not move the finding, and here is why: both messages are populated from the same three-member
`v2g_ctx->meter_info`, by the same two blocks written the same way — `iso_server.cpp:1717-1726` for
`ChargingStatusRes` and `:2041-2050` for `CurrentDemandRes`. The omission is in what the struct can
hold, so the AC path that `[V2G2-902]` names directly has it too. **Whether the DC message carries the
same obligation is not something we claim** — an AC capture would settle the citation, and we have not
run one.

## Why this is worth fixing rather than a shrug

`receipt_is_required` is a real command on your own interface — `interfaces/ISO15118_charger.yaml:107`,
*"used by the SECC to indicate that the EVCC is required to send a MeteringReceiptReq message for the
purpose of signing the meter info record"* — wired through to
`ISO15118_chargerImpl::handle_receipt_is_required` and into `ChargingStatusRes`/`CurrentDemandRes`.
A CSO can switch it on today.

What they get for it is a protocol round trip per receipt and no artefact: the EV signs an
unauthenticated integer, and the signature it produces is discarded by an empty function. That is not
a conformance failure of the receipt half — it is a feature whose output has no consumer, and the one
piece of data in that message which **cannot be reconstructed from anywhere else** is the one that is
dropped. The station knows its own `MeterID`, reading and tuple; it does not have, and can never
recompute, the EV's signature.

## Suggested fix

Three separable pieces; the first is the `shall` and the other two are what make it useful.

1. **Carry the meter's signature into `MeterInfo`.** Give `v2g_ctx->meter_info` a
   `SigMeterReading` buffer, fill it in `handle_update_meter_info` from
   `powermeter.energy_Wh_import_signed->total`, and set `res->MeterInfo.SigMeterReading` beside
   `MeterReading` at both call sites. `sigMeterReadingType` is `xs:base64Binary` with a 64-byte
   maximum, so what fits depends on `signing_method` — worth deciding explicitly which of your
   signing methods can be represented, and warning rather than truncating for those that cannot.
   `MeterStatus` and `TMeter` are optional and are the cheap part of the same edit.
2. **Publish the receipt.** `publish_iso_metering_receipt_req` needs a body and the interface needs
   somewhere to put it — the whole `MeteringReceiptReq` including its `Signature`, base64-encoded, in
   the shape `publish_iso_certificate_installation_exi_req` already uses for a different message. That
   is what makes `[V2G2-904]` NOTE 1's secondary actor possible.
3. **Optionally verify it locally.** You already have `check_iso2_signature` and the contract
   certificate from `PaymentDetailsReq`; `[V2G2-481]` then has a code to answer with. This is the
   *may*, and skipping it is defensible once (2) exists.

Whether (1) alone is worth shipping without (2) is yours to judge — it is the half with a requirement
behind it.

## Context: three ISO 15118-2 stacks, three different halves

| stack | `SigMeterReading` in `ChargingStatusRes`/`CurrentDemandRes` | verifies `MeteringReceiptReq` |
|---|---|---|
| Josev (SwitchEV) | **no** — `meter_info=…get_meter_info_v2()` is commented out at both `-2` call sites (`iso15118_2_states.py:2147`, `:2494`), so no `MeterInfo` is sent at all | **yes** — `iso15118_2_states.py:1962-1979`, and it stops the session with *"Unable to verify signature of MeteringReceiptReq"* |
| **EVerest `EvseV2G`** | **no** — sends `MeterInfo` with two of five elements | **no**, and does not forward it either |
| *(ours)* | **yes** when a meter is configured, `null` when none is | **yes**, and records the verdict per receipt |

Nobody has all of it. Josev verifies a receipt for a record it never sends; you send a record and
discard the receipt. That is worth knowing before treating either half as exotic.

**And your `-20` module is the same shape, one step worse** — `meter_info` is never set on a
`ChargeLoopRes` at all, which we measured with a control in August and
[filed separately](everest-d20-meter-info.md). The two reports are about one capability in two
message sets, and neither fix reaches the other module.

---

## Before sending

- [x] **Reproduce it yourself.** Their own recorded frames, decoded by an independent codec
      (V2Gdecoder / EXIficient), with the reproduction script in the run directory. No rig needed —
      the bytes were recorded on 2026-08-02 for an unrelated reason.
- [x] **Separate the `shall` from the `may`.** `[V2G2-902]` is the requirement; `[V2G2-904]` is
      expressly permissive and the report says so before making its case.
- [x] **Check that the feature is reachable.** `receipt_is_required` is a documented command on their
      own charger interface, not dead code.
- [x] **Say where it is not exotic.** Josev implements the verification half; we implement both.
      Nobody implements all of it, and the table says so.
- [ ] **Re-read the citations against the tree before posting.** Nine file:line references here.
- [ ] **Consider filing (1) and (2) as two issues.** They have different severities — one is a
      `shall`, one is an unusable feature — and different fixes in different files. The rule that has
      served this directory is that a single issue invites a single answer, and the weaker one decides
      it.
- [ ] **Ask rather than assert about the truncation question.** `sigMeterReadingType` caps at 64
      bytes and some signing methods will not fit; what EVerest should do there is their design call,
      not ours.
- [ ] **Post under your own name, in your own words.**
