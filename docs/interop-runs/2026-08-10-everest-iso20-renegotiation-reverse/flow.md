# Session flow

21 request frame(s), 21 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |
| 2 | AuthorizationSetupReq | AuthorizationSetupRes | OK |
| 3 | AuthorizationReq | AuthorizationRes | OK |
| 4 | ServiceDiscoveryReq | ServiceDiscoveryRes | OK |
| 5 | ServiceDetailReq | ServiceDetailRes | OK |
| 6 | ServiceSelectionReq | ServiceSelectionRes | OK |
| 7 | DC_ChargeParameterDiscoveryReq | DC_ChargeParameterDiscoveryRes | OK |
| 8 | ScheduleExchangeReq | ScheduleExchangeRes | OK |
| 9 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 10 | DC_PreChargeReq | DC_PreChargeRes | OK |
| 11 | DC_PreChargeReq | DC_PreChargeRes | OK |
| 12 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 13 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 14 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 15 | DC_WeldingDetectionReq | DC_WeldingDetectionRes | OK |
| 16 | DC_WeldingDetectionReq | DC_WeldingDetectionRes | OK |
| 17 | DC_WeldingDetectionReq | DC_WeldingDetectionRes | OK |
| 18 | DC_WeldingDetectionReq | DC_WeldingDetectionRes | OK |
| 19 | DC_WeldingDetectionReq | DC_WeldingDetectionRes | OK |
| 20 | SessionStopReq | SessionStopRes | OK |
