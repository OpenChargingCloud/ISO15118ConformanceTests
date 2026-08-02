# Session flow

36 request frame(s), 36 response frame(s).

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
| 28 | PreChargeReq | PreChargeRes | OK |
| 29 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 30 | CurrentDemandReq | CurrentDemandRes | OK |
| 31 | CurrentDemandReq | CurrentDemandRes | OK |
| 32 | CurrentDemandReq | CurrentDemandRes | OK |
| 33 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 34 | WeldingDetectionReq | WeldingDetectionRes | OK |
| 35 | SessionStopReq | SessionStopRes | OK |

## Against the declared flow — `iso2-dc-eim (iso15118-2, dc)`

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

Repeat counts (a difference here is usually their compaction, not a defect):

- AuthorizationReq: 2× on the wire, 1× in the scenario
- CableCheckReq: 21× on the wire, 1× in the scenario

**The order matches the declared flow exactly.**
