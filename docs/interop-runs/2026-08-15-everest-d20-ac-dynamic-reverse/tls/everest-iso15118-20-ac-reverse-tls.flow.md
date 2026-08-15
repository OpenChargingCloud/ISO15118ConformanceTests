# Session flow

56 request frame(s), 56 response frame(s).

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
| 7 | AC_ChargeParameterDiscoveryReq | AC_ChargeParameterDiscoveryRes | OK |
| 8 | ScheduleExchangeReq | ScheduleExchangeRes | OK |
| 9 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 10 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 11 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 12 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 13 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 14 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 15 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 16 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 17 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 18 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 19 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 20 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 21 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 22 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 23 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 24 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 25 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 26 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 27 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 28 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 29 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 30 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 31 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 32 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 33 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 34 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 35 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 36 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 37 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 38 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 39 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 40 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 41 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 42 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 43 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 44 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 45 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 46 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 47 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 48 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 49 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 50 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 51 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 52 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 53 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 54 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 55 | SessionStopReq | SessionStopRes | OK |
