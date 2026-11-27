# Session flow

10 request frame(s), 10 response frame(s).

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
| 9 | PowerDeliveryReq | PowerDeliveryRes | FAILED_ContactorError |

## Response codes other than OK

- `[9] PowerDeliveryRes` → **FAILED_ContactorError**
