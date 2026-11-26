934 requests, 934 responses, first request to last: 24.326 s

| request | n | EV turnaround, ms — median (min, max) | our response, ms — median (min, max) |
|---|---:|---|---|
| SupportedAppProtocolReq | 1 | — | 8.7 |
| SessionSetupReq | 1 | 26.0 | 9.9 |
| AuthorizationSetupReq | 1 | 25.8 | 2.8 |
| AuthorizationReq | 1 | 25.9 | 1.2 |
| ServiceDiscoveryReq | 1 | 25.8 | 3.1 |
| ServiceDetailReq | 1 | 25.6 | 4.1 |
| ServiceSelectionReq | 1 | 25.7 | 2.0 |
| AC_ChargeParameterDiscoveryReq | 1 | 25.8 | 3.5 |
| ScheduleExchangeReq | 1 | 25.9 | 3.1 |
| PowerDeliveryReq | 2 | 25.9 (min 25.8, max 26.0) | 1.5 (min 1.0, max 2.0) |
| AC_ChargeLoopReq | 922 | 25.7 (min 25.5, max 26.7) | 0.2 (min 0.2, max 7.3) |
| SessionStopReq | 1 | 25.9 | 1.6 |
