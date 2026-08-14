# 2026-08-14 — the reverse direction over TLS, for the first time in any protocol

**Their EV discovered our station over SDP with the TLS security byte set, handshook mutual TLS 1.3 with
a vehicle certificate of its own, and charged: 56 exchanges, every response `OK`, 43 `AC_ChargeLoop`
pairs to `SessionStop`.** The last combination in
[`open-work.md`](../../open-work.md)'s untested table, and it had never run because of **our fixture**,
not because of the rig.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `PyEvJosev` wrapping `EVerest/ext-switchev-iso15118` `26f79889` |
| Config | [`config-ac20-reverse-ours.yaml`](config-ac20-reverse-ours.yaml) — **unchanged** from 2026-08-13; it already carried `tls_active: true` and `enable_tls_1_3: true` |
| Ours | `EverestInteropTests.TheirPyEvJosev_AgainstOurSecc_RunsToCompletion`, listening on 15118 and advertising over SDP, **inside WSL** because SDP is multicast |
| Outcome | **relaxed arm: 56 exchanges, all `OK`, 43 charge loops.** **strict arm: the 500 ms sequence timeout, over TLS this time** |

## The transport, read back rather than configured

```
SDP: request from [fe80::…]:38947 — ISO_15118_2, TLS, TCP
SDP: answered [fe80::…]:38947 with [fe80::…]:15118 (TLS, TCP), 28 byte(s).
TLS: their client certificate is DC=OEM, C=DE, O=Pionix, CN=WMIV1234567890ABCDEX.
TLS: Tls13, TLS_AES_256_GCM_SHA384
```

| | |
|---|---|
| Version / suite | **TLS 1.3**, `TLS_AES_256_GCM_SHA384` — one of the two ISO 15118-20 prescribes |
| Their vehicle credential | `CN=WMIV1234567890ABCDEX, O=Pionix, C=DE, DC=OEM`, **prime256v1**, issued by `VehicleSubCA2` |
| Ours | `CN=SECCCert, O=EVerest, C=DE, DC=CPO` — **their own** SECC leaf plus both CPO Sub-CAs |
| Who validated whom | mutual. Their EV checks the station against `CertPath.V2G_ROOT_PEM` with `CERT_REQUIRED` and `check_hostname = False` (`iso15118/shared/security.py`, `get_ssl_context(server_side=False)`); ours required and read back theirs |

**The material has to be theirs, and that is what makes the run mean something.** Their EV anchors at the
V2G root in its own PKI path, so a certificate we minted could not have been accepted — this is their
`create_certs.sh` output installed wholesale, our station presenting the CPO half of it, restored
afterwards ([`tls-pki-setup.sh`](../../../tools/interop-everest/tls-pki-setup.sh) and its restore twin;
pristine root `88:F8:C2:D5…` verified back in place).

Their vehicle leaf is **P-256**, which is what
[`josev-iso20-pki-curve`](../../reports/josev-iso20-pki-curve.md) is about — that filing concerns the
key-wrap curve for contract provisioning, not the TLS credential, and no conformance claim about TLS
groups is made here.

## Why it had never run: two lines in a fixture

`InteropEnvironment.ServerTlsOrNull` has existed since the tux-evse reverse runs and its own
documentation calls it *"the only way that direction can run over TLS at all"*. The **eVDriveFlow**
reverse fixture uses it. The EVerest one did not:

```csharp
using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.IPv6Any, listenPort));   // no TLS options
await using var sdp = await InteropSdp.AdvertiseOrNullAsync(listener.LocalEndpoint.Port,
                                                             tls: false, cts.Token);      // a constant
```

So *"the reverse direction has never run over TLS in any mode"* was a statement about our harness that
read like one about the counterparties. It is the same shape as the mode-defaulting defect this fixture
gave up on 2026-08-13 — **a capability we already held, that no call site reached for** — and this is the
third instance in a week.

Fixed by mirroring the eVDriveFlow fixture: the listener takes `serverTls`, the SDP flag is derived from
it rather than written down, the recording gains a `-tls` suffix, and the negotiated parameters are
printed from the `SslStream` instead of restated from the configuration.

## What cost the first attempt, and it is a rig trap worth naming

The first run advertised **NoTLS** with `V2G_INTEROP_TLS_SERVER` set. The fixture was fine; the assembly
was not. `dotnet test --artifacts-path ~/wsl-artifacts` is a **separate output tree from the Windows
`bin/`**, so `--no-build` there runs whatever WSL last built — which predated the change by an hour. The
symptom is the worst kind: everything starts, the peer connects, the session runs, and only one word in
one log line says the transport was not what was asked for.

**Rebuild in WSL before the first reverse run after any fixture change**, or drop `--no-build`.

It did leave one observation behind. **Their EV asked for TLS in its SDP request, was answered `NoTLS`,
and connected in plaintext anyway** — and then offered `-20`, which `[V2G20-1237]` puts in the TLS 1.3
row alone. That is the same requirement our own EVCC was fixed against on 2026-08-10. It is recorded
here and in [`open-work.md`](../../open-work.md) as a candidate rather than filed: this run configured
their car and then offered it a downgrade, so the arm that would settle it is a station of theirs
answering `NoTLS` to a `-20` EV, not ours.

## The strict arm: the same 532 ms, over TLS

With our charge-loop timer at its conformant 0,5 s (`[V2G20-1500]`), the session dies where it did on
plain TCP:

```
SessionAborted: SECC sequence timeout: EV silent for > 500 ms in the charge loop
```

**And this measures the thing [the forty-seventh filing](../../reports/josev-iso20-evcc-charge-loop-pacing.md)
listed as unmeasured.** Its checklist says the plain-TCP number *"is this one or worse, but 'or worse' is
unmeasured"*. Now it is:

| | span | loops | per pair |
|---|---:|---:|---:|
| plain TCP, 2026-08-13 | 23,407 s | 44 | **≈532 ms** |
| mutual TLS 1.3, today | 23,400 s | **43** | **≈544 ms** |

Both from their own `PowerOn` → `CarRequestedStopPower` lines, both inside the car simulator's own 20 s
window. TLS costs about **12 ms per exchange** — the deviation is unchanged in kind and slightly worse in
degree, which is what the report predicted without being able to say so.

## One difference between the plain and the TLS runs, stated as bytes

The TLS sessions carry `PowerDeliveryReq` messages the plain ones do not:

| | before the first charge loop |
|---|---|
| plain, 2026-08-13 | **one** `PowerDeliveryReq`, 37 bytes (the one carrying the charging profile) |
| TLS, relaxed | **two**: a 27-byte one, then the 37-byte one |
| TLS, strict | **four**, then a single charge loop |

Our station answered every one `OK`. The shorter form differs from the session-ending
`PowerDeliveryReq` (also 27 bytes) in its trailing byte, so it is not a repeat of the stop — the shape is
consistent with the car polling `PowerDelivery` while its own side is not yet ready, and **that reading is
an inference, not a decode**: `SessionTrace.Build` refuses a PnC recording, so no artifact here decodes
those two frames. Two TLS runs show it and two plain ones do not, which is suggestive and not conclusive.

## And a label that had never been wrong before

`InteropEnvironment.ReportTransport` printed `SslStream.RemoteCertificate` as *"server …"*. Correct in
every forward run; in the first reverse TLS session it ever saw, it labelled their **vehicle** certificate
that way. Now named by role off `ssl.IsServer`.

## Reproduce

```bash
bash tools/interop-everest/tls-pki-setup.sh          # a vehicle credential has to exist at all
# …plus the mirror image it does not export: SECC_LEAF + both CPO Sub-CAs as a PKCS#12
openssl pkcs12 -export -out secc.p12 -inkey .../client/cso/SECC_LEAF.key -passin pass:123456 \
    -in .../client/cso/SECC_LEAF.pem -certfile cpo-subcas.pem -passout pass:123456
```

```bash
# ours first: their EV probes once, shortly after the manager boots
V2G_INTEROP_LISTEN=15118 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac \
V2G_INTEROP_TLS_SERVER=~/everest/tlsac/secc.p12:123456 V2G_INTEROP_TLS_REQUIRE_CLIENT=1 \
  dotnet test -c Release --artifacts-path ~/wsl-artifacts \
              --filter FullyQualifiedName~EverestInteropTests.TheirPyEvJosev &
until ss -lnt | grep -q ':15118 '; do sleep 1; done

~/everest/dist/bin/manager --config ~/everest/configs-ours/config-ac20-reverse-ours.yaml
```

Add `V2G_INTEROP_CHARGELOOP=20000` for the measured arm. **A run that used it is not a passing
charge-loop conformance result.** Run [`tls-pki-restore.sh`](../../../tools/interop-everest/tls-pki-restore.sh)
afterwards.

## Artifacts

[`strict/`](strict/) and [`measure/`](measure/), each with the flow, the frames, both octet streams and
both sides' logs. Neither has a `trace.json`: their EV signs the `AuthorizationReq` with a key that is
theirs, so `SessionTrace.Build` refuses the recording rather than substitute the recorded signature and
verify nothing. The station credential is deliberately **not** committed — it carries a private key, and
`create_certs.sh` regenerates it in seconds.

## Next

- **The `-20`-over-plain-TCP observation above**, if somebody wants it settled from their side.
- `AC_BPT` in reverse, still — their config would need `supported_d20_energy_services: AC_BPT`, and our
  `Secc20Ac` already offers the service.
- A reverse **`-2`** run over TLS 1.2, which is now one environment variable rather than a fixture change.
