246 requests, 246 responses, first request to last: 24.855 s

| request | n | EV turnaround, ms — median (min, max) | our response, ms — median (min, max) |
|---|---:|---|---|
| SupportedAppProtocolReq | 1 | — | 8.3 |
| SessionSetupReq | 1 | 101.0 | 10.9 |
| ServiceDiscoveryReq | 1 | 100.9 | 3.1 |
| PaymentServiceSelectionReq | 1 | 100.8 | 1.6 |
| AuthorizationReq | 1 | 100.6 | 1.4 |
| ChargeParameterDiscoveryReq | 1 | 100.8 | 4.5 |
| PowerDeliveryReq | 2 | 100.7 (min 100.7, max 100.8) | 1.5 (min 0.2, max 2.9) |
| ChargingStatusReq | 237 | 100.7 (min 100.5, max 145.8) | 0.2 (min 0.2, max 1.9) |
| SessionStopReq | 1 | 100.6 | 1.1 |
