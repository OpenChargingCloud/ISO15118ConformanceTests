# Session flow

9 request frame(s), 9 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |
| 2 | AuthorizationSetupReq | AuthorizationSetupRes | OK |
| 3 | AuthorizationReq | AuthorizationRes | OK |
| 4 | AuthorizationReq | AuthorizationRes | OK |
| 5 | ServiceDiscoveryReq | ServiceDiscoveryRes | OK |
| 6 | ServiceDetailReq | ServiceDetailRes | OK |
| 7 | ServiceSelectionReq | ServiceSelectionRes | OK |
| 8 | DC_ChargeParameterDiscoveryReq | DC_ChargeParameterDiscoveryRes | FAILED_WrongChargeParameter |

## Response codes other than OK

- `[8] DC_ChargeParameterDiscoveryRes` → **FAILED_WrongChargeParameter**
