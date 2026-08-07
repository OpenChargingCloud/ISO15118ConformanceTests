# Session flow

6 request frame(s), 6 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |
| 2 | ServiceDiscoveryReq | ServiceDiscoveryRes | OK |
| 3 | PaymentServiceSelectionReq | PaymentServiceSelectionRes | OK |
| 4 | AuthorizationReq | AuthorizationRes | OK |
| 5 | AuthorizationReq | AuthorizationRes | FAILED_SequenceError |

## Response codes other than OK

- `[5] AuthorizationRes` → **FAILED_SequenceError**

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
  -   ChargeParameterDiscoveryReq   (in the scenario, never on the wire)
  -   PowerDeliveryReq   (in the scenario, never on the wire)
  -   ChargingStatusReq   (in the scenario, never on the wire)
  -   PowerDeliveryReq   (in the scenario, never on the wire)
  -   SessionStopReq   (in the scenario, never on the wire)

### station → EV

      SupportedAppProtocolRes
      SessionSetupRes
      ServiceDiscoveryRes
      PaymentServiceSelectionRes
      AuthorizationRes
  -   ChargeParameterDiscoveryRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   ChargingStatusRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   SessionStopRes   (in the reference, never answered)

**10 divergence(s) in the order.** Each one is a question for the write-up: our state machine, their capture, or a real disagreement?
