893 requests, 893 responses, first request to last: 25.029 s

| request | n | EV turnaround, ms — median (min, max) | our response, ms — median (min, max) |
|---|---:|---|---|
| SupportedAppProtocolReq | 1 | — | 11.4 |
| SessionSetupReq | 1 | 26.2 | 25.1 |
| AuthorizationSetupReq | 1 | 26.3 | 2.5 |
| AuthorizationReq | 1 | 25.9 | 1.1 |
| ServiceDiscoveryReq | 1 | 26.0 | 6.6 |
| ServiceDetailReq | 1 | 26.0 | 4.7 |
| ServiceSelectionReq | 1 | 26.3 | 2.1 |
| AC_ChargeParameterDiscoveryReq | 1 | 26.2 | 7.3 |
| ScheduleExchangeReq | 1 | 26.5 | 6.1 |
| PowerDeliveryReq | 2 | 26.5 (min 26.2, max 26.9) | 2.4 (min 1.9, max 3.0) |
| AC_ChargeLoopReq | 881 | 25.9 (min 25.6, max 76.3) | 0.3 (min 0.1, max 23.2) |
| SessionStopReq | 1 | 25.7 | 1.4 |
