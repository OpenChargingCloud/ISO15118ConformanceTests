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

/* Shared sub-structs, filled with the same values as the C# fixtures. */
static void set_pv(struct iso2_PhysicalValueType* p, int8_t mult, iso2_unitSymbolType unit, int16_t val) {
    p->Multiplier = mult;
    p->Unit       = unit;
    p->Value      = val;
}
static void set_dc_evstatus(struct iso2_DC_EVStatusType* s) {
    s->EVReady    = 1;
    s->EVErrorCode = iso2_DC_EVErrorCodeType_NO_ERROR;
    s->EVRESSSOC  = 50;
}
static void set_dc_evsestatus(struct iso2_DC_EVSEStatusType* s) {
    s->NotificationMaxDelay      = 0;
    s->EVSENotification          = iso2_EVSENotificationType_None;
    s->EVSEIsolationStatus_isUsed = 0u;
    s->EVSEStatusCode            = iso2_DC_EVSEStatusCodeType_EVSE_Ready;
}
static void set_ac_evsestatus(struct iso2_AC_EVSEStatusType* s) {
    s->NotificationMaxDelay = 0;
    s->EVSENotification     = iso2_EVSENotificationType_None;
    s->RCD                  = 0;
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

    } else if (strcmp(v, "SessionStopReq") == 0) {
        body->SessionStopReq_isUsed = 1u;
        body->SessionStopReq.ChargingSession = iso2_chargingSessionType_Terminate;

    } else if (strcmp(v, "SessionStopRes") == 0) {
        body->SessionStopRes_isUsed = 1u;
        body->SessionStopRes.ResponseCode = iso2_responseCodeType_OK;

    } else if (strcmp(v, "CableCheckReq") == 0) {
        body->CableCheckReq_isUsed = 1u;
        set_dc_evstatus(&body->CableCheckReq.DC_EVStatus);

    } else if (strcmp(v, "CableCheckRes") == 0) {
        body->CableCheckRes_isUsed = 1u;
        body->CableCheckRes.ResponseCode = iso2_responseCodeType_OK;
        set_dc_evsestatus(&body->CableCheckRes.DC_EVSEStatus);
        body->CableCheckRes.EVSEProcessing = iso2_EVSEProcessingType_Ongoing;

    } else if (strcmp(v, "PreChargeReq") == 0) {
        body->PreChargeReq_isUsed = 1u;
        set_dc_evstatus(&body->PreChargeReq.DC_EVStatus);
        set_pv(&body->PreChargeReq.EVTargetVoltage, 0, iso2_unitSymbolType_V, 400);
        set_pv(&body->PreChargeReq.EVTargetCurrent, 0, iso2_unitSymbolType_A, 10);

    } else if (strcmp(v, "PreChargeRes") == 0) {
        body->PreChargeRes_isUsed = 1u;
        body->PreChargeRes.ResponseCode = iso2_responseCodeType_OK;
        set_dc_evsestatus(&body->PreChargeRes.DC_EVSEStatus);
        set_pv(&body->PreChargeRes.EVSEPresentVoltage, 0, iso2_unitSymbolType_V, 395);

    } else if (strcmp(v, "WeldingDetectionReq") == 0) {
        body->WeldingDetectionReq_isUsed = 1u;
        set_dc_evstatus(&body->WeldingDetectionReq.DC_EVStatus);

    } else if (strcmp(v, "WeldingDetectionRes") == 0) {
        body->WeldingDetectionRes_isUsed = 1u;
        body->WeldingDetectionRes.ResponseCode = iso2_responseCodeType_OK;
        set_dc_evsestatus(&body->WeldingDetectionRes.DC_EVSEStatus);
        set_pv(&body->WeldingDetectionRes.EVSEPresentVoltage, 0, iso2_unitSymbolType_V, 400);

    } else if (strcmp(v, "PowerDeliveryReq") == 0) {
        body->PowerDeliveryReq_isUsed = 1u;
        body->PowerDeliveryReq.ChargeProgress    = iso2_chargeProgressType_Start;
        body->PowerDeliveryReq.SAScheduleTupleID = 1;
        body->PowerDeliveryReq.ChargingProfile_isUsed             = 0u;
        body->PowerDeliveryReq.DC_EVPowerDeliveryParameter_isUsed = 0u;
        body->PowerDeliveryReq.EVPowerDeliveryParameter_isUsed    = 0u;

    } else if (strcmp(v, "PowerDeliveryRes") == 0) {
        body->PowerDeliveryRes_isUsed = 1u;
        body->PowerDeliveryRes.ResponseCode = iso2_responseCodeType_OK;
        body->PowerDeliveryRes.AC_EVSEStatus_isUsed = 0u;
        body->PowerDeliveryRes.EVSEStatus_isUsed    = 0u;
        body->PowerDeliveryRes.DC_EVSEStatus_isUsed = 1u;
        set_dc_evsestatus(&body->PowerDeliveryRes.DC_EVSEStatus);

    } else if (strcmp(v, "ChargingStatusRes") == 0) {
        body->ChargingStatusRes_isUsed = 1u;
        struct iso2_ChargingStatusResType* r = &body->ChargingStatusRes;
        r->ResponseCode = iso2_responseCodeType_OK;
        set_str(r->EVSEID.characters, &r->EVSEID.charactersLen, "EVSE1");
        r->SAScheduleTupleID    = 1;
        r->EVSEMaxCurrent_isUsed  = 0u;
        r->MeterInfo_isUsed       = 0u;
        r->ReceiptRequired_isUsed = 0u;
        set_ac_evsestatus(&r->AC_EVSEStatus);

    } else if (strcmp(v, "CurrentDemandReq") == 0) {
        body->CurrentDemandReq_isUsed = 1u;
        struct iso2_CurrentDemandReqType* q = &body->CurrentDemandReq;
        set_dc_evstatus(&q->DC_EVStatus);
        set_pv(&q->EVTargetCurrent, 0, iso2_unitSymbolType_A, 10);
        q->EVMaximumVoltageLimit_isUsed  = 0u;
        q->EVMaximumCurrentLimit_isUsed  = 0u;
        q->EVMaximumPowerLimit_isUsed    = 0u;
        q->BulkChargingComplete_isUsed   = 0u;
        q->ChargingComplete              = 0;
        q->RemainingTimeToFullSoC_isUsed = 0u;
        q->RemainingTimeToBulkSoC_isUsed = 0u;
        set_pv(&q->EVTargetVoltage, 0, iso2_unitSymbolType_V, 400);

    } else if (strcmp(v, "CurrentDemandRes") == 0) {
        body->CurrentDemandRes_isUsed = 1u;
        struct iso2_CurrentDemandResType* r = &body->CurrentDemandRes;
        r->ResponseCode = iso2_responseCodeType_OK;
        set_dc_evsestatus(&r->DC_EVSEStatus);
        set_pv(&r->EVSEPresentVoltage, 0, iso2_unitSymbolType_V, 395);
        set_pv(&r->EVSEPresentCurrent, 0, iso2_unitSymbolType_A, 10);
        r->EVSECurrentLimitAchieved = 0;
        r->EVSEVoltageLimitAchieved = 0;
        r->EVSEPowerLimitAchieved   = 0;
        r->EVSEMaximumVoltageLimit_isUsed = 0u;
        r->EVSEMaximumCurrentLimit_isUsed = 0u;
        r->EVSEMaximumPowerLimit_isUsed   = 0u;
        set_str(r->EVSEID.characters, &r->EVSEID.charactersLen, "EVSE1");
        r->SAScheduleTupleID    = 1;
        r->MeterInfo_isUsed       = 0u;
        r->ReceiptRequired_isUsed = 0u;

    } else if (strcmp(v, "ChargeParameterDiscoveryReq") == 0) {
        body->ChargeParameterDiscoveryReq_isUsed = 1u;
        struct iso2_ChargeParameterDiscoveryReqType* q = &body->ChargeParameterDiscoveryReq;
        q->MaxEntriesSAScheduleTuple_isUsed = 0u;
        q->RequestedEnergyTransferMode      = iso2_EnergyTransferModeType_AC_single_phase_core;
        q->DC_EVChargeParameter_isUsed = 0u;
        q->EVChargeParameter_isUsed    = 0u;
        q->AC_EVChargeParameter_isUsed = 1u;
        q->AC_EVChargeParameter.DepartureTime_isUsed = 0u;
        set_pv(&q->AC_EVChargeParameter.EAmount,      0, iso2_unitSymbolType_Wh, 1000);
        set_pv(&q->AC_EVChargeParameter.EVMaxVoltage, 0, iso2_unitSymbolType_V, 400);
        set_pv(&q->AC_EVChargeParameter.EVMaxCurrent, 0, iso2_unitSymbolType_A, 16);
        set_pv(&q->AC_EVChargeParameter.EVMinCurrent, 0, iso2_unitSymbolType_A, 2);

    } else if (strcmp(v, "ChargeParameterDiscoveryRes") == 0) {
        body->ChargeParameterDiscoveryRes_isUsed = 1u;
        struct iso2_ChargeParameterDiscoveryResType* r = &body->ChargeParameterDiscoveryRes;
        r->ResponseCode   = iso2_responseCodeType_OK;
        r->EVSEProcessing = iso2_EVSEProcessingType_Finished;
        r->SAScheduleList_isUsed = 0u;
        r->SASchedules_isUsed    = 0u;
        r->DC_EVSEChargeParameter_isUsed = 0u;
        r->EVSEChargeParameter_isUsed    = 0u;
        r->AC_EVSEChargeParameter_isUsed = 1u;
        set_ac_evsestatus(&r->AC_EVSEChargeParameter.AC_EVSEStatus);
        set_pv(&r->AC_EVSEChargeParameter.EVSENominalVoltage, 0, iso2_unitSymbolType_V, 230);
        set_pv(&r->AC_EVSEChargeParameter.EVSEMaxCurrent,     0, iso2_unitSymbolType_A, 32);

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
