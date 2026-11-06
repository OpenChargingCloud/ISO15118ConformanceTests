709 requests, 709 responses, first request to last: 18.748 s

| request | n | EV turnaround, ms — median (min, max) | our response, ms — median (min, max) |
|---|---:|---|---|
| SupportedAppProtocolReq | 1 | — | 16.4 |
| SessionSetupReq | 1 | 26.4 | 15.6 |
| AuthorizationSetupReq | 1 | 26.1 | 3.0 |
| AuthorizationReq | 1 | 26.2 | 1.9 |
| ServiceDiscoveryReq | 1 | 25.9 | 5.0 |
| ServiceDetailReq | 1 | 25.9 | 5.3 |
| ServiceSelectionReq | 1 | 26.0 | 2.8 |
| DC_ChargeParameterDiscoveryReq | 1 | 26.0 | 5.6 |
| ScheduleExchangeReq | 1 | 26.2 | 5.2 |
| DC_CableCheckReq | 1 | 304.5 | 1.3 |
| DC_PreChargeReq | 2 | 25.8 (min 25.7, max 25.8) | 0.8 (min 0.4, max 1.2) |
| PowerDeliveryReq | 2 | 25.8 (min 25.7, max 25.9) | 1.8 (min 0.4, max 3.1) |
| DC_ChargeLoopReq | 692 | 25.7 (min 25.5, max 26.5) | 0.2 (min 0.2, max 8.0) |
| DC_WeldingDetectionReq | 2 | 25.5 (min 25.5, max 25.5) | 0.5 (min 0.2, max 0.7) |
| SessionStopReq | 1 | 25.6 | 1.0 |
