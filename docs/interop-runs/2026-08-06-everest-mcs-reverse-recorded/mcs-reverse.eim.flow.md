# Session flow

52 request frame(s), 52 response frame(s).

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
| 13 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 14 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 15 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 16 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 17 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 18 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 19 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 20 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 21 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 22 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 23 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 24 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 25 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 26 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 27 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 28 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 29 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 30 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 31 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 32 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 33 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 34 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 35 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 36 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 37 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 38 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 39 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 40 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 41 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 42 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 43 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 44 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 45 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 46 | DC_WeldingDetectionReq | DC_WeldingDetectionRes | OK |
| 47 | DC_WeldingDetectionReq | DC_WeldingDetectionRes | OK |
| 48 | DC_WeldingDetectionReq | DC_WeldingDetectionRes | OK |
| 49 | DC_WeldingDetectionReq | DC_WeldingDetectionRes | OK |
| 50 | DC_WeldingDetectionReq | DC_WeldingDetectionRes | OK |
| 51 | SessionStopReq | SessionStopRes | OK |
