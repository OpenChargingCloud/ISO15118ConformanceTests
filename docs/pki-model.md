# PKI & certificate model for the -20 simulation

Design note for Phase 5: which certificates the EVCC/SECC use, on which layer, and how the
test PKI is built. The target is a simulation that **starts at the TLS handshake** — for
ISO 15118-20 that means **mutual TLS**, which pulls certificate handling into scope
(previously a documented gap, see [roadmap.md](roadmap.md)).

## Sources

Two CharIN documents, both under <https://www.charin.global/technology/knowledge-base/>:

- **Certificate types & hierarchy** — *Certificate Policy for the CharIN V2G
  second-generation PKI, v1.0 (2022-11-23)*, "compliant to ISO 15118-20". Realises the
  certificate structure of ISO 15118-20 (§7.9 / Annex) and pins the concrete branch layout
  used here. Where the CP and the raw standard differ in naming, the CP's names are used
  (the CP notes ISO 15118-20's own naming is not fully consistent).
- **TLS version / cipher / curve profiles** — *CharIN implementation guide for TLS with ISO
  15118: avoidance and handling of wrong implementations, Version 1 (2026-04-18)*. Pins the
  concrete TLS parameters per protocol (see [TLS profiles](#tls-profiles-per-protocol)) and
  catalogues the field failures caused by mismatched TLS-version / curve / certificate
  combinations.

## Two authentication planes

The simulation must keep these strictly separate — they use **different certificates on
different layers**:

| Plane | Layer | EV certificate | EVSE certificate | Purpose |
|---|---|---|---|---|
| Transport auth | TLS 1.3 handshake | **Vehicle certificate** | **SECC certificate** | Mutually authenticate the TLS channel |
| Plug & Charge auth | Application (V2G messages) | **Contract certificate** | — (verifies) | EV signs the `GenChallenge`; SECC/e-MSP authorise charging |

Per the CP: the SECC always acts as the TLS **server**, the EVCC always as the TLS
**client**. Both leaf certificates chain to the *same* single V2G Root CA, so each side
validates the other's chain to a shared trust anchor → that is the mutual-TLS binding.

The Contract certificate is **not** part of the TLS handshake. It is presented later, inside
the -20 `AuthorizationReq` (PnC variant), to sign the challenge the SECC issued in
`AuthorizationSetupRes`; the SECC verifies the contract-cert chain and the signature. This
is the same split ISO 15118-**2** already used (contract cert = app-layer only) — the new
part for -20 is the *transport-layer* client cert (the Vehicle certificate).

## The five PKI branches (CharIN V2G CP)

One V2G Root CA is the sole trust anchor; every branch is `V2G Root CA → Tier-1 CA →
Tier-2 CA → leaf`.

| Branch | Leaf (end entity) | Held by | Needed for the session? |
|---|---|---|---|
| **CSO** | SECC certificate | SECC | ✅ TLS server cert |
| **Vehicle** | Vehicle certificate | EVCC | ✅ TLS client cert |
| **e-MSP** | Contract certificate (1…n) | EVCC | ✅ Plug & Charge signing |
| **OEM Prov** | OEM Provisioning certificate | EVCC | ❌ only for out-of-band contract-cert provisioning |
| **CPS** | CPS certificate | Provisioning service | ❌ only signs contract-cert install messages |

Why "1…n" contract certificates: an EV may hold a contract with several e-MSPs, one Contract
certificate each. The SECC advertises the mobility operators / providers it accepts in
`AuthorizationSetupRes`; the EVCC then **selects** a Contract certificate whose provider is
in that list. Holding ≥2 is what makes the selection non-trivial and therefore worth
exercising.

## Scope assumption: contract certs pre-installed out-of-band

The simulation **assumes the EV already holds ≥2 valid Contract certificates**, installed
out-of-band. This deliberately skips the online provisioning flow (the OEM Prov / CPS
branches, the `CertificateInstallation`/`CertificateUpdate` message exchange, and the
Diffie-Hellman-wrapped private-key transfer the CP describes). Note the provisioning
*messages* themselves are already generated and vector-tested at the codec level (Phase 4) —
it is the live provisioning *session flow* that stays out of scope, not the wire format.

## Test PKI (generated at test time, nothing checked in)

Mirror the existing approach in
[`Vanaheimr.V2G.Simulation.Tests/TestData/TestCertificate.cs`](../Vanaheimr.V2G.Simulation.Tests/TestData/TestCertificate.cs)
(self-signed cert via `CertificateRequest`) but build small real chains so chain validation
actually runs:

```
V2G Root CA (self-signed, shared trust anchor)
├─ Tier-1 CSO CA     → Tier-2 CSO CA     → SECC leaf        (TLS server)
├─ Tier-1 Vehicle CA → Tier-2 Vehicle CA → Vehicle leaf     (TLS client)
└─ Tier-1 e-MSP CA   → Tier-2 e-MSP CA   → Contract leaf ×n (PnC signing)
```

- Both TLS sides trust the same root → mutual-TLS validation succeeds; a leaf under a
  *different* root must fail (negative test).
- The Tier-1/Tier-2 split can be collapsed to a single intermediate for the first
  iteration if two CA levels per branch prove noisy — the two-level structure is what the CP
  specifies, so keep it as the target.
- Everything is generated per test run (no expiry maintenance, nothing sensitive committed),
  consistent with the project's "no real certificates checked in" rule.

## TLS profiles per protocol

Per the CharIN TLS implementation guide, the TLS version, cipher suites, signature
algorithms, **and the certificate/key-exchange curve are one coupled profile** — mixing them
across protocols is *the* dominant field failure. The two profiles:

| | ISO 15118-2 | ISO 15118-20 |
|---|---|---|
| TLS version | **1.2 only** | **1.3 only**, mutual auth |
| Cipher suites | `TLS_ECDH_ECDSA_WITH_AES_128_CBC_SHA256`, `TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256` | `TLS_AES_256_GCM_SHA384`, `TLS_CHACHA20_POLY1305_SHA256` |
| Signature algs | ECDSA / secp256r1 | `ecdsa_secp521r1_sha512`, `ed448` |
| Curve (PKI + key exchange) | **secp256r1** | **secp521r1** (and Curve448 for Ed448) |

Rules that fall out of this and must be enforced in the simulation:

- **The certificate-chain curve must match the negotiated TLS profile.** For -20 the TLS
  leafs (Vehicle + SECC) are **secp521r1** — the current P-256 `TestCertificate.cs` is a
  -2/TLS-1.2-only placeholder and must be replaced for the -20 path. This is consistent with
  our existing app-layer `V2GSignature` (already P-521 / SHA-512 or Ed448).
- **Do not offer "ISO 15118-2 over TLS 1.3."** It is technically possible but unsupported by
  ESDP and a documented interop hazard; the mapping is strictly -2↔1.2 and -20↔1.3.
- **Disable TLS 1.3 middlebox-compatibility mode** on both sides (the spurious
  `ChangeCipherSpec` dummy records break EVCCs that don't expect them). In .NET this is not
  exposed the way OpenSSL exposes it — verify Schannel's behaviour, note as a deviation if it
  cannot be turned off.
- These belong in [`TlsOptions.cs`](../Vanaheimr.V2G.Simulation/Transport/TlsOptions.cs) as
  two explicit named profiles, never the library default — the guide's core lesson is that
  letting the TLS stack auto-select (highest version + "best" curve) is exactly what breaks.

Test-case shape to mirror (guide §6.1): T1 = -2-only EVSE + dual-stack EV → downgrades to
TLS 1.2/secp256r1; T2 = -20-only EVSE + dual-stack EV → TLS 1.3/secp521r1; T3 = multi-proto
EVSE + -2-only EV → TLS 1.2.

## Capability exchange before TLS (ESDP)

The guide recommends **ISO 15118-20-2 (ESDP)** to exchange the supported V2G protocols, TLS
versions and security profiles **before** the TLS handshake, so the EV can build a
consistent `ClientHello` instead of guessing. This is the discovery/capability stage that
sits in front of the handshake — relevant to the SDP/SLAC code being brought in: the
selected protocol (and thus TLS profile) should be an *input* to `TcpV2GClient`/`TlsOptions`,
decided at the ESDP stage, not hard-coded per connection.

## Open items still to verify

- **eMAID ↔ Contract certificate mapping** and the exact `AuthorizationSetupRes` provider-list
  field driving contract-cert selection — confirm against the -20 CommonMessages schema
  already in the repo.
- **Schannel/.NET control** over the exact -20 cipher-suite ordering and middlebox mode —
  confirm what `SslServerAuthenticationOptions` / `CipherSuitesPolicy` actually let us pin on
  Windows, and record anything that can't be pinned as a known deviation (as the existing
  `TlsOptions.cs` gap comment already does for cipher pinning).

## Related

- [roadmap.md](roadmap.md) — where mutual TLS + contract certs sit in the plan (moved from
  "non-goal" to in-scope target for the -20 simulation).
- [prompts/phase5.md](prompts/phase5.md) — the original Phase 5 prompt (server-side TLS only;
  this note supersedes its "mutual TLS = documented gap" framing for the target picture).
- Codec-level XMLDSig / signature suite: the "XMLDSig" sections of the top-level
  [README.md](../README.md).
