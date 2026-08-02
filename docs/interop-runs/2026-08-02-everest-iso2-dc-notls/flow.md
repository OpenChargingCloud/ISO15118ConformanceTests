# Session flow

1174 request frame(s), 1174 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

*(abridged: the 1164 identical authorization polls in the middle are collapsed to one line —
the full artifact is reproducible, see notes.md)*

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
| … | *(1164 more AuthorizationReq/Res exchanges, all OK, EVSEProcessing=Ongoing)* | | |
| 1172 | AuthorizationReq | AuthorizationRes | OK |
| 1173 | AuthorizationReq | AuthorizationRes | OK |

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
      ServiceDiscoveryRes
      PaymentServiceSelectionRes
      AuthorizationRes
  -   ChargeParameterDiscoveryRes   (in the reference, never answered)
  -   CableCheckRes   (in the reference, never answered)
  -   PreChargeRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   CurrentDemandRes   (in the reference, never answered)
  -   PowerDeliveryRes   (in the reference, never answered)
  -   WeldingDetectionRes   (in the reference, never answered)
  -   SessionStopRes   (in the reference, never answered)

Repeat counts (a difference here is usually their compaction, not a defect):

- AuthorizationReq: 1170× on the wire, 1× in the scenario

**16 divergence(s) in the order.** Each one is a question for the write-up: our state machine, their capture, or a real disagreement?
