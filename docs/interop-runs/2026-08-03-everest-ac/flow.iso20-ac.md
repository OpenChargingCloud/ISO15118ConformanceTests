# Session flow

12 request frame(s), 12 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |
| 2 | AuthorizationSetupReq | AuthorizationSetupRes | OK |
| 3 | AuthorizationReq | AuthorizationRes | OK |
| 4 | AuthorizationReq | AuthorizationRes | OK |
| 5 | AuthorizationReq | AuthorizationRes | OK |
| 6 | ServiceDiscoveryReq | ServiceDiscoveryRes | OK |
| 7 | ServiceDetailReq | ServiceDetailRes | OK |
| 8 | ServiceSelectionReq | ServiceSelectionRes | OK |
| 9 | AC_ChargeParameterDiscoveryReq | AC_ChargeParameterDiscoveryRes | OK |
| 10 | ScheduleExchangeReq | ScheduleExchangeRes | OK |
| 11 | PowerDeliveryReq | PowerDeliveryRes | FAILED_ContactorError |

## Response codes other than OK

- `[11] PowerDeliveryRes` → **FAILED_ContactorError**

## Against the declared flow — `iso20-ac-eim (iso15118-20, ac)`

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
      AC_ChargeParameterDiscoveryReq
      ScheduleExchangeReq
      PowerDeliveryReq
  -   AC_ChargeLoopReq   (in the scenario, never on the wire)
  -   PowerDeliveryReq   (in the scenario, never on the wire)
  -   SessionStopReq   (in the scenario, never on the wire)

### station → EV

      SupportedAppProtocolRes
      SessionSetupRes
      AuthorizationSetupRes
      AuthorizationRes
      ServiceDiscoveryRes
      ServiceDetailRes
      ServiceSelectionRes
      AC_ChargeParameterDiscoveryRes
      ScheduleExchangeRes
      PowerDeliveryRes
  -   AC_ChargeLoopRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   SessionStopRes   (in the reference, never answered)

Repeat counts (a difference here is usually their compaction, not a defect):

- AuthorizationReq: 3× on the wire, 1× in the scenario

**6 divergence(s) in the order.** Each one is a question for the write-up: our state machine, their capture, or a real disagreement?
