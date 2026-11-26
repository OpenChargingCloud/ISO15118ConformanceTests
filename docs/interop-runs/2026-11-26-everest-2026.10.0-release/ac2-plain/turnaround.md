246 requests, 246 responses, first request to last: 24.986 s

| request | n | EV turnaround, ms — median (min, max) | our response, ms — median (min, max) |
|---|---:|---|---|
| SupportedAppProtocolReq | 1 | — | 8.8 |
| SessionSetupReq | 1 | 100.9 | 9.5 |
| ServiceDiscoveryReq | 1 | 101.2 | 3.4 |
| PaymentServiceSelectionReq | 1 | 101.1 | 2.1 |
| AuthorizationReq | 1 | 100.9 | 1.4 |
| ChargeParameterDiscoveryReq | 1 | 100.9 | 6.2 |
| PowerDeliveryReq | 2 | 100.9 (min 100.7, max 101.2) | 1.7 (min 0.2, max 3.2) |
| ChargingStatusReq | 237 | 100.9 (min 100.6, max 193.8) | 0.3 (min 0.2, max 2.2) |
| SessionStopReq | 1 | 100.8 | 0.9 |
