/* SPDX-License-Identifier: Apache-2.0 */
/*
 * cbv2g-iso2 — reference EXI encoder for a fixed set of ISO 15118-2 test messages,
 * built on EVerest's libcbv2g. It emits wire-conformant EXI hex (including the 0x80
 * header, excluding the V2GTP header) for named vectors that mirror the C# round-trip
 * fixtures, so the generated codec can be diffed against it.
 *
 * Development tool only; `dotnet test` never runs it. Regenerated hex is checked into
 * Vanaheimr.V2G.Exi.Tests/Vectors/Iso15118_2.vectors.json.
 *
 * Usage:  cbv2g_iso2 <VectorName>   ->  space-separated lowercase hex on stdout.
 */

#include <stdio.h>
#include <string.h>
#include <stdint.h>

#include "cbv2g/common/exi_bitstream.h"
#include "cbv2g/iso_2/iso2_msgDefDatatypes.h"
#include "cbv2g/iso_2/iso2_msgDefEncoder.h"

#define OUT_BUF_SIZE 4096

/* Every vector uses an all-zero 8-byte SessionID and an otherwise empty header. */
static void set_header(struct iso2_MessageHeaderType* h) {
    memset(h->SessionID.bytes, 0, iso2_sessionIDType_BYTES_SIZE);
    h->SessionID.bytesLen  = iso2_sessionIDType_BYTES_SIZE;
    h->Notification_isUsed = 0u;
    h->Signature_isUsed    = 0u;
}

static void set_str(char* dst, uint16_t* len, const char* s) {
    size_t n = strlen(s);
    memcpy(dst, s, n);
    dst[n] = '\0';
    *len = (uint16_t)n;
}

static void print_hex(const uint8_t* data, size_t len) {
    for (size_t i = 0; i < len; i++)
        printf(i == 0 ? "%02x" : " %02x", data[i]);
    printf("\n");
}

int main(int argc, char** argv) {
    if (argc != 2) {
        fprintf(stderr, "usage: %s <vector>\n", argv[0]);
        return 1;
    }
    const char* v = argv[1];

    struct iso2_exiDocument doc;
    init_iso2_exiDocument(&doc);
    set_header(&doc.V2G_Message.Header);
    struct iso2_BodyType* body = &doc.V2G_Message.Body;

    if (strcmp(v, "SessionSetupReq") == 0) {
        body->SessionSetupReq_isUsed = 1u;
        static const uint8_t evcc[6] = { 0xAB, 0xCD, 0xEF, 0x01, 0x02, 0x03 };
        memcpy(body->SessionSetupReq.EVCCID.bytes, evcc, sizeof(evcc));
        body->SessionSetupReq.EVCCID.bytesLen = (uint16_t)sizeof(evcc);

    } else if (strcmp(v, "SessionSetupRes_ts") == 0) {
        body->SessionSetupRes_isUsed = 1u;
        body->SessionSetupRes.ResponseCode = iso2_responseCodeType_OK_NewSessionEstablished;
        set_str(body->SessionSetupRes.EVSEID.characters, &body->SessionSetupRes.EVSEID.charactersLen, "DE*ABC*E12345*1");
        body->SessionSetupRes.EVSETimeStamp        = 1600000000;
        body->SessionSetupRes.EVSETimeStamp_isUsed = 1u;

    } else if (strcmp(v, "SessionSetupRes_nots") == 0) {
        body->SessionSetupRes_isUsed = 1u;
        body->SessionSetupRes.ResponseCode = iso2_responseCodeType_OK;
        set_str(body->SessionSetupRes.EVSEID.characters, &body->SessionSetupRes.EVSEID.charactersLen, "EVSE1");
        body->SessionSetupRes.EVSETimeStamp_isUsed = 0u;

    } else if (strcmp(v, "ServiceDiscoveryReq_absent") == 0) {
        body->ServiceDiscoveryReq_isUsed = 1u;
        body->ServiceDiscoveryReq.ServiceScope_isUsed    = 0u;
        body->ServiceDiscoveryReq.ServiceCategory_isUsed = 0u;

    } else if (strcmp(v, "ServiceDiscoveryReq_present") == 0) {
        body->ServiceDiscoveryReq_isUsed = 1u;
        set_str(body->ServiceDiscoveryReq.ServiceScope.characters, &body->ServiceDiscoveryReq.ServiceScope.charactersLen, "urn:scope:test");
        body->ServiceDiscoveryReq.ServiceScope_isUsed    = 1u;
        body->ServiceDiscoveryReq.ServiceCategory        = iso2_serviceCategoryType_EVCharging;
        body->ServiceDiscoveryReq.ServiceCategory_isUsed = 1u;

    } else if (strcmp(v, "ServiceDiscoveryRes") == 0) {
        body->ServiceDiscoveryRes_isUsed = 1u;
        struct iso2_ServiceDiscoveryResType* r = &body->ServiceDiscoveryRes;
        r->ResponseCode = iso2_responseCodeType_OK;
        r->PaymentOptionList.PaymentOption.arrayLen  = 2;
        r->PaymentOptionList.PaymentOption.array[0]  = iso2_paymentOptionType_Contract;
        r->PaymentOptionList.PaymentOption.array[1]  = iso2_paymentOptionType_ExternalPayment;
        r->ChargeService.ServiceID = 1;
        set_str(r->ChargeService.ServiceName.characters, &r->ChargeService.ServiceName.charactersLen, "AC");
        r->ChargeService.ServiceName_isUsed  = 1u;
        r->ChargeService.ServiceCategory     = iso2_serviceCategoryType_EVCharging;
        r->ChargeService.ServiceScope_isUsed = 0u;
        r->ChargeService.FreeService         = 1; /* true */
        r->ChargeService.SupportedEnergyTransferMode.EnergyTransferMode.arrayLen = 2;
        r->ChargeService.SupportedEnergyTransferMode.EnergyTransferMode.array[0] = iso2_EnergyTransferModeType_AC_single_phase_core;
        r->ChargeService.SupportedEnergyTransferMode.EnergyTransferMode.array[1] = iso2_EnergyTransferModeType_AC_three_phase_core;
        r->ServiceList_isUsed = 0u;

    } else {
        fprintf(stderr, "cbv2g-iso2: unknown vector '%s'\n", v);
        return 1;
    }

    uint8_t out[OUT_BUF_SIZE];
    exi_bitstream_t stream;
    exi_bitstream_init(&stream, out, sizeof(out), 0, NULL);

    int error = encode_iso2_exiDocument(&stream, &doc);
    if (error != 0) {
        fprintf(stderr, "cbv2g-iso2: encode failed with libcbv2g error %d\n", error);
        return 3;
    }

    print_hex(out, exi_bitstream_get_length(&stream));
    return 0;
}
