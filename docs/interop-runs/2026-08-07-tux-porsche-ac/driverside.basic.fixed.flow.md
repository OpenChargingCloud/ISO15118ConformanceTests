# Session flow

10 request frame(s), 10 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |
| 2 | ServiceDiscoveryReq | ServiceDiscoveryRes | OK |
| 3 | PaymentServiceSelectionReq | PaymentServiceSelectionRes | OK |
| 4 | AuthorizationReq | AuthorizationRes | OK |
| 5 | ChargeParameterDiscoveryReq | ChargeParameterDiscoveryRes | OK |
| 6 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 7 | ChargingStatusReq | ChargingStatusRes | OK |
| 8 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 9 | SessionStopReq | SessionStopRes | OK |

## Against the declared flow — `porsche-taycan-4s-driverside-ac-iso2:1`

Reference: a tux-evse scenario — a real session, captured and replayed.

Consecutive repeats are collapsed on both sides: a session polls, and a compacted
scenario names each request once, so the counts are compared separately from the order.

### EV → station

      SupportedAppProtocolReq
      SessionSetupReq
      ServiceDiscoveryReq
      PaymentServiceSelectionReq
      AuthorizationReq
      ChargeParameterDiscoveryReq
      PowerDeliveryReq
      ChargingStatusReq
      PowerDeliveryReq
      SessionStopReq

### station → EV

      SupportedAppProtocolRes
      SessionSetupRes
      ServiceDiscoveryRes
      PaymentServiceSelectionRes
      AuthorizationRes
      ChargeParameterDiscoveryRes
      PowerDeliveryRes
      ChargingStatusRes
      PowerDeliveryRes
      SessionStopRes

**The order matches the declared flow exactly.**
