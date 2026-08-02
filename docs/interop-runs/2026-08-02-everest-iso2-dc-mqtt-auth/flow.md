# Session flow

47 request frame(s), 47 response frame(s).

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
| 6 | AuthorizationReq | AuthorizationRes | OK |
| 7 | AuthorizationReq | AuthorizationRes | OK |
| 8 | AuthorizationReq | AuthorizationRes | OK |
| 9 | AuthorizationReq | AuthorizationRes | OK |
| 10 | AuthorizationReq | AuthorizationRes | OK |
| 11 | ChargeParameterDiscoveryReq | ChargeParameterDiscoveryRes | OK |
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
| 28 | CableCheckReq | CableCheckRes | OK |
| 29 | CableCheckReq | CableCheckRes | OK |
| 30 | CableCheckReq | CableCheckRes | OK |
| 31 | CableCheckReq | CableCheckRes | OK |
| 32 | CableCheckReq | CableCheckRes | OK |
| 33 | CableCheckReq | CableCheckRes | OK |
| 34 | CableCheckReq | CableCheckRes | OK |
| 35 | CableCheckReq | CableCheckRes | OK |
| 36 | CableCheckReq | CableCheckRes | OK |
| 37 | CableCheckReq | CableCheckRes | OK |
| 38 | CableCheckReq | CableCheckRes | OK |
| 39 | CableCheckReq | CableCheckRes | OK |
| 40 | CableCheckReq | CableCheckRes | OK |
| 41 | CableCheckReq | CableCheckRes | OK |
| 42 | CableCheckReq | CableCheckRes | OK |
| 43 | CableCheckReq | CableCheckRes | OK |
| 44 | CableCheckReq | CableCheckRes | OK |
| 45 | CableCheckReq | CableCheckRes | OK |
| 46 | CableCheckReq | CableCheckRes | FAILED |

## Response codes other than OK

- `[46] CableCheckRes` → **FAILED**

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
  -   PreChargeReq   (in the scenario, never on the wire)
  -   PowerDeliveryReq   (in the scenario, never on the wire)
  -   CurrentDemandReq   (in the scenario, never on the wire)
  -   PowerDeliveryReq   (in the scenario, never on the wire)
  -   WeldingDetectionReq   (in the scenario, never on the wire)
  -   SessionStopReq   (in the scenario, never on the wire)

### station → EV

      SupportedAppProtocolRes
      SessionSetupRes
      ServiceDiscoveryRes
      PaymentServiceSelectionRes
      AuthorizationRes
      ChargeParameterDiscoveryRes
      CableCheckRes
  -   PreChargeRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   CurrentDemandRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   WeldingDetectionRes   (in the reference, never answered)
  -   SessionStopRes   (in the reference, never answered)

Repeat counts (a difference here is usually their compaction, not a defect):

- AuthorizationReq: 7× on the wire, 1× in the scenario
- CableCheckReq: 35× on the wire, 1× in the scenario

**12 divergence(s) in the order.** Each one is a question for the write-up: our state machine, their capture, or a real disagreement?
