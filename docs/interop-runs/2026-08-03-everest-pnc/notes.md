# 2026-08-03 — Plug & Charge against EVerest

**Our signed ISO 15118-2 AuthorizationReq verified at their station — the second independent stack
ever to check one of our contract signatures. ISO 15118-20 Plug & Charge cannot be run against them
at all: it is commented out in their module.**

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2025.10.0** — `EvseV2G` (-2) and `Evse15118D20` (-20) |
| Image | `ghcr.io/everest/everest-demo/manager:2025.10.0-patches` |
| Ours | `Vanaheimr.V2G.Exi` @ `e9f47c3` + the contract-credential harness knob |
| Credential | **theirs**: `tests/ocpp_tests/test_sets/everest-aux/certs/client/mo/MO_CERT_CHAIN.p12`, password `123456` |
| Session | -2 DC over **TLS 1.2**, EIM fallback measured over plain TCP; -20 DC plain TCP |
| Outcome | **-2: chain accepted, signature verified, then their SIL has nobody to authorize an eMAID.** **-20: PnC not implemented.** |
| Artifacts | [`flow.iso2-pnc.md`](flow.iso2-pnc.md), [`frames.iso2-pnc.log`](frames.iso2-pnc.log), [`require-auth-pnc.log`](require-auth-pnc.log), [`their-pnc-code.txt`](their-pnc-code.txt), both of their logs |

Until today, "we sign → a foreign station verifies" had exactly one witness: Josev. This adds a second
one for -2, and settles -20 in the other direction.

## No keys were generated

A station verifies a contract signature against a chain it trusts, so a self-made credential would be
refused for a reason that says nothing about our signing. EVerest ships a complete MO hierarchy in its
own throwaway test PKI, including a ready `MO_CERT_CHAIN.p12` — contract leaf plus `MO_SUB_CA2` and
`MO_SUB_CA1` — whose root their `EvseSecurity` already trusts. The run hands their own material back
to them; nothing was minted here.

Two small mercies in that file: the leaf's Common Name is `UKSWI123456789A`, **15 characters**, so it
passes our own eMAID length check ([the one added on 2026-07-31](../../../EVSimulatorApp/docs/CONCEPT.md) after a
19-character CN travelled in this repository's corpus), and the password is in the tin next to it.

## ISO 15118-2 — and the first thing they taught us

The first attempt ran over plain TCP and quietly came out as EIM. Their reason is in their log:

```
PnC is not allowed without TLS-communication. Correcting value to '1' (ExternalPayment)
```

`iso_server.cpp` strips `Contract` from `PaymentOptionList` whenever the connection is not TLS. That is
the standard's rule, and it is a **station-side check this project has never had to satisfy before** —
every earlier PnC run, against Josev, was already over TLS for other reasons, so the requirement had
never been the thing that decided anything.

Over TLS 1.2, with their V2G root and the two CPO sub-CAs as the trust bundle (their SECC sends only
its leaf — the same finding as [the -20 TLS 1.3 run](../2026-08-03-everest-iso20-dc-tls13/notes.md)):

```
| 3 | PaymentServiceSelectionReq | PaymentServiceSelectionRes | OK |
| 4 | PaymentDetailsReq          | PaymentDetailsRes          | OK |
| 5 | AuthorizationReq           | AuthorizationRes           | OK |
```

`PaymentDetailsRes = OK` is them accepting the contract chain. Then the signed `AuthorizationReq`, and
this is the line that matters — from their MQTT interface, published by `EvseV2G` itself:

```
require_auth_pnc: {"data":{"authorization_type":"PlugAndCharge",
                           "certificate":"-----BEGIN CERTIFICATE-----\nMIICZjCCAg2gAwIBAgICMEQ…
```

**That publish is downstream of the signature check.** Their handler ([`their-pnc-code.txt`](their-pnc-code.txt))
runs GenChallenge echo → signature present → `check_iso2_signature(...)` → *then*
`publish_require_auth_pnc(...)`; every failure before it takes a `goto error_out` with its own response
code. So the message existing at all is a positive statement that our signature verified — and the
codes corroborate by elimination: a bad challenge is `FAILED_ChallengeInvalid` [V2G2-475], a bad
signature is `FAILED_SignatureError`, and we got neither.

### Where it stops, and why that is theirs

After publishing, the station polls `Ongoing` until `auth_timeout_pnc` and then answers plain `FAILED`
— the one branch that produces a bare `FAILED`. Their `auth` module's verdict on the eMAID token:

```
Result for token: [redacted] hash: 772A94DA47EE0039: NO_CONNECTOR_AVAILABLE
```

Two causes, and the second survived fixing the first. Their SIL plug-in fires `DummyTokenProvider`,
which authorizes connector 1 by **RFID** before our car ever connects — the same plug-in the DC cable
check needs. Swapping in `DummyTokenProviderManual` (their own module, `requires: {}`) removes that
collision and the connector starts free; the PnC token is still refused. In a real deployment an OCPP
backend authorizes the eMAID, and the SIL has no stand-in for one. [`pnc-authorize.sh`](../../../tools/interop-everest/pnc-authorize.sh)
forwards their own token back to their auth module and gets no further; it is checked in because it
documents the topic shape, not because it works.

Note the topic shape while you are there: 2025.10 publishes on
`everest/modules/<module>/impl/<impl>/var`, where the 2023 demo image used `everest/<module>/<impl>/var`.
`mqtt-authorize.sh` still carries the old form — which is why it appeared to do nothing against this
image, and why `sil-car.sh`'s plug-in flow was doing the authorizing all along.

## ISO 15118-20 — not implemented, in their own comment

The -20 run never got a signature onto the wire: our `AuthorizationReq` went out **26 bytes and
unsigned**, because `AuthorizationSetupRes` offered EIM only, so our EVCC correctly declined to sign.
Their module explains itself:

```cpp
for (auto& option : payment_options) {
    if (option == types::iso15118::PaymentOption::ExternalPayment) {
        auth_services.push_back(dt::Authorization::EIM);
    } else if (option == types::iso15118::PaymentOption::Contract) {
        // auth_services.push_back(iso15118::message_20::Authorization::PnC);
        EVLOG_warning << "Currently Plug&Charge is not supported and ignored";
    }
}
```

The line is commented out. **ISO 15118-20 Plug & Charge is not available in `Evse15118D20` at
2025.10**, with or without TLS, with or without configuration — so it moves off this project's to-do
list and onto theirs. Josev remains the only -20 PnC counterparty in either direction.

The session then hung on `Ongoing` until **our own 60-second deadline** ended it — the guard EVerest
itself forced us to add on 2026-08-02, doing exactly its job against the same counterparty that
motivated it.

## What this run adds

| | before | now |
|---|---|---|
| -2 contract chain accepted by a foreign station | Josev | Josev + EVerest |
| -2 signature verified by a foreign station | Josev | Josev + EVerest |
| -2 PnC refused without TLS | never exercised | measured, and correct |
| -20 PnC at EVerest | assumed possible, on the to-do list | **not implemented by them** |

The honest residue: **no complete PnC charge**, in either protocol, against this counterparty. The
signature is the part that is ours, and it verified; the authorization backend is the part that is
theirs, and the SIL does not have one.

## Reproduce

Their DC config with the device lines changed and `DummyTokenProvider` swapped for
`DummyTokenProviderManual`, plus their certs copied into `/ext/dist/etc/everest/certs/`, exactly as in
[the -20 DC run](../2026-08-03-everest-iso20-dc-full-charge/notes.md).

```bash
docker cp everest:/ext/source/tests/ocpp_tests/test_sets/everest-aux/certs/client/mo/MO_CERT_CHAIN.p12 .
docker exec everest sh -c "cat …/ca/v2g/V2G_ROOT_CA.pem …/ca/cso/CPO_SUB_CA1.pem …/ca/cso/CPO_SUB_CA2.pem" > v2g-trust.pem

V2G_INTEROP_SECC=127.0.0.1:15163 V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc \
  V2G_INTEROP_TLS=1 V2G_INTEROP_TLS_TRUST=v2g-trust.pem \
  V2G_INTEROP_CONTRACT_CERT=MO_CERT_CHAIN.p12 V2G_INTEROP_CONTRACT_PASS=123456 dotnet test …
```

The colima port trap turned up twice more, and the documented workaround held both times: leave the
published relay container alone and re-point the forwarder *inside* the target container. Restarting a
relay poisons its port; a fresh port is a coin flip.

## Next

- **-2 PnC with a metering receipt.** Their station never set `ReceiptRequired` here, so the second
  signed message type of a -2 PnC session went unexercised against them.
- **Ask EVerest what authorizes an eMAID in the SIL** — there may be a module for it we did not find.
- **-20 PnC stays with Josev** until their module implements it; worth re-checking on the next release.
