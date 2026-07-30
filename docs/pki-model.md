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

## Test PKI — use the WWCP PKI builder

The PKI does **not** need to be hand-rolled: the WWCP ISO 15118 stack vendored under
[`libs/WWCP_ISO15118/`](../libs/WWCP_ISO15118) already ships a BouncyCastle-based V2G PKI
builder (`WWCP_ISO15118_PKI`) that generates the full hierarchy, chains, trust bundle, and
CRLs, with `strict-2` (P-256) and `strict-20` (P-521 / Ed448) profiles matching the
[TLS profiles](#tls-profiles-per-protocol) below. It builds the branches all the way down:

```
V2G Root CA (self-signed, shared trust anchor)
├─ CPO     Sub-CA 1 → CPO     Sub-CA 2 → SECC leaf         (TLS server)
├─ MO      Sub-CA 1 → MO      Sub-CA 2 → Contract leaf ×n  (PnC signing, application layer)
├─ OEM     Sub-CA 1 → OEM     Sub-CA 2 → OEM Prov leaf     (out-of-band provisioning only)
├─ Vehicle Sub-CA 1 → Vehicle Sub-CA 2 → Vehicle leaf      (TLS client, -20 mutual TLS)
└─ CPS     Sub-CA   ───────────────────  CPS Signing leaf  (signs CertificateInstallationRes)
```

The **Vehicle branch** was added specifically for this model (the builder previously reused
the Contract / OEM-Prov cert as the -20 TLS client, which contradicted the CharIN 2nd-gen CP);
it now emits a dedicated Vehicle leaf carrying `clientAuth` (never `serverAuth`), separate
from the application-layer Contract cert. Chain bundles land at `chains/secc_chain.pem`,
`chains/vehicle_chain.pem`, `chains/contract_chain.pem`, … with `chains/v2g_root_trust.pem`
as the shared anchor.

Wiring notes for the simulation:

- SECC TLS server ← `secc_chain.pem`; EVCC TLS client ← `vehicle_chain.pem`; both validate the
  peer up to `v2g_root_trust.pem` → mutual-TLS validation succeeds, and a leaf under a
  *different* root must fail (negative test — the builder's `evil_twin_root` variant is exactly
  this case).
- The Contract cert (`contract_chain.pem`) is **not** presented in the TLS handshake — it is
  verified at the application layer during -20 Plug & Charge authorization.
- The builder can generate at test time (nothing sensitive committed, no expiry maintenance),
  consistent with the project's "no real certificates checked in" rule; the current
  [`TestData/TestCertificate.cs`](../Vanaheimr.V2G.Simulation.Tests/TestData/TestCertificate.cs)
  self-signed P-256 cert stays fine for the -2/TLS-1.2 path but is superseded by the builder's
  strict-20 chains for the mutual-TLS work.

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
sits in front of the handshake: the selected protocol (and thus TLS profile) should be an
*input* to `TcpV2GClient`/`TlsOptions`, decided at discovery time, not hard-coded per
connection.

The SLAC + (basic, wired) SDP layers are now vendored under
[`libs/WWCP_ISO15118/`](../libs/WWCP_ISO15118) (`WWCP_ISO15118_SLAC`, `WWCP_ISO15118_SDP`) and
feed exactly this decision — SDP already carries the security/transport mode in its
request/response. ESDP itself (the -20-2 capability extension) is not yet implemented there
(the WWCP stack lists it as out of scope), so for now the protocol/TLS-profile choice comes
from SDP + local policy rather than a full ESDP exchange.

## Wired into the simulation

**Full-stack orchestration.** `E2E/FullStackLoopbackTests.cs` runs the whole entry sequence in one
loopback test: a real **SLAC** match (loopback UDP, both PLC chips keyed) → **SDP** discovery via the
`ISeccDiscovery` seam → **mutual TLS** on the BouncyCastle backend (secp521r1) → **SAP** → a -20 DC
session to SessionStop. It is the end-to-end proof that the stages compose; the individual stages have
their own focused tests below.

The mutual-TLS path is implemented (`Vanaheimr.V2G.Simulation`): `TlsOptions` carries the EVCC
client certificate + SECC "require & validate client cert"; `TcpV2GClient`/`TcpV2GListener`
present/require them. The `Vanaheimr.V2G.Simulation.Tests` project references the WWCP PKI
builder and generates a hierarchy at test time (`TestData/V2GTestPki.cs`, BouncyCastle →
`X509Certificate2` via in-memory PKCS#12), then runs -20 AC/DC sessions over a bilaterally
authenticated `SslStream` (`E2E/MutualTlsLoopbackTests.cs`): SECC leaf = server, Vehicle leaf =
client, both anchored to the shared V2G Root, plus a negative test (certless client rejected).

**Two TLS backends (selectable).** Windows Schannel cannot use P-521 certificates for TLS
(verified: P-256 mutual TLS succeeds on TLS 1.3/1.2, **P-521 fails** "Authentication failed"
server-side; OpenSSL-backed .NET on Linux does support it). So the simulation offers two TLS
backends, both exposing a plain `Stream`:

- **.NET `SslStream`** (default, `TlsOptions`) — fast, platform-native; on Windows limited to
  Schannel-supported curves, so its mutual-TLS tests use P-256 to exercise the mechanism.
- **BouncyCastle TLS** (`Transport/BouncyCastle/`, `BcTlsOptions`) — a managed, cross-platform
  stack that runs the **-20-faithful profile**: TLS 1.3, the -20 cipher suites
  (`TLS_AES_256_GCM_SHA384` / `TLS_CHACHA20_POLY1305_SHA256`), and **secp521r1 *and* Ed448**
  certificates. `E2E/BcMutualTlsLoopbackTests.cs` runs -20 DC mutual-TLS sessions over both a
  P-521 and an Ed448 hierarchy — the SECC leaf (server) and Vehicle leaf (client) come straight
  from the PKI builder's BouncyCastle cert objects (no PKCS#12 bridge needed on this path).

So the Schannel P-521 limitation is a property of one backend, not a project gap: pick the
BouncyCastle backend for the secp521r1/Ed448 -20 TLS profile, the .NET backend otherwise.

## Suite pinning: what each platform allows

`TlsOptions.EnabledSslProtocols` + `TlsOptions.CipherSuites` state a profile; `TlsProfiles` holds
the two suite lists and `E2E/TlsAssert.cs` verifies at runtime what a session *actually* negotiated.
How much of that can be enforced differs per platform — all of the following is measured, on
Windows 10.0.26200 / .NET 10.0.10 and macOS 26.5.2 / .NET 10.0.301:

| | TLS 1.3 on `SslStream` | Per-connection suite pinning |
|---|---|---|
| Windows (Schannel) | yes | **no** — `new CipherSuitesPolicy(…)` throws `PlatformNotSupportedException` in the *constructor*, so the capability check must precede it |
| macOS (SecureTransport) | **no** — `PlatformNotSupportedException`; `Tls12\|Tls13` completes on **1.2** | yes |
| Linux (OpenSSL) | yes | yes |

Consequences the code encodes:

- macOS routes TLS-1.3-only sessions to the BouncyCastle backend (`Transport/TlsPlatform.cs`)
  rather than letting them downgrade — a TLS-1.2 "-20 session" would be silently non-conformant.
- Where suites cannot be pinned, `TlsAssert` records what was negotiated instead of failing.
  Measured unpinned: -2 gets `TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384` on **both** platforms — a
  real profile deviation (the profile wants AES-128-CBC) — while -20 gets
  `TLS_AES_256_GCM_SHA384`, which is conformant only because the platform's own preference happens
  to agree, not because anything enforced it.

**Two profile suites do not exist on Schannel at all** (`Get-TlsCipherSuite`, 28 suites listed):

- `TLS_ECDH_ECDSA_WITH_AES_128_CBC_SHA256` — static ECDH is absent, so the -2 profile's *first*
  alternative is unreachable on Windows in principle; only the ECDHE variant is available.
- `TLS_CHACHA20_POLY1305_SHA256` — of the two -20 suites only `TLS_AES_256_GCM_SHA384` exists.

Enforcing the -2 profile on Windows would therefore mean machine-wide configuration (group policy
"SSL Cipher Suite Order", `Disable-TlsCipherSuite`, or the `…\Cryptography\Configuration\Local\SSL\
00010002` key). That is deliberately **not** done: it changes every process on the host, outlives
the test run, and would make a green suite mean "this machine is reconfigured" rather than "the
simulator is profile-faithful". Where profile fidelity has to be *demonstrated*, use the
BouncyCastle backend, which pins TLS 1.3 and the -20 suites by construction; on the .NET path the
deviation is documented and reported per test.

**P-521 on Schannel is still broken** (re-verified on .NET 10.0.10, both TLS 1.2 and 1.3): the
server fails with `AuthenticationException` → `Win32Exception: Die lokale Sicherheitsautorität
(LSA) ist nicht erreichbar`, the client sees only `IOException: Received an unexpected EOF`.
Controls: P-384 works on both versions, and a P-521 *client* certificate against a P-256 server
fails the same way — so it is specific to secp521r1 and affects both directions. The P-256 test
certificates in `MutualTlsLoopbackTests` therefore stay.

## Open items still to verify

- **eMAID ↔ Contract certificate mapping** and the exact `AuthorizationSetupRes` provider-list
  field driving contract-cert selection — confirm against the -20 CommonMessages schema
  already in the repo.
- ~~**Exact cipher-suite/curve pinning on the .NET backend**~~ — **answered, see
  [Suite pinning: what each platform allows](#suite-pinning-what-each-platform-allows).**
- **SDP discovery stage** is wired via the `ISeccDiscovery` seam (`Discovery/`): `FixedSeccDiscovery`
  (explicit host:port) and `SdpSeccDiscovery` (real `EVCC_SDPClient`). CI covers the SDP message
  round-trips and the discovery-result → `SeccEndpoint` mapping deterministically; the real UDP/IPv6
  **multicast** exchange runs only in real/CLI runs — an EVCC and SECC in one process on one host cannot
  hear each other's multicast (both disable multicast loopback), and Windows multicast in CI is
  unreliable, so it stays out of the deterministic test run.
- **SLAC** stage — wired into the Simulation library (`Slac/`): `SlacEvStage` / `SlacEvseStage` over the
  WWCP SLAC state machines, each optionally programming a PLC chip (`IPlcChipController`) with the
  negotiated NID/NMK and waiting for the AVLN. Unlike SDP, SLAC **is** deterministically loopback-testable
  — `UdpSlacTransport` unicasts to bootstrap/learned peers (no multicast), so a full EV↔EVSE match runs
  in-process over loopback UDP (`Slac/SlacUdpLoopbackTests.cs`, ~1 s): one test asserts both sides agree on
  NID/NMK, a second drives the `SimulatedChipController` on both ends and asserts both chips are keyed.
  Placed directly in the core library (accepting the heavy transitive `Hermod`/`Styx` dependency) — a
  "slim down Hermod" cleanup is noted as a future TODO.

## Related

- [roadmap.md](roadmap.md) — where mutual TLS + contract certs sit in the plan (moved from
  "non-goal" to in-scope target for the -20 simulation).
- [prompts/phase5.md](prompts/phase5.md) — the original Phase 5 prompt (server-side TLS only;
  this note supersedes its "mutual TLS = documented gap" framing for the target picture).
- [`libs/WWCP_ISO15118/`](../libs/WWCP_ISO15118) — vendored WWCP stack providing the PKI
  builder (`WWCP_ISO15118_PKI`, incl. the Vehicle branch), SDP, SLAC, and V2GTP.
- Codec-level XMLDSig / signature suite: the "XMLDSig" sections of the top-level
  [README.md](../README.md).
