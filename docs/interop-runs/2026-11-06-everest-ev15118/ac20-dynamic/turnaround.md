929 requests, 929 responses, first request to last: 24.345 s

| request | n | EV turnaround, ms — median (min, max) | our response, ms — median (min, max) |
|---|---:|---|---|
| SupportedAppProtocolReq | 1 | — | 9.0 |
| SessionSetupReq | 1 | 25.7 | 10.5 |
| AuthorizationSetupReq | 1 | 26.3 | 2.3 |
| AuthorizationReq | 1 | 25.9 | 1.5 |
| ServiceDiscoveryReq | 1 | 25.8 | 5.0 |
| ServiceDetailReq | 1 | 25.9 | 4.3 |
| ServiceSelectionReq | 1 | 26.1 | 2.9 |
| AC_ChargeParameterDiscoveryReq | 1 | 26.0 | 4.4 |
| ScheduleExchangeReq | 1 | 26.2 | 4.9 |
| PowerDeliveryReq | 2 | 26.3 (min 25.8, max 26.9) | 2.3 (min 0.6, max 3.9) |
| AC_ChargeLoopReq | 917 | 25.8 (min 25.4, max 32.6) | 0.3 (min 0.2, max 9.9) |
| SessionStopReq | 1 | 25.9 | 1.8 |
