# Session flow

4 request frame(s), 4 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |
| 2 | AuthorizationSetupReq | AuthorizationSetupRes | OK |
| 3 | SessionStopReq | SessionStopRes | OK |

## Against the declared flow — `iso20-dc-eim (iso15118-20, dc)`

Reference: our own recorded session — the route this stack takes, not a conformance claim.

Consecutive repeats are collapsed on both sides: a session polls, and a compacted
scenario names each request once, so the counts are compared separately from the order.

### EV → station

      SupportedAppProtocolReq
      SessionSetupReq
      AuthorizationSetupReq
  -   AuthorizationReq   (in the scenario, never on the wire)
  -   ServiceDiscoveryReq   (in the scenario, never on the wire)
  -   ServiceDetailReq   (in the scenario, never on the wire)
  -   ServiceSelectionReq   (in the scenario, never on the wire)
  -   DC_ChargeParameterDiscoveryReq   (in the scenario, never on the wire)
  -   ScheduleExchangeReq   (in the scenario, never on the wire)
  -   DC_CableCheckReq   (in the scenario, never on the wire)
  -   DC_PreChargeReq   (in the scenario, never on the wire)
  -   PowerDeliveryReq   (in the scenario, never on the wire)
  -   DC_ChargeLoopReq   (in the scenario, never on the wire)
  -   PowerDeliveryReq   (in the scenario, never on the wire)
  -   DC_WeldingDetectionReq   (in the scenario, never on the wire)
      SessionStopReq

### station → EV

      SupportedAppProtocolRes
      SessionSetupRes
      AuthorizationSetupRes
  -   AuthorizationRes   (in the reference, never answered)
  -   ServiceDiscoveryRes   (in the reference, never answered)
  -   ServiceDetailRes   (in the reference, never answered)
  -   ServiceSelectionRes   (in the reference, never answered)
  -   DC_ChargeParameterDiscoveryRes   (in the reference, never answered)
  -   ScheduleExchangeRes   (in the reference, never answered)
  -   DC_CableCheckRes   (in the reference, never answered)
  -   DC_PreChargeRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   DC_ChargeLoopRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   DC_WeldingDetectionRes   (in the reference, never answered)
      SessionStopRes

**24 divergence(s) in the order.** Each one is a question for the write-up: our state machine, their capture, or a real disagreement?
