# Session flow

5 request frame(s), 5 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |
| 2 | AuthorizationSetupReq | AuthorizationSetupRes | OK |
| 3 | AuthorizationReq | AuthorizationRes | WARNING_CertificateValidationError |
| 4 | ServiceDiscoveryReq | ServiceDiscoveryRes | FAILED_SequenceError |

## Response codes other than OK

- `[3] AuthorizationRes` → **WARNING_CertificateValidationError**
- `[4] ServiceDiscoveryRes` → **FAILED_SequenceError**
