# Session flow

68 request frame(s), 68 response frame(s).

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
| 10 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 11 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 12 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 13 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 14 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 15 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 16 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 17 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 18 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 19 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 20 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 21 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 22 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 23 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 24 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 25 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 26 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 27 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 28 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 29 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 30 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 31 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 32 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 33 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 34 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 35 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 36 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 37 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 38 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 39 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 40 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 41 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 42 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 43 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 44 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 45 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 46 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 47 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 48 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 49 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 50 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 51 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 52 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 53 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 54 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 55 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 56 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 57 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 58 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 59 | DC_CableCheckReq | DC_CableCheckRes | OK |
| 60 | DC_PreChargeReq | DC_PreChargeRes | OK |
| 61 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 62 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 63 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 64 | DC_ChargeLoopReq | DC_ChargeLoopRes | OK |
| 65 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 66 | DC_WeldingDetectionReq | DC_WeldingDetectionRes | OK |
| 67 | SessionStopReq | SessionStopRes | OK |
