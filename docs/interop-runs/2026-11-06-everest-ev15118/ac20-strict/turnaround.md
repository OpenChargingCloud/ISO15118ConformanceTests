929 requests, 929 responses, first request to last: 24.225 s

| request | n | EV turnaround, ms — median (min, max) | our response, ms — median (min, max) |
|---|---:|---|---|
| SupportedAppProtocolReq | 1 | — | 8.1 |
| SessionSetupReq | 1 | 25.6 | 9.1 |
| AuthorizationSetupReq | 1 | 25.8 | 2.4 |
| AuthorizationReq | 1 | 25.8 | 1.4 |
| ServiceDiscoveryReq | 1 | 25.8 | 3.2 |
| ServiceDetailReq | 1 | 25.6 | 3.8 |
| ServiceSelectionReq | 1 | 25.8 | 2.3 |
| AC_ChargeParameterDiscoveryReq | 1 | 25.7 | 3.6 |
| ScheduleExchangeReq | 1 | 26.2 | 4.4 |
| PowerDeliveryReq | 2 | 25.8 (min 25.7, max 25.8) | 1.6 (min 1.2, max 2.1) |
| AC_ChargeLoopReq | 917 | 25.8 (min 25.5, max 27.1) | 0.3 (min 0.2, max 6.5) |
| SessionStopReq | 1 | 26.0 | 1.4 |
