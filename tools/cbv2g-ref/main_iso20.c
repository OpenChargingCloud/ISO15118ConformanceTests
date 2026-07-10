/* SPDX-License-Identifier: Apache-2.0 */
/*
 * cbv2g-iso20 — reference EXI encoder for a fixed set of ISO 15118-20 test messages across all
 * three Phase-4 message sets (CommonMessages, DC, AC), built on EVerest's libcbv2g.
 *
 * Development tool only; `dotnet test` never invokes it. Regenerated hex is checked into
 * Vanaheimr.V2G.Exi.Tests/Vectors/Iso15118_20.*.vectors.json.
 *
 * Usage:  cbv2g_iso20 <Set>_<VectorName>   ->  space-separated lowercase hex on stdout.
 *         <Set> is one of: Common, DC, AC.
 */

#include <stdio.h>
#include <string.h>
#include <stdint.h>

#include "cbv2g/common/exi_bitstream.h"
#include "cbv2g/iso_20/iso20_CommonMessages_Datatypes.h"
#include "cbv2g/iso_20/iso20_CommonMessages_Encoder.h"
#include "cbv2g/iso_20/iso20_DC_Datatypes.h"
#include "cbv2g/iso_20/iso20_DC_Encoder.h"
#include "cbv2g/iso_20/iso20_AC_Datatypes.h"
#include "cbv2g/iso_20/iso20_AC_Encoder.h"

#define OUT_BUF_SIZE 4096

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

/* Every vector uses an all-zero 8-byte SessionID, a fixed TimeStamp and no header signature. */
static void set_header(struct iso20_MessageHeaderType* h) {
    memset(h->SessionID.bytes, 0, iso20_sessionIDType_BYTES_SIZE);
    h->SessionID.bytesLen = iso20_sessionIDType_BYTES_SIZE;
    h->TimeStamp = 1700000000ULL;
    h->Signature_isUsed = 0u;
}
static void set_header_dc(struct iso20_dc_MessageHeaderType* h) {
    memset(h->SessionID.bytes, 0, iso20_dc_sessionIDType_BYTES_SIZE);
    h->SessionID.bytesLen = iso20_dc_sessionIDType_BYTES_SIZE;
    h->TimeStamp = 1700000000ULL;
    h->Signature_isUsed = 0u;
}
static void set_header_ac(struct iso20_ac_MessageHeaderType* h) {
    memset(h->SessionID.bytes, 0, iso20_ac_sessionIDType_BYTES_SIZE);
    h->SessionID.bytesLen = iso20_ac_sessionIDType_BYTES_SIZE;
    h->TimeStamp = 1700000000ULL;
    h->Signature_isUsed = 0u;
}

/* ---- CommonMessages ---------------------------------------------------------------------- */

static int do_common(const char* v) {
    struct iso20_exiDocument doc;
    memset(&doc, 0, sizeof(doc));

    if (strcmp(v, "SessionSetupReq") == 0) {
        doc.SessionSetupReq_isUsed = 1u;
        set_header(&doc.SessionSetupReq.Header);
        set_str(doc.SessionSetupReq.EVCCID.characters, &doc.SessionSetupReq.EVCCID.charactersLen, "EVCCID1234567");

    } else if (strcmp(v, "SessionSetupRes") == 0) {
        doc.SessionSetupRes_isUsed = 1u;
        set_header(&doc.SessionSetupRes.Header);
        doc.SessionSetupRes.ResponseCode = iso20_responseCodeType_OK;
        set_str(doc.SessionSetupRes.EVSEID.characters, &doc.SessionSetupRes.EVSEID.charactersLen, "EVSEID1234567");

    } else if (strcmp(v, "AuthorizationSetupRes") == 0) {
        doc.AuthorizationSetupRes_isUsed = 1u;
        struct iso20_AuthorizationSetupResType* r = &doc.AuthorizationSetupRes;
        set_header(&r->Header);
        r->ResponseCode = iso20_responseCodeType_OK;
        r->AuthorizationServices.arrayLen = 2;
        r->AuthorizationServices.array[0] = iso20_authorizationType_EIM;
        r->AuthorizationServices.array[1] = iso20_authorizationType_PnC;
        r->CertificateInstallationService = 1; /* true */
        r->EIM_ASResAuthorizationMode_isUsed = 1u;
        r->PnC_ASResAuthorizationMode_isUsed = 0u;

    } else {
        fprintf(stderr, "cbv2g-iso20: unknown CommonMessages vector '%s'\n", v);
        return 1;
    }

    uint8_t out[OUT_BUF_SIZE];
    exi_bitstream_t stream;
    exi_bitstream_init(&stream, out, sizeof(out), 0, NULL);
    int error = encode_iso20_exiDocument(&stream, &doc);
    if (error != 0) {
        fprintf(stderr, "cbv2g-iso20: CommonMessages encode failed with error %d\n", error);
        return 3;
    }
    print_hex(out, exi_bitstream_get_length(&stream));
    return 0;
}

/* ---- DC ----------------------------------------------------------------------------------- */

static int do_dc(const char* v) {
    struct iso20_dc_exiDocument doc;
    memset(&doc, 0, sizeof(doc));

    if (strcmp(v, "DC_CableCheckReq") == 0) {
        doc.DC_CableCheckReq_isUsed = 1u;
        set_header_dc(&doc.DC_CableCheckReq.Header);

    } else if (strcmp(v, "DC_CableCheckRes") == 0) {
        doc.DC_CableCheckRes_isUsed = 1u;
        set_header_dc(&doc.DC_CableCheckRes.Header);
        doc.DC_CableCheckRes.ResponseCode = iso20_dc_responseCodeType_OK;
        doc.DC_CableCheckRes.EVSEProcessing = iso20_dc_processingType_Finished;

    } else {
        fprintf(stderr, "cbv2g-iso20: unknown DC vector '%s'\n", v);
        return 1;
    }

    uint8_t out[OUT_BUF_SIZE];
    exi_bitstream_t stream;
    exi_bitstream_init(&stream, out, sizeof(out), 0, NULL);
    int error = encode_iso20_dc_exiDocument(&stream, &doc);
    if (error != 0) {
        fprintf(stderr, "cbv2g-iso20: DC encode failed with error %d\n", error);
        return 3;
    }
    print_hex(out, exi_bitstream_get_length(&stream));
    return 0;
}

/* ---- AC ----------------------------------------------------------------------------------- */

static void set_rational(struct iso20_ac_RationalNumberType* r, int8_t exponent, int16_t value) {
    r->Exponent = exponent;
    r->Value = value;
}

static int do_ac(const char* v) {
    struct iso20_ac_exiDocument doc;
    memset(&doc, 0, sizeof(doc));

    if (strcmp(v, "AC_ChargeParameterDiscoveryReq") == 0) {
        doc.AC_ChargeParameterDiscoveryReq_isUsed = 1u;
        struct iso20_ac_AC_ChargeParameterDiscoveryReqType* q = &doc.AC_ChargeParameterDiscoveryReq;
        set_header_ac(&q->Header);
        q->AC_CPDReqEnergyTransferMode_isUsed     = 1u;
        q->BPT_AC_CPDReqEnergyTransferMode_isUsed = 0u;
        struct iso20_ac_AC_CPDReqEnergyTransferModeType* m = &q->AC_CPDReqEnergyTransferMode;
        set_rational(&m->EVMaximumChargePower, 0, 11000);
        m->EVMaximumChargePower_L2_isUsed = 0u;
        m->EVMaximumChargePower_L3_isUsed = 0u;
        set_rational(&m->EVMinimumChargePower, 0, 100);
        m->EVMinimumChargePower_L2_isUsed = 0u;
        m->EVMinimumChargePower_L3_isUsed = 0u;

    } else {
        fprintf(stderr, "cbv2g-iso20: unknown AC vector '%s'\n", v);
        return 1;
    }

    uint8_t out[OUT_BUF_SIZE];
    exi_bitstream_t stream;
    exi_bitstream_init(&stream, out, sizeof(out), 0, NULL);
    int error = encode_iso20_ac_exiDocument(&stream, &doc);
    if (error != 0) {
        fprintf(stderr, "cbv2g-iso20: AC encode failed with error %d\n", error);
        return 3;
    }
    print_hex(out, exi_bitstream_get_length(&stream));
    return 0;
}

int main(int argc, char** argv) {
    if (argc != 2) {
        fprintf(stderr, "usage: %s <Set>_<vector>  (Set: Common, DC, AC)\n", argv[0]);
        return 1;
    }
    const char* arg = argv[1];

    if (strncmp(arg, "Common_", 7) == 0) return do_common(arg + 7);
    if (strncmp(arg, "DC_", 3) == 0)     return do_dc(arg);       /* DC vector names already start with DC_ */
    if (strncmp(arg, "AC_", 3) == 0)     return do_ac(arg);       /* AC vector names already start with AC_ */

    fprintf(stderr, "cbv2g-iso20: vector name must be prefixed Common_/DC_/AC_\n");
    return 1;
}
