# Session flow

7 request frame(s), 7 response frame(s).

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
| 6 | PowerDeliveryReq | PowerDeliveryRes | FAILED_ChargingProfileInvalid |

## Response codes other than OK

- `[6] PowerDeliveryRes` → **FAILED_ChargingProfileInvalid**

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
  -   SessionStopReq   (in the scenario, never on the wire)

### station → EV

      SupportedAppProtocolRes
      SessionSetupRes
      ServiceDiscoveryRes
      PaymentServiceSelectionRes
      AuthorizationRes
      ChargeParameterDiscoveryRes
      PowerDeliveryRes
  -   SessionStopRes   (in the reference, never answered)

Repeat counts (a difference here is usually their compaction, not a defect):

- PowerDeliveryReq: 1× on the wire, 2× in the scenario

Verbs this build has no mapping for, so they are absent from the comparison:

- `charging_status_req`

Add them to `TuxEvseScenario.Vocabulary` once their spelling is confirmed —
guessing it from a pattern is how a comparison starts agreeing with itself.

**2 divergence(s) in the order.** Each one is a question for the write-up: our state machine, their capture, or a real disagreement?
