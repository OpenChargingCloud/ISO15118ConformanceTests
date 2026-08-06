# Session flow

25 request frame(s), 25 response frame(s).

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
| 6 | CableCheckReq | CableCheckRes | OK |
| 7 | PreChargeReq | PreChargeRes | OK |
| 8 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 9 | CurrentDemandReq | CurrentDemandRes | OK |
| 10 | CurrentDemandReq | CurrentDemandRes | OK |
| 11 | CurrentDemandReq | CurrentDemandRes | OK |
| 12 | CurrentDemandReq | CurrentDemandRes | OK |
| 13 | CurrentDemandReq | CurrentDemandRes | OK |
| 14 | CurrentDemandReq | CurrentDemandRes | OK |
| 15 | CurrentDemandReq | CurrentDemandRes | OK |
| 16 | CurrentDemandReq | CurrentDemandRes | OK |
| 17 | CurrentDemandReq | CurrentDemandRes | OK |
| 18 | CurrentDemandReq | CurrentDemandRes | OK |
| 19 | CurrentDemandReq | CurrentDemandRes | OK |
| 20 | CurrentDemandReq | CurrentDemandRes | OK |
| 21 | CurrentDemandReq | CurrentDemandRes | OK |
| 22 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 23 | WeldingDetectionReq | WeldingDetectionRes | OK |
| 24 | SessionStopReq | SessionStopRes | OK |

## Against the declared flow — `audi-dc-iso2:1`

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
      CableCheckReq
      PreChargeReq
      PowerDeliveryReq
      CurrentDemandReq
      PowerDeliveryReq
      WeldingDetectionReq
      SessionStopReq

### station → EV

      SupportedAppProtocolRes
      SessionSetupRes
      ServiceDiscoveryRes
      PaymentServiceSelectionRes
      AuthorizationRes
      ChargeParameterDiscoveryRes
      CableCheckRes
      PreChargeRes
      PowerDeliveryRes
      CurrentDemandRes
      PowerDeliveryRes
      WeldingDetectionRes
      SessionStopRes

**The order matches the declared flow exactly.**
