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

## Against the declared flow — `iso2-ac-eim (iso15118-2, ac)`

Reference: our own recorded session — the route this stack takes, not a conformance claim.

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

Repeat counts (a difference here is usually their compaction, not a defect):

- AuthorizationReq: 2× on the wire, 1× in the scenario

**The order matches the declared flow exactly.**
