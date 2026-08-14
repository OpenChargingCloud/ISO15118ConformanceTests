# Session flow

13 request frame(s), 13 response frame(s).

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
| 7 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 8 | ChargingStatusReq | ChargingStatusRes | OK |
| 9 | ChargingStatusReq | ChargingStatusRes | OK |
| 10 | ChargingStatusReq | ChargingStatusRes | OK |
| 11 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 12 | SessionStopReq | SessionStopRes | OK |
