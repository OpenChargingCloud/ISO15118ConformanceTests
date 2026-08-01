# Session flow

13 request frame(s), 12 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |
| 2 | AuthorizationSetupReq | AuthorizationSetupRes | OK |
| 3 | AuthorizationReq | AuthorizationRes | OK |
| 4 | ServiceDiscoveryReq | ServiceDiscoveryRes | OK |
| 5 | ServiceDetailReq | ServiceDetailRes | OK |
| 6 | ServiceSelectionReq | ServiceSelectionRes | OK |
| 7 | DC_ChargeParameterDiscoveryReq | DC_ChargeParameterDiscoveryRes | OK |
| 8 | ScheduleExchangeReq | ScheduleExchangeRes | OK |
| 9 | DC_CableCheckReq | DC_CableCheckRes | FAILED |
| 10 | DC_PreChargeReq | DC_PreChargeRes | OK |
| 11 | PowerDeliveryReq | PowerDeliveryRes | OK |
| 12 | DC_ChargeLoopReq | — (no answer) |  |

## Response codes other than OK

- `[9] DC_CableCheckRes` → **FAILED**

## Against the declared flow — `iso20-dc-eim (iso15118-20, dc)`

Reference: our own recorded session — the route this stack takes, not a conformance claim.

Consecutive repeats are collapsed on both sides: a session polls, and a compacted
scenario names each request once, so the counts are compared separately from the order.

### EV → station

      SupportedAppProtocolReq
      SessionSetupReq
      AuthorizationSetupReq
      AuthorizationReq
      ServiceDiscoveryReq
      ServiceDetailReq
      ServiceSelectionReq
      DC_ChargeParameterDiscoveryReq
      ScheduleExchangeReq
      DC_CableCheckReq
      DC_PreChargeReq
      PowerDeliveryReq
      DC_ChargeLoopReq
  -   PowerDeliveryReq   (in the scenario, never on the wire)
  -   DC_WeldingDetectionReq   (in the scenario, never on the wire)
  -   SessionStopReq   (in the scenario, never on the wire)

### station → EV

      SupportedAppProtocolRes
      SessionSetupRes
      AuthorizationSetupRes
      AuthorizationRes
      ServiceDiscoveryRes
      ServiceDetailRes
      ServiceSelectionRes
      DC_ChargeParameterDiscoveryRes
      ScheduleExchangeRes
      DC_CableCheckRes
      DC_PreChargeRes
      PowerDeliveryRes
  -   DC_ChargeLoopRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   DC_WeldingDetectionRes   (in the reference, never answered)
  -   SessionStopRes   (in the reference, never answered)

Repeat counts (a difference here is usually their compaction, not a defect):

- DC_ChargeLoopReq: 1× on the wire, 3× in the scenario

**7 divergence(s) in the order.** Each one is a question for the write-up: our state machine, their capture, or a real disagreement?
