# Session flow

2 request frame(s), 2 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |

## Against the declared flow — `audi-dc-iso2:1`

Reference: a tux-evse scenario — a real session, captured and replayed.

Consecutive repeats are collapsed on both sides: a session polls, and a compacted
scenario names each request once, so the counts are compared separately from the order.

### EV → station

      SupportedAppProtocolReq
      SessionSetupReq
  -   ServiceDiscoveryReq   (in the scenario, never on the wire)
  -   PaymentServiceSelectionReq   (in the scenario, never on the wire)
  -   AuthorizationReq   (in the scenario, never on the wire)
  -   ChargeParameterDiscoveryReq   (in the scenario, never on the wire)
  -   CableCheckReq   (in the scenario, never on the wire)
  -   PreChargeReq   (in the scenario, never on the wire)
  -   PowerDeliveryReq   (in the scenario, never on the wire)
  -   CurrentDemandReq   (in the scenario, never on the wire)
  -   PowerDeliveryReq   (in the scenario, never on the wire)
  -   WeldingDetectionReq   (in the scenario, never on the wire)
  -   SessionStopReq   (in the scenario, never on the wire)

### station → EV

      SupportedAppProtocolRes
      SessionSetupRes
  -   ServiceDiscoveryRes   (in the reference, never answered)
  -   PaymentServiceSelectionRes   (in the reference, never answered)
  -   AuthorizationRes   (in the reference, never answered)
  -   ChargeParameterDiscoveryRes   (in the reference, never answered)
  -   CableCheckRes   (in the reference, never answered)
  -   PreChargeRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   CurrentDemandRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   WeldingDetectionRes   (in the reference, never answered)
  -   SessionStopRes   (in the reference, never answered)

**22 divergence(s) in the order.** Each one is a question for the write-up: our state machine, their capture, or a real disagreement?
