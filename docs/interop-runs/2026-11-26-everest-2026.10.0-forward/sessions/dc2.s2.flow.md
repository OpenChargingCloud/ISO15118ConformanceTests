# Session flow

43 request frame(s), 43 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |
| 2 | ServiceDiscoveryReq | ServiceDiscoveryRes | OK |
| 3 | PaymentServiceSelectionReq | PaymentServiceSelectionRes | OK |
| 4 | AuthorizationReq | AuthorizationRes | OK |
| 5 | AuthorizationReq | AuthorizationRes | OK |
| 6 | ChargeParameterDiscoveryReq | ChargeParameterDiscoveryRes | OK |
| 7 | CableCheckReq | CableCheckRes | OK |
| 8 | CableCheckReq | CableCheckRes | OK |
| 9 | CableCheckReq | CableCheckRes | OK |
| 10 | CableCheckReq | CableCheckRes | OK |
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
| 35 | PreChargeReq | PreChargeRes | OK |
| 36 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 37 | CurrentDemandReq | CurrentDemandRes | OK |
| 38 | CurrentDemandReq | CurrentDemandRes | OK |
| 39 | CurrentDemandReq | CurrentDemandRes | OK |
| 40 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 41 | WeldingDetectionReq | WeldingDetectionRes | OK |
| 42 | SessionStopReq | SessionStopRes | OK |
