# 2026-08-15 — ISO 15118-2 in reverse, and a car that changes what it is when the transport does

**The first ISO 15118-2 session this project has run in the reverse direction against EVerest, in any
transport.** It was expected to be the last small item on a list. It turned out that their car does
something over TLS that it does not do over TCP — and that our own station had been checking two things
per session that no run could read.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `PyEvJosev` |
| Config | [`config-ac2-reverse-ours.yaml`](config-ac2-reverse-ours.yaml) — the AC reverse config with `supported_ISO15118_2: true`, the `-20` flags off, and **`enable_tls_1_3: false`** |
| Ours | `EverestInteropTests.TheirPyEvJosev_AgainstOurSecc_RunsToCompletion`, `V2G_INTEROP_PROTOCOL=2` |
| Outcome | **plain: EIM. TLS 1.2: Plug & Charge**, contract verified, chain anchored, one signed metering receipt |

**No PKI regeneration.** `-2` TLS is unilateral, so their car needs no vehicle credential — and
`enable_tls_1_3: false` is what makes that true on their side as well: Josev's `get_ssl_context` loads
the client chain only under `is_tls_1_3_enabled()`. The pristine tree already holds both the V2G root
their EV anchors at and the SECC leaf our station presents, so this run touched nothing and restored
nothing (root `88:F8:C2:D5…` before and after).

## Their car is a different car over TLS

Same config, same rig, one variable — the SDP security byte:

| arm | transport | payment | exchanges | `MeteringReceiptReq` | corpus trace |
|---|---|---|---:|---:|---|
| plain | TCP | **EIM** | 52 | 0 | **yes** |
| tls-roots | **TLS 1.2** | **Contract** | 52 | **1** | no (signed requests) |

Over plain TCP the flow is `ServiceDiscovery → PaymentServiceSelection → Authorization`. Over TLS an
extra exchange appears — `PaymentDetailsReq` — and a `MeteringReceiptReq` arrives inside the charge loop.
That is ISO 15118-2's own rule showing up as behaviour: **Contract requires TLS**, and their EV applies it
without being told which to use. This project has met the rule from the other side before — their
*station* refuses `Contract` on a plaintext connection, which was the first external check of that
requirement against us — and this is the same rule seen from the car.

**The transport arm is the conformant one, and it is also the richer session.** A `-2` reverse cell
filled over plain TCP would have been EIM only.

## What our station checked, and could not say

`Secc2` verifies a Contract session's signed `AuthorizationReq` — GenChallenge echo, reference digest,
ECDSA signature, under whichever `SignedInfo` grammar it parses — and every signed `MeteringReceiptReq`
after it. `InteropSession`'s `-2` branch reported **neither**:

```csharp
observed?.Invoke(new SeccOutcome(secc.IsDone, SequenceErrorAt: secc.SequenceErrorAt));
```

So a reverse `-2` Plug & Charge run would have been judged on `IsDone` — which a session with an
unverifiable signature reaches just as well, because our station reports the verdict rather than refusing
on it. **Fifth instance in three days of *a value our own side already held that no caller could
reach***, and the first where the discarded value *is* the result of the run.

Now carried and printed:

```
Plug & Charge (inbound, -2): contract DC=MO, C=DE, O=EVerest, CN=UKSWI123456789A;
    challenge OK, digest OK, signature OK, grammar=xmldsig-standalone;
    chain trusted, anchored at DC=MO, C=DE, O=EVerest, CN=MORootCA.
Metering receipts (inbound, -2): 1, 1 verified, grammar=xmldsig-standalone.
```

`xmldsig-standalone` is the Josev-form `SignedInfo` grammar, which is what a Josev-derived EV should
produce — our verifier accepts both forms and says which one it used.

## And the chain was not being validated at all

The first TLS arm came back `chain no trust roots configured — chain not validated`. That is
`ChainResult.NotConfigured`, kept distinct from a rejection precisely so it cannot be read as a pass —
and it was true of **every inbound Plug & Charge result this project has ever recorded**, in both
protocols: the signature was checked against the leaf the car presented, and nobody asked who issued that
leaf. Both station classes have carried a `ContractChainValidator` the whole time; no interop run could
set it.

`V2G_INTEROP_CONTRACT_ROOTS` now does, for `-2` and `-20` alike. With EVerest's own
`ca/mo/MO_ROOT_CA.pem` the verdict becomes *trusted, anchored at `CN=MORootCA`*.

**With a negative control, because "trusted" is worth nothing without one.** Pointing the same knob at
the **V2G** root — a real self-signed root from the same PKI, wrong branch — gives:

```
… signature OK, grammar=xmldsig-standalone; chain unable to get local issuer certificate.
```

Signature still `OK`, chain refused. The two are reported separately because they are separate facts,
and the session completes in both arms: **our `-2` station reports the verdict, it does not refuse on
it.** A reader of `Passed` alone would learn nothing about either.

## A corpus trace from the reverse direction

The plain arm produced a replayable `trace.json` — the first any reverse run in this series has. Every
`-20` reverse session was refused by `SessionTrace.Build` because their EV signs the `AuthorizationReq`
with a key that is theirs, and substituting a recorded signature would verify nothing. **An EIM `-2`
session has no signature to substitute**, so the recording becomes a corpus entry like any other. The
TLS arms are Contract sessions and are refused for exactly the documented reason.

## Reproduce

```bash
# their EV: -2 on, -20 off, and enable_tls_1_3 false — which pins TLS 1.2 *and* is why it presents
# no client certificate, -2 TLS being unilateral
sed -e 's/supported_ISO15118_2: false/supported_ISO15118_2: true/' \
    -e 's/supported_ISO15118_20_AC: true/supported_ISO15118_20_AC: false/' \
    -e 's/enable_tls_1_3: true/enable_tls_1_3: false/' \
    -e '/supported_d20_energy_services: AC/d' \
    config-ac20-reverse-ours.yaml > config-ac2-reverse-ours.yaml

# our station's credential, straight out of the pristine tree — no create_certs.sh, no restore
PASS=$(cat $CERTS/client/cso/SECC_LEAF_PASSWORD.txt)
awk '/BEGIN CERT/{n++} n>1' $CERTS/client/cso/CPO_CERT_CHAIN.pem > cpo-subcas.pem
openssl pkcs12 -export -out secc.p12 -inkey $CERTS/client/cso/SECC_LEAF.key -passin "pass:$PASS" \
    -in $CERTS/client/cso/SECC_LEAF.pem -certfile cpo-subcas.pem -passout pass:123456
```

```bash
V2G_INTEROP_LISTEN=15118 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=ac \
V2G_INTEROP_TLS_SERVER=secc.p12:123456 \
V2G_INTEROP_CONTRACT_ROOTS=$CERTS/ca/mo/MO_ROOT_CA.pem \
  dotnet test -c Release --artifacts-path ~/wsl-artifacts \
              --filter FullyQualifiedName~EverestInteropTests.TheirPyEvJosev &
until ss -lnt | grep -q ':15118 '; do sleep 1; done

~/everest/dist/bin/manager --config ~/everest/configs-ours/config-ac2-reverse-ours.yaml
```

Drop `V2G_INTEROP_TLS_SERVER` for the EIM arm; point `V2G_INTEROP_CONTRACT_ROOTS` at
`ca/v2g/V2G_ROOT_CA.pem` for the negative control. **No `V2G_INTEROP_TLS_REQUIRE_CLIENT`** — `-2` TLS is
unilateral and demanding a client certificate would turn correct behaviour into a failed handshake.

## Artifacts

[`plain/`](plain/), [`tls-roots/`](tls-roots/) and [`wrongroot/`](wrongroot/) — flow, frames, both octet
streams, both sides' logs, and a `trace.json` in the first.

Offline gate: **1 405 green**, four assemblies, exit code 0.

## Next

- **`-2` DC in reverse**, which is the same config with `charge_mode: DC` and their DC car-sim script —
  and would put CableCheck, PreCharge and WeldingDetection through our `Secc2` from the far side.
- **Re-read the earlier Plug & Charge cells now that the chain can be validated.** Every `←SECC` PnC
  result in the matrix was recorded with `ChainResult.NotConfigured`, which is a weaker claim than it
  reads; re-running the `-20` reverse PnC arm with `V2G_INTEROP_CONTRACT_ROOTS` set is one variable.
