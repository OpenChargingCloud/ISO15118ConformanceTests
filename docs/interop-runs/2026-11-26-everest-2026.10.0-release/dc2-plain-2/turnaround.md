189 requests, 189 responses, first request to last: 19.427 s

| request | n | EV turnaround, ms — median (min, max) | our response, ms — median (min, max) |
|---|---:|---|---|
| SupportedAppProtocolReq | 1 | — | 8.7 |
| SessionSetupReq | 1 | 100.8 | 8.9 |
| ServiceDiscoveryReq | 1 | 100.9 | 2.7 |
| PaymentServiceSelectionReq | 1 | 100.7 | 1.8 |
| AuthorizationReq | 1 | 100.6 | 1.3 |
| ChargeParameterDiscoveryReq | 1 | 100.7 | 6.2 |
| CableCheckReq | 1 | 377.2 | 1.1 |
| PreChargeReq | 1 | 100.7 | 0.7 |
| PowerDeliveryReq | 2 | 100.9 (min 100.6, max 101.1) | 1.8 (min 0.3, max 3.4) |
| CurrentDemandReq | 177 | 101.0 (min 100.6, max 166.8) | 0.4 (min 0.2, max 3.4) |
| WeldingDetectionReq | 1 | 101.0 | 1.0 |
| SessionStopReq | 1 | 100.7 | 1.0 |
