# Session flow

52 request frame(s), 52 response frame(s).

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
| 6 | ChargeParameterDiscoveryReq | ChargeParameterDiscoveryRes | OK |
| 7 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 8 | ChargingStatusReq | ChargingStatusRes | OK |
| 9 | MeteringReceiptReq | MeteringReceiptRes | OK |
| 10 | ChargingStatusReq | ChargingStatusRes | OK |
| 11 | ChargingStatusReq | ChargingStatusRes | OK |
| 12 | ChargingStatusReq | ChargingStatusRes | OK |
| 13 | ChargingStatusReq | ChargingStatusRes | OK |
| 14 | ChargingStatusReq | ChargingStatusRes | OK |
| 15 | ChargingStatusReq | ChargingStatusRes | OK |
| 16 | ChargingStatusReq | ChargingStatusRes | OK |
| 17 | ChargingStatusReq | ChargingStatusRes | OK |
| 18 | ChargingStatusReq | ChargingStatusRes | OK |
| 19 | ChargingStatusReq | ChargingStatusRes | OK |
| 20 | ChargingStatusReq | ChargingStatusRes | OK |
| 21 | ChargingStatusReq | ChargingStatusRes | OK |
| 22 | ChargingStatusReq | ChargingStatusRes | OK |
| 23 | ChargingStatusReq | ChargingStatusRes | OK |
| 24 | ChargingStatusReq | ChargingStatusRes | OK |
| 25 | ChargingStatusReq | ChargingStatusRes | OK |
| 26 | ChargingStatusReq | ChargingStatusRes | OK |
| 27 | ChargingStatusReq | ChargingStatusRes | OK |
| 28 | ChargingStatusReq | ChargingStatusRes | OK |
| 29 | ChargingStatusReq | ChargingStatusRes | OK |
| 30 | ChargingStatusReq | ChargingStatusRes | OK |
| 31 | ChargingStatusReq | ChargingStatusRes | OK |
| 32 | ChargingStatusReq | ChargingStatusRes | OK |
| 33 | ChargingStatusReq | ChargingStatusRes | OK |
| 34 | ChargingStatusReq | ChargingStatusRes | OK |
| 35 | ChargingStatusReq | ChargingStatusRes | OK |
| 36 | ChargingStatusReq | ChargingStatusRes | OK |
| 37 | ChargingStatusReq | ChargingStatusRes | OK |
| 38 | ChargingStatusReq | ChargingStatusRes | OK |
| 39 | ChargingStatusReq | ChargingStatusRes | OK |
| 40 | ChargingStatusReq | ChargingStatusRes | OK |
| 41 | ChargingStatusReq | ChargingStatusRes | OK |
| 42 | ChargingStatusReq | ChargingStatusRes | OK |
| 43 | ChargingStatusReq | ChargingStatusRes | OK |
| 44 | ChargingStatusReq | ChargingStatusRes | OK |
| 45 | ChargingStatusReq | ChargingStatusRes | OK |
| 46 | ChargingStatusReq | ChargingStatusRes | OK |
| 47 | ChargingStatusReq | ChargingStatusRes | OK |
| 48 | ChargingStatusReq | ChargingStatusRes | OK |
| 49 | ChargingStatusReq | ChargingStatusRes | OK |
| 50 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 51 | SessionStopReq | SessionStopRes | OK |
