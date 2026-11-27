# Session flow

46 request frame(s), 46 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |
| 2 | ServiceDiscoveryReq | ServiceDiscoveryRes | OK |
| 3 | PaymentServiceSelectionReq | PaymentServiceSelectionRes | OK |
| 4 | PaymentDetailsReq | PaymentDetailsRes | OK |
| 5 | AuthorizationReq | AuthorizationRes | OK |
| 6 | AuthorizationReq | AuthorizationRes | OK |
| 7 | AuthorizationReq | AuthorizationRes | OK |
| 8 | AuthorizationReq | AuthorizationRes | OK |
| 9 | AuthorizationReq | AuthorizationRes | OK |
| 10 | ChargeParameterDiscoveryReq | ChargeParameterDiscoveryRes | OK |
| 11 | CableCheckReq | CableCheckRes | OK |
| 12 | CableCheckReq | CableCheckRes | OK |
| 13 | CableCheckReq | CableCheckRes | OK |
| 14 | CableCheckReq | CableCheckRes | OK |
| 15 | CableCheckReq | CableCheckRes | OK |
| 16 | CableCheckReq | CableCheckRes | OK |
| 17 | CableCheckReq | CableCheckRes | OK |
| 18 | CableCheckReq | CableCheckRes | OK |
| 19 | CableCheckReq | CableCheckRes | OK |
| 20 | CableCheckReq | CableCheckRes | OK |
| 21 | CableCheckReq | CableCheckRes | OK |
| 22 | CableCheckReq | CableCheckRes | OK |
| 23 | CableCheckReq | CableCheckRes | OK |
| 24 | CableCheckReq | CableCheckRes | OK |
| 25 | CableCheckReq | CableCheckRes | OK |
| 26 | CableCheckReq | CableCheckRes | OK |
| 27 | CableCheckReq | CableCheckRes | OK |
| 28 | CableCheckReq | CableCheckRes | OK |
| 29 | CableCheckReq | CableCheckRes | OK |
| 30 | CableCheckReq | CableCheckRes | OK |
| 31 | CableCheckReq | CableCheckRes | OK |
| 32 | CableCheckReq | CableCheckRes | OK |
| 33 | CableCheckReq | CableCheckRes | OK |
| 34 | CableCheckReq | CableCheckRes | OK |
| 35 | CableCheckReq | CableCheckRes | OK |
| 36 | CableCheckReq | CableCheckRes | OK |
| 37 | CableCheckReq | CableCheckRes | OK |
| 38 | PreChargeReq | PreChargeRes | OK |
| 39 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 40 | CurrentDemandReq | CurrentDemandRes | OK |
| 41 | MeteringReceiptReq | MeteringReceiptRes | OK |
| 42 | CurrentDemandReq | CurrentDemandRes | OK |
| 43 | MeteringReceiptReq | MeteringReceiptRes | OK |
| 44 | CurrentDemandReq | CurrentDemandRes | OK |
| 45 | PowerDeliveryReq | PowerDeliveryRes | FAILED_SequenceError |

## Response codes other than OK

- `[45] PowerDeliveryRes` → **FAILED_SequenceError**
