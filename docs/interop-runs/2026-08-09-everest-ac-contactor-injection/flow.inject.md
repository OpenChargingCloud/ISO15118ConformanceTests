# Session flow

16 request frame(s), 16 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |
| 2 | AuthorizationSetupReq | AuthorizationSetupRes | OK |
| 3 | AuthorizationReq | AuthorizationRes | OK |
| 4 | AuthorizationReq | AuthorizationRes | OK |
| 5 | ServiceDiscoveryReq | ServiceDiscoveryRes | OK |
| 6 | ServiceDetailReq | ServiceDetailRes | OK |
| 7 | ServiceSelectionReq | ServiceSelectionRes | OK |
| 8 | AC_ChargeParameterDiscoveryReq | AC_ChargeParameterDiscoveryRes | OK |
| 9 | ScheduleExchangeReq | ScheduleExchangeRes | OK |
| 10 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 11 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 12 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 13 | AC_ChargeLoopReq | AC_ChargeLoopRes | OK |
| 14 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 15 | SessionStopReq | SessionStopRes | OK |
