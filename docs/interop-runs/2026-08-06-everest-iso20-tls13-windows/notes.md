# 2026-08-06 — **-20 DC over mutual TLS 1.3, driven from Windows**

**The caveat that had stood in the README's TLS footnote since 2026-08-05 is gone, and gone the only way
that counts: on the wire.** Two complete ISO 15118-20 DC sessions ran from our EVCC on Windows to
everest-core 2026.02.1's `Evse15118D20` over mutual TLS 1.3 — 59 and 68 exchanges, every response `OK`,
both to `SessionStop`. Their side:

```
Client supports TLS1.3: Change verify mode to SSL_VERIFY_PEER and SSL_VERIFY_FAIL_IF_NO_PEER_CERT
Handshake complete!
Verify certificate result is okay
```

| | |
|---|---|
| Counterparty | [EVerest](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), native build |
| Their config | [`config-d20-tls-ours.yaml`](config-d20-tls-ours.yaml) — `ENFORCE_TLS` + `enforce_tls_1_3: true` |
| Ours | `Vanaheimr.V2G.Exi` @ `0059ce6`, `Evcc20Dc`, **BouncyCastle** backend on Windows |
| Machine | WSL2 Debian 13 on Windows 11; station in WSL, our EVCC on Windows through an IPv4 TCP relay |
| The one new variable | `V2G_TLS_BACKEND=BouncyCastle` |

## What had been blocking it

[Finding 4 of the matrix run](../2026-08-05-everest-2026021-matrix/notes.md) named it precisely: on Windows
`TlsPlatform` routed TLS 1.3 through `SslStream`/Schannel, the BouncyCastle fallback was macOS-gated, and
Schannel **refuses to present a client chain whose root the system store does not trust** — upstream of the
wire, so their side merely saw a chain it could not verify. Installing a throwaway test root into the
user's Windows trust store was not an acceptable fix, so the -20 TLS client stayed proven from macOS only.

The app closed that the same day (`99e8925` + `63a2302`): a session names its TLS backend through
`TlsOptions.Backend`, or through `V2G_TLS_BACKEND` for a run that cannot edit them — which is exactly this
harness's situation. **No harness change was needed for this run**; the variable sits beside the existing
`V2G_INTEROP_TLS*` set and `DevTlsOrNull` leaves `Backend` at `Auto`, so the environment wins.

## The run

```bash
V2G_INTEROP_SECC=127.0.0.1:15200 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc \
V2G_INTEROP_TLS=1 \
V2G_INTEROP_TLS_TRUST=…/trust.pem \
V2G_INTEROP_TLS_CLIENT=…/vehicle.p12:123456 \
V2G_TLS_BACKEND=BouncyCastle \
V2G_INTEROP_RECORD=… \
  dotnet test ISO15118ConformanceTests.Simulation -c Release \
    --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

SDP probed with the **TLS security byte** (`SECURITY=00`), relay as ever; the rest of the per-session
ritual is `Evse15118D20`'s usual replug → probe → re-point.

## Finding 1 — the credential is theirs, and it has to be minted

Their pristine `everest-aux` PKI carries **no vehicle credential at all** — only `client/v2g/V2G_ROOT_CA.key`
and the CPO material. So a mutual-TLS run has to generate one, exactly as the 2025.10 run did: their
vendored Josev `create_certs.sh -v iso-20`, which produces
`VEHICLE_LEAF ← VehicleSubCA2 ← VehicleSubCA1 ← V2GRootCA`, everything at password `123456`.

Two practical notes for repeating it:

- **Run the script from its own directory.** It reads `configs/*.cnf` by relative path, so invoking it from
  anywhere else produces a tree full of empty files and a confusing cascade of downstream errors — it still
  exits 0.
- **It regenerates the whole PKI**, station leaf included, so the station and our client agree only if the
  generated tree is installed wholesale. That is what happened here; their PKI was backed up first and
  restored from `everest-core/tests/ocpp_tests/test_sets/everest-aux/certs/` afterwards, so a later PnC run
  is not left standing on generated material.

The chain is **P-256**: their `iso-20` branch sets `EC_CURVE=prime256v1` with a `TODO Check correct version
for ISO 15118-20` beside it. Our -20 profile is written around secp521r1, and the session ran anyway —
worth knowing that the counterparty's own -20 tooling does not produce -20-curve material.

## Finding 2 — "the station sends only its leaf" is a property of the PKI, not of their code

The 2025.10 and 2026.02.1 runs both recorded that `Evse15118D20` sends only its leaf, so a chain to the V2G
root cannot be built from what arrives on the wire — the reason `V2G_INTEROP_TLS_TRUST` accepts a *bundle*
and supplies intermediates itself. With the freshly generated PKI installed, the station sent its **full
chain**:

```
 0 s:CN=SECCCert, O=EVerest, C=DE, DC=CPO      i:CN=CPOSubCA2
 1 s:CN=CPOSubCA2, O=EVerest, C=DE, DC=V2G     i:CN=CPOSubCA1
 2 s:CN=CPOSubCA1, O=EVerest, C=DE, DC=V2G     i:CN=V2GRootCA
```

So the earlier observation was about what `everest-aux`'s `CPO_CERT_CHAIN.pem` contains, not about a
station that withholds intermediates. The bundle-shaped trust option is still right — it just was not
exercised as a workaround here.

## What this does and does not settle

- **Settled:** our EVCC presents a secp-family client chain rooted outside any system store, on Windows,
  with nothing installed anywhere, and a second implementation accepts it and runs a full -20 DC session
  over TLS 1.3. The README's TLS row no longer rests on a single platform.
- **Not settled:** this is *their* test PKI and their curve choice. The -20 profile
  (`libs/EVSimulatorApp/docs/pki-model.md`: TLS 1.3, pinned suites, secp521r1) has still never met a
  counterparty that
  generates secp521r1 material, because the one that ships an `iso-20` cert script does not.

## Artifacts

`tls13.s1.*` and `tls13.s2.*` (`flow.md` / `frames.log` / `trace.json`), `their-charger.tls13.log`, and
`config-d20-tls-ours.yaml`. No keys are kept: the credential is theirs, regenerated in seconds by the
command named above, and their PKI has been restored pristine.
