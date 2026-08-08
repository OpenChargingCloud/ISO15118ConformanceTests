/*
 * cbv2g-defect-probe — does libcbv2g's WPT encoder actually fail on minOccurs="2" particles?
 *
 * Issue C of docs/reports/libcbv2g-grammar-deviations.md says three generated types cannot be
 * encoded at all, because the LOOP grammar state for a `minOccurs="2"` repeating particle has no exit
 * production: its only other branch is `error = EXI_ERROR__UNKNOWN_EVENT_CODE`. That was read out of
 * the generated C. Reading is not running, and a defect report that says "we traced the control flow"
 * invites the reply "did you try it".
 *
 * So this tries it, through the public API — `encode_iso20_wpt_exiDocument`, the entry point any
 * caller uses. The type-level encoders are `static`, which is just as well: going through the document
 * encoder means the result is what a user of the library would actually get.
 *
 * It runs in two parts, because the first version of this probe found that **issue B masks issue C**
 * and reported three false negatives before that was understood.
 *
 *   Part 1  LF_SystemSetupData present, VendorSpecificDataContainer EMPTY.
 *           Issue B leaves the suffix with no event code in that state, so the encoder never descends
 *           into it: it returns success and produces a message byte-identical in length to one that
 *           does not carry the field at all. The field is silently dropped.
 *
 *   Part 2  The same, with ONE container item, which gives the suffix a code. Now the encoder does
 *           descend, and issue C fires: UNKNOWN_EVENT_CODE for all three minOccurs="2" types.
 *
 * Each part has a control that must encode, so a failure cannot be this probe filling the struct
 * wrongly: the only difference between a control and a case is the particle under test.
 *
 * Two items each — the schema's own `minOccurs` — so every case is a *minimal valid* document.
 * Nothing exotic is being asked of the encoder.
 *
 * Build and run: see README.md. Nothing in `dotnet test` touches this.
 */

#include <stdio.h>
#include <string.h>

#include <cbv2g/iso_20/iso20_WPT_Datatypes.h>
#include <cbv2g/iso_20/iso20_WPT_Encoder.h>
#include <cbv2g/iso_20/iso20_ACDP_Datatypes.h>
#include <cbv2g/iso_20/iso20_ACDP_Encoder.h>
#include <cbv2g/iso_20/iso20_ACDP_Decoder.h>
#include <cbv2g/common/exi_bitstream.h>
#include <cbv2g/common/exi_error_codes.h>

static void set_header(struct iso20_wpt_MessageHeaderType* h) {
    memset(h, 0, sizeof(*h));
    h->SessionID.bytesLen = 8;               /* eight zero bytes, as in the vector corpus */
    h->TimeStamp          = 1700000000u;
    h->Signature_isUsed   = 0u;
}

/** The required scalars every FinePositioningSetup message needs, so the probe differs from the
 *  corpus vectors only in the field under test. */
static void fill_res_scalars(struct iso20_wpt_WPT_FinePositioningSetupResType* r) {
    set_header(&r->Header);
    r->ResponseCode = iso20_wpt_responseCodeType_OK;
    r->PrimaryDeviceFinePositioningMethodList.WPT_FinePositioningMethod.arrayLen = 1;
    r->PrimaryDeviceFinePositioningMethodList.WPT_FinePositioningMethod.array[0] =
        iso20_wpt_WPT_FinePositioningMethodType_Manual;
    r->PrimaryDevicePairingMethodList.WPT_PairingMethod.arrayLen = 1;
    r->PrimaryDevicePairingMethodList.WPT_PairingMethod.array[0] =
        iso20_wpt_WPT_PairingMethodType_LPE;
    r->PrimaryDeviceAlignmentCheckMethodList.WPT_AlignmentCheckMethod.arrayLen = 1;
    r->PrimaryDeviceAlignmentCheckMethodList.WPT_AlignmentCheckMethod.array[0] =
        iso20_wpt_WPT_AlignmentCheckMethodType_PowerCheck;
    r->NaturalOffset = 0;
    r->VendorSpecificDataContainer.arrayLen = 0;
}

static void fill_spec_data(struct iso20_wpt_WPT_TxRxSpecDataType* d, uint32_t id) {
    memset(d, 0, sizeof(*d));
    d->TxRxIdentifier      = id;
    d->TxRxPosition.Coord_X    = (int16_t) (id * 10);
    d->TxRxOrientation.Coord_Y = (int16_t) (id * 5);
}

/** Encodes and reports how many bytes came out — which turns out to matter more than the error code:
 *  a "successful" encode that is the same length as the empty message has dropped the field. */
static int encode(struct iso20_wpt_exiDocument* doc, size_t* written) {
    uint8_t buffer[4096];
    exi_bitstream_t stream;
    size_t pos = 0;
    exi_bitstream_init(&stream, buffer, sizeof(buffer), pos, NULL);
    int error = encode_iso20_wpt_exiDocument(&stream, doc);
    *written = exi_bitstream_get_length(&stream);
    return error;
}

/* NB: never write `report(..., encode(&doc, &n), n)`. C does not order argument evaluation, so `n`
 * can be read before `encode` fills it — which it was, and the first version of this probe printed
 * two zeroes and drew a conclusion from them. Encode into a local, then report. */
static int report(const char* name, const char* expectation, int error, size_t written) {
    const char* verdict = (error == EXI_ERROR__NO_ERROR) ? "encoded"
                        : (error == EXI_ERROR__UNKNOWN_EVENT_CODE) ? "UNKNOWN_EVENT_CODE"
                        : "other error";
    printf("  %-26s %-18s -> %-20s (%d)  %2zu B\n", name, expectation, verdict, error, written);
    return error;
}

/** Builds a SetupRes with `vendor_items` VendorSpecificDataContainer entries, so that the caller can
 *  make the LF_SystemSetupData suffix reachable or not — which is issue B, and which has to be got out
 *  of the way before issue C can even be reached. */
static struct iso20_wpt_WPT_LF_SystemSetupDataType* start_res(struct iso20_wpt_exiDocument* doc,
                                                              int vendor_items) {
    memset(doc, 0, sizeof(*doc));
    doc->WPT_FinePositioningSetupRes_isUsed = 1u;
    struct iso20_wpt_WPT_FinePositioningSetupResType* r = &doc->WPT_FinePositioningSetupRes;
    fill_res_scalars(r);
    r->VendorSpecificDataContainer.arrayLen = (uint16_t) vendor_items;
    for (int i = 0; i < vendor_items; i++) {
        r->VendorSpecificDataContainer.array[i].bytesLen = 1;
        r->VendorSpecificDataContainer.array[i].bytes[0] = (uint8_t) (0xA0 + i);
    }
    r->LF_SystemSetupData_isUsed = 1u;
    return &r->LF_SystemSetupData;
}

static void fill_receiver(struct iso20_wpt_WPT_LF_SystemSetupDataType* s, int items) {
    s->LF_ReceiverSetupData_isUsed = 1u;
    s->LF_ReceiverSetupData.NumberOfReceivers   = (uint8_t) items;
    s->LF_ReceiverSetupData.RxSpecData.arrayLen = (uint16_t) items;
    for (int i = 0; i < items; i++)
        fill_spec_data(&s->LF_ReceiverSetupData.RxSpecData.array[i], (uint32_t) (i + 1));
}

static void fill_transmitter(struct iso20_wpt_WPT_LF_SystemSetupDataType* s, int items, int with_package) {
    s->LF_TransmitterSetupData_isUsed = 1u;
    s->LF_TransmitterSetupData.NumberOfTransmitters = (uint8_t) items;
    s->LF_TransmitterSetupData.SignalFrequency.Exponent = 0;
    s->LF_TransmitterSetupData.SignalFrequency.Value    = 100;
    s->LF_TransmitterSetupData.TxSpecData.arrayLen = (uint16_t) items;
    for (int i = 0; i < items; i++)
        fill_spec_data(&s->LF_TransmitterSetupData.TxSpecData.array[i], (uint32_t) (i + 1));

    if (with_package) {
        struct iso20_wpt_WPT_TxRxPackageSpecDataType* p = &s->LF_TransmitterSetupData.TxPackageSpecData;
        s->LF_TransmitterSetupData.TxPackageSpecData_isUsed = 1u;
        p->PulseSequenceOrder.arrayLen = (uint16_t) items;
        for (int i = 0; i < items; i++) {
            p->PulseSequenceOrder.array[i].IndexNumber    = (uint16_t) (i + 1);
            p->PulseSequenceOrder.array[i].TxRxIdentifier = (uint32_t) (i + 1);
        }
        p->PulseSeparationTime   = 10;
        p->PulseDuration         = 20;
        p->PackageSeparationTime = 30;
    }
}

/* ---- Issue A: the ACDP document element code -----------------------------------------------
 *
 * A is not a failure — it is a different byte — so it needs a different kind of evidence. What this
 * part does is produce cbV2G's *own* bytes for the two affected messages and name which element code
 * went into them, so the report does not have to point at our encoder for any of it. Feed the hex to
 * a schema-informed processor (tools/interop-exificient/) and it will name a different message.
 *
 * Also decodes each one back through cbV2G's own decoder, which reads it correctly — the point being
 * that the pair is perfectly self-consistent, and that self-consistency is exactly what cannot detect
 * this class of defect.
 */

static void set_header_acdp(struct iso20_acdp_MessageHeaderType* h) {
    memset(h, 0, sizeof(*h));
    h->SessionID.bytesLen = 8;
    h->TimeStamp          = 1700000000u;
    h->Signature_isUsed   = 0u;
}

static void print_hex(const uint8_t* bytes, size_t len) {
    for (size_t i = 0; i < len; i++) printf("%02x", bytes[i]);
}

/** Encodes one ACDP message, prints its hex and its document element code, then decodes it back with
 *  cbV2G and reports which element cbV2G itself thinks it is. */
static void acdp_case(const char* name, int is_connect_res) {
    struct iso20_acdp_exiDocument doc;
    uint8_t buffer[512];
    exi_bitstream_t stream;
    size_t pos = 0, written;

    memset(&doc, 0, sizeof(doc));
    if (is_connect_res) {
        doc.ACDP_ConnectRes_isUsed = 1u;
        struct iso20_acdp_ACDP_ConnectResType* r = &doc.ACDP_ConnectRes;
        set_header_acdp(&r->Header);
        r->ResponseCode   = iso20_acdp_responseCodeType_OK;
        r->EVSEProcessing = iso20_acdp_processingType_Finished;
        r->EVSEElectricalChargingDeviceStatus = iso20_acdp_electricalChargingDeviceStatusType_State_C;
        r->EVSEMechanicalChargingDeviceStatus = iso20_acdp_mechanicalChargingDeviceStatusType_EndPosition;
    } else {
        doc.ACDP_DisconnectReq_isUsed = 1u;
        struct iso20_acdp_ACDP_ConnectReqType* q = &doc.ACDP_DisconnectReq;
        set_header_acdp(&q->Header);
        q->EVElectricalChargingDeviceStatus = iso20_acdp_electricalChargingDeviceStatusType_State_A;
    }

    exi_bitstream_init(&stream, buffer, sizeof(buffer), pos, NULL);
    if (encode_iso20_acdp_exiDocument(&stream, &doc) != EXI_ERROR__NO_ERROR) {
        printf("  %-20s encode failed\n", name);
        return;
    }
    written = exi_bitstream_get_length(&stream);

    /* The document element code is the six bits after the one-byte EXI header. */
    unsigned code = (unsigned) ((buffer[1] >> 2) & 0x3Fu);

    printf("  %-20s cbV2G writes element code %u   ", name, code);
    print_hex(buffer, written);
    printf("  (%zu B)\n", written);

    struct iso20_acdp_exiDocument back;
    memset(&back, 0, sizeof(back));
    exi_bitstream_init(&stream, buffer, written, (pos = 0), NULL);
    if (decode_iso20_acdp_exiDocument(&stream, &back) == EXI_ERROR__NO_ERROR) {
        const char* seen = back.ACDP_ConnectRes_isUsed    ? "ACDP_ConnectRes"
                         : back.ACDP_DisconnectReq_isUsed ? "ACDP_DisconnectReq"
                         : back.ACDP_ConnectReq_isUsed    ? "ACDP_ConnectReq"
                         : back.ACDP_DisconnectRes_isUsed ? "ACDP_DisconnectRes"
                         : "something else";
        printf("  %-20s cbV2G reads it back as %s\n", "", seen);
    }
}

int main(void) {
    struct iso20_wpt_exiDocument doc;
    size_t n, baseline;
    int err, contradicted = 0;

    printf("libcbv2g WPT encoder — the two defects interact, so order matters\n\n");

    /* ---- Part 1: the suffix is unreachable with an empty container (issue B) --------------- */

    printf("Part 1 — LF_SystemSetupData with VendorSpecificDataContainer EMPTY\n");

    memset(&doc, 0, sizeof(doc));
    doc.WPT_FinePositioningSetupRes_isUsed = 1u;
    fill_res_scalars(&doc.WPT_FinePositioningSetupRes);
    doc.WPT_FinePositioningSetupRes.LF_SystemSetupData_isUsed = 0u;
    err = encode(&doc, &baseline);
    report("control, field absent", "must encode", err, baseline);

    {
        struct iso20_wpt_WPT_LF_SystemSetupDataType* s = start_res(&doc, 0);
        fill_receiver(s, 2);
        err = encode(&doc, &n);
        report("receiver-2, empty vsdc", "?", err, n);
        if (err == EXI_ERROR__NO_ERROR && n == baseline)
            printf("      ^ same length as the message WITHOUT the field: encoded \"successfully\",\n"
                   "        with LF_SystemSetupData silently dropped\n");
        else
            contradicted++;
    }

    /* ---- Part 2: one container item makes the suffix reachable, and then C fires ----------- */

    printf("\nPart 2 — the same, with ONE VendorSpecificDataContainer item so the suffix has a code\n");

    {
        struct iso20_wpt_WPT_LF_SystemSetupDataType* s = start_res(&doc, 1);
        s->LF_ReceiverSetupData_isUsed = 0u;
        s->LF_TransmitterSetupData_isUsed = 0u;
        err = encode(&doc, &n);
        report("no LF branch", "must encode", err, n);
    }

    struct { const char* name; int receiver, items, package; } cases[] = {
        { "receiver-2",     1, 2, 0 },
        { "transmitter-2",  0, 2, 0 },
        { "package-spec-2", 0, 2, 1 },
    };

    for (size_t i = 0; i < sizeof(cases) / sizeof(cases[0]); i++) {
        struct iso20_wpt_WPT_LF_SystemSetupDataType* s = start_res(&doc, 1);
        if (cases[i].receiver) fill_receiver(s, cases[i].items);
        else                   fill_transmitter(s, cases[i].items, cases[i].package);

        err = encode(&doc, &n);
        report(cases[i].name, "claim: fails", err, n);
        if (err != EXI_ERROR__UNKNOWN_EVENT_CODE) contradicted++;
    }

    /* ---- Part 3: issue A, cbV2G's own ACDP bytes ------------------------------------------- */

    printf("\nPart 3 — ACDP document element codes, from cbV2G's encoder\n");
    printf("         EXI 1.0 Second Edition 8.5.1 sorts global elements by qname:\n");
    printf("         ConnectReq=0, ConnectRes=1, DisconnectReq=2, DisconnectRes=3\n");
    acdp_case("ACDP_ConnectRes", 1);
    acdp_case("ACDP_DisconnectReq", 0);
    printf("         Hand either hex to a schema-informed processor and it will name the other message.\n");

    printf("\n  %d result(s) contradicted the report\n", contradicted);
    return contradicted == 0 ? 0 : 1;
}
