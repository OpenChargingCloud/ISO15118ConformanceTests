# Session flow

8 request frame(s), 8 response frame(s).

No timings: the recorder keeps two octet streams and no clock, so the order within
each direction is real and the pairing below is by position.

| # | EV → station | station → EV | code |
|---|---|---|---|
| 0 | SupportedAppProtocolReq | SupportedAppProtocolRes | OK_SuccessfulNegotiation |
| 1 | SessionSetupReq | SessionSetupRes | OK_NewSessionEstablished |
| 2 | ServiceDiscoveryReq | ServiceDiscoveryRes | OK |
| 3 | PaymentServiceSelectionReq | PaymentServiceSelectionRes | OK |
| 4 | PaymentDetailsReq | PaymentDetailsRes | OK |
| 5 | AuthorizationReq | AuthorizationRes | OK |
| 6 | AuthorizationReq | AuthorizationRes | OK |
| 7 | AuthorizationReq | AuthorizationRes | FAILED_CertificateRevoked |

## Response codes other than OK

- `[7] AuthorizationRes` → **FAILED_CertificateRevoked**
