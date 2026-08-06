# 2026-08-03 — EVerest `Evse15118D20`, ISO 15118-20 DC over **mutual TLS 1.3**

**A complete -20 DC charge over mutual TLS 1.3 with a foreign station**, on the cipher suites the -20
profile names, with our EVCC validating their SECC chain and their SECC validating ours. 116 exchanges,
every response `OK`, route identical to our own recorded session; run twice.

The app's `libs/EVSimulatorApp/docs/pki-model.md` has pinned -20 to a mutual TLS 1.3 handshake since it
was written, and until today
**our own tests were the only thing that had ever checked it.** Now something else has.

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2025.10.0**, `Evse15118D20` (libiso15118 v0.9.0, OpenSSL 3.0.15) |
| Image | `ghcr.io/everest/everest-demo/manager@sha256:5b0136c31a9f4be985df313b5b1d2e90464d00b203f63613199657f2697ce097` |
| Ours | `Vanaheimr.V2G.Exi` @ `ea23970` + the harness TLS work below |
| Session | ISO 15118-20 DC, Scheduled, **TLS 1.3, mutual**, [`config-d20-tls.yaml`](config-d20-tls.yaml) |
| Their side | `tls_negotiation_strategy: ENFORCE_TLS`, `enforce_tls_1_3: true` |
| Our side | `V2G_INTEROP_TLS=1`, `_TLS_TRUST=<their V2G bundle>`, `_TLS_CLIENT=<vehicle.p12>` |
| Outcome | **complete charge, 116/116 `OK`**, both directions of the handshake authenticated |
| Artifacts | [`flow.md`](flow.md), [`frames.log`](frames.log), [`session.trace.json`](session.trace.json), [`their-charger.log`](their-charger.log) |

Their side, start to finish:

```
Start TLS server [fe80::1092:6bff:fe77:d439%eth0]:50000
Client supports TLS1.3: Change verify mode to SSL_VERIFY_PEER and SSL_VERIFY_FAIL_IF_NO_PEER_CERT
…
Closing TLS connection
TLS connection closed gracefully
```

## The handshake, in the four attempts it took

Each failure moved the error one layer further in, which is the useful way to read them.

**1. `bad protocol version`.** Our harness offered `Tls12 | Tls13` to everything. Their station under
`enforce_tls_1_3` refuses a ClientHello that still permits 1.2 —
`tls_early_post_process_client_hello: unsupported protocol` on their side, an opaque *"bad protocol
version"* on ours. Not a defect: a station being strict about its own profile. **The harness now pins
the TLS version by protocol** — -2 to 1.2, -20 to 1.3 — which is what the app's `pki-model.md` says and what
`TlsOptions`' own documentation warns about leaving permissive. It also pins the **cipher suites** the
same way, so the run asserts the profile rather than inheriting whatever backend was chosen; the final
run above ran with `TLS_AES_256_GCM_SHA384` / `TLS_CHACHA20_POLY1305_SHA256` requested explicitly.

**2. `bad certificate` (alert 42, sent by us).** Our EVCC rejected their server certificate — correctly.
Their station sends **only its leaf**: `CN=SECCCert` issued by `CPOSubCA2`, with neither Sub-CA on the
wire, so nothing can chain it to the V2G root. openssl agrees: *"unable to get local issuer
certificate"*. An EV therefore has to hold the CPO Sub-CAs already. `V2G_INTEROP_TLS_TRUST` now takes a
PEM **bundle**: self-signed entries become trust roots, the rest intermediates we are willing to supply
ourselves. Worth naming as a finding — in the field it is the SECC's job to send its chain — but it is
also the kind of thing an EV can be configured around, so it is not fatal.

**3. `peer did not return a certificate`.** Their `Evse15118D20` switches to
`SSL_VERIFY_PEER | SSL_VERIFY_FAIL_IF_NO_PEER_CERT` the moment the client offers TLS 1.3: **mutual TLS
is not optional there.** So the run needed a Vehicle certificate, and their image ships none —
`client/vehicle/` is empty. See [Certificates](#certificates) below.

**4. `certificate verify failed`.** Our client certificate arrived but its chain did not. Two causes,
one theirs and one mine: `openssl pkcs12 -export` honours only the **last** `-certfile`, so the bundle I
built contained one Sub-CA instead of two; and .NET on macOS parks a PKCS#12 key in the keychain as
non-exportable, which our -20 transport rejects outright because it hands the key to the BouncyCastle
backend. The harness now loads with `X509KeyStorageFlags.Exportable` and sends every non-key entry in
the file as `ClientCertificateChain`. Our own error message for the first of those was exact enough to
act on without a debugger, which is the whole argument for writing them that way.

## Certificates

Explicitly, because generating key material is normally out of bounds here and this run is the
exception, agreed beforehand: **EVerest's own `create_certs.sh` was run, unmodified**, inside the
throwaway container.

Their image ships a V2G root *including its private key* (`client/v2g/V2G_ROOT_CA.key`, a published test
root) and Josev ships `iso15118/shared/pki/create_certs.sh` with a `vehicleLeafCert.cnf`; generating the
test PKI from that script **is** their documented SIL workflow, and the image ships the script but not
its output. So:

```bash
docker exec everest sh -c "cp -r /ext/dist/libexec/everest/3rd_party/josev/iso15118/shared/pki /tmp/pki \
                        && cd /tmp/pki && sh create_certs.sh -v iso-20 -p 123456"
docker exec everest sh -c "cp -r /tmp/pki/iso15118_20/certs/* /ext/dist/etc/everest/certs/"
```

That produces a consistent chain on both sides: `SECC_LEAF ← CPOSubCA2 ← CPOSubCA1 ← V2GRootCA` for the
station and `VEHICLE_LEAF ← VehicleSubCA2 ← VehicleSubCA1 ← V2GRootCA` for us. Nothing of ours was
touched, nothing was registered anywhere, and the material lives only in a container that gets deleted.

The client PKCS#12 (note the single `-certfile`, see failure 4):

```bash
cat ca/vehicle/VEHICLE_SUB_CA2.pem ca/vehicle/VEHICLE_SUB_CA1.pem > /tmp/vchain.pem
openssl pkcs12 -export -inkey client/vehicle/VEHICLE_LEAF.key -passin pass:123456 \
               -in client/vehicle/VEHICLE_LEAF.pem -certfile /tmp/vchain.pem \
               -out /tmp/vehicle.p12 -passout pass:123456
```

## A finding, with a caveat

**`enable_tls_key_logging: true` kills their -20 server.** On the first incoming connection:

```
Incoming connection from [fe80::…]:33236
UDP socket bound to source port: 49152
[ERRO] Shutdown loop() because of: Could not set interface name:eth0 (reason: Protocol not available)
```

With the option off, the same connection completes a TLS handshake. So their key logger opens a UDP
socket and binds it to an interface, and that call fails here.

**The caveat matters:** this runs under `qemu-x86_64` on an ARM host, and `SO_BINDTODEVICE` is exactly
the kind of socket option user-mode emulation does not implement. So the honest statement is *"the key
logger does not work in this environment"*, not *"the key logger is broken"* — and the useful part is
the second-order effect, below.

## The same defect class, three times

Every one of those failures ended their **whole event loop**, not just the connection:

| Trigger | Message |
|---|---|
| a unicast SDP request | `Read on sdp server socket failed (Resource temporarily unavailable)` |
| key logging enabled | `Could not set interface name:eth0 (Protocol not available)` |
| a refused TLS handshake | `Failed to SSL_accept(): … peer did not return a certificate` |

In all three, `loop()` shuts down and **the sockets stay bound**, so the station keeps accepting TCP
connections and answers nothing. From the outside that is indistinguishable from a hung peer, which is
what made the first hour of this run expensive. A per-connection error ending the accept loop is one
defect with three symptoms, and it is the thing worth reporting to EVerest — more than any of the
individual triggers.

## What this proves

Their `Evse15118D20` is cbV2G underneath, so this is not an independent-codec result. What it is:
**the first time anything outside this project has exercised the -20 TLS profile our own documentation
pins** — TLS 1.3, mutual authentication, the profile's two cipher suites, secp521r1-capable backend,
a full DC charge on top — and confirmation that our EVCC builds and validates a foreign SECC chain
against a supplied trust anchor rather than accepting whatever it is handed. The `V2G_INTEROP_TLS=1`
probe path (accept anything, offer both versions) still exists for a first contact with an unknown
station, but it is no longer what a -20 run does.

`session.trace.json` is checked in: 116 exchanges, strictly alternating, complete.

## Next

- **`IsoMux`** — one endpoint answering both -2 and -20, the closest thing to a real charger.
- **AC**, both protocols; our Dynamic AC arm has still never met a station.
- **-2 over TLS 1.2** against `EvseV2G` with the same trust plumbing, now that it exists.
- Report the accept-loop shutdown to EVerest, with all three triggers.
