711 requests, 711 responses, first request to last: 18.878 s

| request | n | EV turnaround, ms — median (min, max) | our response, ms — median (min, max) |
|---|---:|---|---|
| SupportedAppProtocolReq | 1 | — | 10.4 |
| SessionSetupReq | 1 | 26.2 | 11.5 |
| AuthorizationSetupReq | 1 | 26.1 | 3.3 |
| AuthorizationReq | 1 | 26.2 | 1.4 |
| ServiceDiscoveryReq | 1 | 25.7 | 3.6 |
| ServiceDetailReq | 1 | 26.0 | 4.7 |
| ServiceSelectionReq | 1 | 26.1 | 2.0 |
| DC_ChargeParameterDiscoveryReq | 1 | 26.0 | 5.2 |
| ScheduleExchangeReq | 1 | 25.9 | 5.3 |
| DC_CableCheckReq | 1 | 325.0 | 1.1 |
| DC_PreChargeReq | 2 | 25.7 (min 25.6, max 25.7) | 0.6 (min 0.4, max 0.8) |
| PowerDeliveryReq | 2 | 25.7 (min 25.7, max 25.8) | 1.5 (min 0.4, max 2.5) |
| DC_ChargeLoopReq | 694 | 25.8 (min 25.6, max 26.8) | 0.2 (min 0.2, max 6.5) |
| DC_WeldingDetectionReq | 2 | 25.6 (min 25.6, max 25.7) | 0.6 (min 0.2, max 0.9) |
| SessionStopReq | 1 | 25.6 | 0.9 |
