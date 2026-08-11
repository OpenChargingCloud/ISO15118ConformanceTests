# 2026-08-11 — ISO 15118-2 contract provisioning against EVerest's `EvseV2G`

**The first time any counterparty's `-2` provisioning path has been exercised by this project**, and the
reason it is the first is on our side: `Evcc2.CertInstallRequest` landed the same morning
(`WWCP_ISO15118` `c1a7989`) and nothing could reach it from an interop run until this one
(`V2G_INTEROP_PROVISION`, added here).

everest-core **2026.02.1**, `modules/EVSE/EvseV2G/`, DC over **TLS 1.2**, station config
`config-dc2-certinstall-ours.yaml` (the shipped `-2` PnC config with `payment_enable_eim: true` added —
see *What had to be changed and why*).

**Two sessions, and they are a control pair.** Same request, same station, one variable: which payment
option the car selected. That pair is what turns the first session's response code from a puzzle into a
measured property of their state machine.

| | session 1 | session 2 |
|---|---|---|
| payment option selected | **ExternalPayment** (EIM) | **Contract**, with their own MO chain |
| `CertificateInstallationReq` accepted | yes | yes |
| EXI forwarded to their MQTT interface | yes | yes |
| their wait before failing | **4 500 ms** | **4 500 ms** |
| `CertificateInstallationRes` | **`FAILED_SequenceError`** | **`FAILED`** |
| their own log | `error: Sequence Error` | `error: Response FAILED` |

## What is proven

**1. The certificate service is advertised, and this project had never seen it.** Their
`ServiceDiscoveryRes` carries ServiceID **2**, category `ContractCertificate`, name `Certificate`,
`FreeService`. Three conditions gate it, all in
`charger/ISO15118_chargerImpl.cpp:221` — `pnc_enabled and supported_certificate_service and
tls_server_available` — so it needs Contract in the payment options, `EvseManager`'s
`contract_certificate_installation_enabled` (default **true**), and a live TLS server.

**2. Their forwarding half is correct, to the byte.** The request our EVCC sent and the EXI their
station published on `everest/modules/iso15118_charger/impl/extensions/var/iso15118_certificate_request`
are the same 802 bytes:

```
our CertificateInstallationReq payload   802 bytes  sha256 db60e33a859f5fc7826618ba20bd8939db299fd8c2b1f6283de1ecd59d6d4633
their forwarded exi_request (base64)     802 bytes  sha256 db60e33a859f5fc7826618ba20bd8939db299fd8c2b1f6283de1ecd59d6d4633
```

(Session 1. Session 2's is a different session id and therefore different bytes —
`c1dc8b326f2dbc02cf8d22bf13c6ca77ad58e757dd3fa71109371db2fbc29e84`, also 802 — so the match is a
property of the forwarding, not of one frame.) The V2GTP header is stripped and the payload passed
through unaltered, with `certificate_action: "Install"` beside it. **This is a real implementation and
it does its job.**

**3. Without a backend they wait exactly `V2G_SECC_MSG_CERTINSTALL_TIME` and then fail the session.**
4 500 ms, `iso_server.cpp:30`. Their SIL ships no responder for the `iso15118_extensions` interface, so
this is what the shipped configuration does — which is worth stating plainly, because it means the
*backend* half of their implementation is still unmeasured by anyone here.

## The response code, and why the pair was needed

Session 1 answered `FAILED_SequenceError`, which is not what their own code says it intends — the
timeout branch sets `intl_emergency_shutdown` under a comment citing `[V2G2-918]` and *"response code
faild will be set in iso_validate_response_code()"*. Reading that function explains it
(`iso_server.cpp:63-93`): the emergency-shutdown branch assigns `FAILED`, and eleven lines later a state
check overwrites it whenever `iso_validate_state` returns a failure.

**And the state check failed because we had selected EIM.** Their `handle_iso_payment_service_selection`
branches on the payment option: Contract leaves the state
`WAIT_FOR_PAYMENTDETAILS_CERTINST_CERTUPD`, whose `allowed_requests` mask includes
`V2G_CERTIFICATE_INSTALLATION_MSG`; ExternalPayment leaves `WAIT_FOR_AUTHORIZATION`, whose mask does
not (`iso_server.hpp:100 ff.`). Session 2 selected Contract, the state allowed the message, nothing
overwrote the code, and the answer was the plain `FAILED` the timeout path intends.

So the measured property is:

> **Their station accepts an ISO 15118-2 `CertificateInstallationReq` only in a Contract-authorized
> session. In an EIM session the same request is refused as out of sequence.**

**Whether that is correct is an open question and is deliberately not answered here.** A car with no
contract is exactly the car that needs one installed, and it has nothing to authorize with but EIM — so
the restriction reads as odd from the car's side. But the standard's sequence for the certificate
service is not something this run establishes, and guessing at it is how a real finding gets dismissed.
It belongs in [`normative-basis.md`](../../normative-basis.md) once the clause is read. Recorded here as
a question, with the control pair that makes it precise.

## What had to be changed and why

- **`payment_enable_eim: true`** added to their config for session 1. The shipped `-2` PnC config offers
  **Contract only**, so our EIM car was refused at `PaymentServiceSelection` with
  `FAILED_PaymentSelectionInvalid` — correctly, and that was our configuration error, not a finding. The
  first attempt is recorded here because the correction is the interesting part: a station that offers
  no EIM cannot be asked for a contract by a car that has none.
- Their **own** OEM provisioning credential (`certs/client/oem/OEM_LEAF`, `CN=OEMProvCert`, **P-256**,
  password `123456`) and their **own** MO chain for session 2's contract. Nothing was minted for this
  run: `-2` key transport is P-256 only, and their test PKI already ships it.

## Session 3 — the loop closed, by standing in as the backend

Added the same day. Their station publishes and waits; nothing in their SIL answers, so sessions 1 and 2
could only prove the forward half. Session 3 supplies the missing half:
[`tools/interop-everest/mo-backend-bridge.sh`](../../../tools/interop-everest/mo-backend-bridge.sh)
carries MQTT with their own `mosquitto_sub`/`_pub`, and
[`Iso2MoBackend`](../../../ISO15118ConformanceTests.Simulation/Interop/Iso2MoBackend.cs) issues the
contract by driving a real `Secc2` to the provisioning phase and handing it the request their station
forwarded. **Nothing re-implements provisioning**: the contract, the ECDH-wrapped key and the
four-reference signature are the ones a loopback would produce, so what is under test is their
transport with a known-good answer travelling through it.

**It works.** Their station took the response, delivered it, and the session went *on*:

```
[4] CertificateInstallationReq  810 bytes   →   CertificateInstallationRes (OK)  1467 bytes
[5] PaymentDetailsReq          1917 bytes   →   PaymentDetailsRes (OK)
[6] AuthorizationReq …                      →   AuthorizationRes (OK) ×358, then FAILED
```

Our EVCC verified the response signature and unwrapped the contract key — `MO backend: issued
DE-VAN-C00000001-6 to CN=OEMProvCert; the car's own signature verified, answer wrapped for its key:
yes`. The Authorization failure afterwards is the *known* property of their SIL, not this run's
question: it has no contract-validating backend (matrix footnote ³), and it polled `Ongoing` 358 times
before giving up.

### And the return direction is not byte-exact

The forward direction is (802 bytes, hash for hash). The return direction is **our 1458 bytes plus one
trailing `0x00`**:

```
our CertificateInstallationRes   1458 bytes
their V2GTP frame                1467 bytes = 8 header + 1459 payload   (header declares 1459)
first differing byte             none in the first 1458 — the extra byte is appended
```

So the frame declares, and carries, one byte more than the EXI document it contains. **Benign here** —
an EXI decoder stops at the end of the document, ours did, the signature verified and the session
continued — but it is a wire-level deviation, and it means the length in their frame is not the length
of the message. Their handler copies the decoded base64 to `conn->buffer + V2GTP_HEADER_LENGTH` and
sets `byte_pos = data.size()`, then adds the header length; where the extra byte enters is **not
established here**, and reading their writer is the next step before this becomes a filing. Recorded
with the numbers rather than a cause.

### Two mistakes worth keeping

Both produced a *silent* wrong result rather than an error, which is the reason to write them down.

- **The command envelope.** `set_get_certificate_response` needs
  `{"data":{"args":{…},"id":…,"origin":…},"msg_type":"Cmd"}`. The first attempt omitted `origin` and
  `msg_type` — the message is then dropped without a word and the station runs into its 4 500 ms
  timeout, which looks exactly like publishing nothing. The fix was to capture a command their own
  modules publish (`mosquitto_sub -t "everest/modules/+/impl/+/cmd/+" -v`) and copy the shape. Argument
  names came from `types/iso15118.yaml` for the same reason: `exi_response`, not `exiResponse`, and
  `certificate_action` is required.
- **The request's header.** The backend first re-wrapped the forwarded request in a fresh header, which
  drops the car's signature — the issuer then reported *"the car's own signature did NOT verify"* and
  issued the contract anyway. The request now goes in as it arrived. Worth noticing that the wrong
  version still produced a working session: nothing on either side refuses an unsigned provisioning
  request, so the only thing that caught it was reading our own diagnostic line.

## What this does *not* decide

- **The `CertificateUpdate` filing is not settled by this run**, and a source reading found why before
  the run started. Their advertisement carries **parameter-set-ID 1 only** —
  `const int16_t cert_parameter_set_id[] = {1}; // parameter-set-ID 1: "Installation" service. TODO:
  Support of the "Update" service (parameter-set-ID 2)`. So a car selecting set 2 selects a set they
  never offered, and the service-selection gate answers before
  `handle_iso_certificate_update` can be reached. The filing's open question — *which of two outcomes
  does the union-slot response produce* — needs their advertisement changed, or the request injected
  past the gate. Noted on [the filing](../../reports/everest-evsev2g-certificate-update.md).
- ~~**The backend half.**~~ **Done, session 3 above.** What remains unmeasured is narrower: their
  station never validated anything we sent it — it forwards and relays — so this says nothing about a
  *real* CSMS behind that interface.
- **Where the extra return byte comes from.** Measured, not explained; see session 3.
- **Their `[V2G2-918]` handling in general.** One timeout, in one message. The 4 500 ms is theirs and
  measured; whether `[V2G2-918]` prescribes that number is not checked here.

## Reproduce

```
# station
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-dc2-certinstall-ours.yaml
CP_AT_PLUGIN=1 bash ~/everest/sil-car.sh          # wait for "Set PWM On"
socat TCP-LISTEN:15118,bind=0.0.0.0,reuseaddr,fork "TCP6:[<link-local>%eth0]:<tls-port>"
~/everest/dist/bin/mosquitto_sub -t "everest/modules/iso15118_charger/impl/extensions/#" -v
```

```
# car (Windows)
V2G_INTEROP_SECC=127.0.0.1:15118 V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=dc V2G_INTEROP_TLS=1 \
V2G_INTEROP_PROVISION=install V2G_INTEROP_PROVISION_CERT=<oem-prov.p12> V2G_INTEROP_PROVISION_PASS=123456 \
dotnet test -c Release --filter "FullyQualifiedName~EverestInteropTests.OurEvcc_AgainstTheirEvseV2G"
```

Add `V2G_INTEROP_CONTRACT_CERT=<mo-chain.p12>` and `V2G_INTEROP_CONTRACT_PASS=123456` for session 2.

Artifacts here: [`frames-eim.log`](frames-eim.log), [`frames-contract.log`](frames-contract.log) (our
side), [`mqtt.log`](mqtt.log) / [`mqtt2.log`](mqtt2.log) (their forwarded EXI, verbatim),
[`station.log`](station.log) (their own verdict lines).
