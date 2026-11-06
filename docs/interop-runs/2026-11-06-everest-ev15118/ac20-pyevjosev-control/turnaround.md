56 requests, 56 responses, first request to last: 30.851 s

| request | n | EV turnaround, ms — median (min, max) | our response, ms — median (min, max) |
|---|---:|---|---|
| SupportedAppProtocolReq | 1 | — | 12.7 |
| SessionSetupReq | 1 | 811.2 | 21.3 |
| AuthorizationSetupReq | 1 | 758.7 | 3.9 |
| AuthorizationReq | 1 | 808.4 | 29.3 |
| ServiceDiscoveryReq | 1 | 591.4 | 3.7 |
| ServiceDetailReq | 1 | 586.8 | 4.1 |
| ServiceSelectionReq | 1 | 581.4 | 2.8 |
| AC_ChargeParameterDiscoveryReq | 1 | 597.2 | 4.2 |
| ScheduleExchangeReq | 1 | 568.6 | 6.5 |
| PowerDeliveryReq | 3 | 575.5 (min 549.4, max 578.2) | 1.6 (min 0.8, max 1.9) |
| AC_ChargeLoopReq | 43 | 535.0 (min 526.5, max 578.2) | 0.4 (min 0.2, max 8.8) |
| SessionStopReq | 1 | 552.6 | 1.1 |
