# 2026-08-16 — isomux §4, decided: the car names root B, the station serves root A's chain

The [attempt hours earlier](../2026-08-16-everest-isomux-trusted-ca-keys/notes.md) reached this case and
could not decide it, because our own validation refused every station on the managed TLS backend. That
[defect is fixed](../../open-work.md); this is the same design run again, with two arms added and the
ClientHello on tape.

| | |
|---|---|
| Counterparty | everest-core **2026.02.1** (`b61bb12`), `IsoMux` + `EvseV2G`, `config-mux-tls-ours.yaml` (DC) |
| Setup | a **second V2G root** with a valid SECC chain under it, installed beside the first |
| Result | **`IsoMux` serves the chain it was configured with, whatever the car named.** §4's consequence, measured |
| Cost to them | nothing yet — this confirms a filed finding rather than opening one |

## The four arms

Each arm restarts the manager and re-plugs the SIL car, so no arm inherits the previous one's state.

| arm | names in `trusted_ca_keys` | trusts | outcome | |
|---|---|---|---|---|
| **a** | root A | root A | **complete DC session**, 6 s | control |
| **b** | root B | root B | **refused**, `bad_certificate(42)` in 178 ms | the question |
| **c** | root B | root A | **complete DC session**, 6 s | the proof |
| **a2** | root A | root A | **complete DC session**, 6 s | control, last |

**b and c together are the finding.** Both name root B. The one that trusts B is refused; the one that
trusts A completes. So what arrived was a chain under A while the car had asked for B. The controls
bracket the measurement — first and last — so no drift in the rig can account for it.

## The bytes

Arm c was run once more under `tcpdump -i any 'tcp port 64110'`, because *"our client named root B"* is
otherwise a claim about our own configuration rather than about the wire.

**What the car sent** ([`clienthello.txt`](clienthello.txt)) — RFC 6066 extension 3, 23 bytes:

```
001503 eb80148eb96c1e72e2d8c5ea4de6ef873724f5a8
 │  │  └ cert_sha1_hash of CN=V2GRootCA-B
 │  └ identifier_type 3 = cert_sha1_hash
 └ trusted_cas_list, 21 bytes
```

`openssl x509 -fingerprint -sha1` on root B: `EB:80:14:8E:B9:6C:1E:72:E2:D8:C5:EA:4D:E6:EF:87:37:24:F5:A8`.
One authority named, and it is B.

**What the station served** ([`wire-chain.txt`](wire-chain.txt), [`their-served-chain.pem`](their-served-chain.pem)):

```
CN=SECCCert   <- CN=CPOSubCA2 <- CN=CPOSubCA1 <- CN=V2GRootCA
```

```
under root A (their configured one):   OK
under root B (the one the car named):  verification failed
```

`CN=SECCCert-B` under `CN=CPOSubCA-B` under root B was installed, valid, and **not served**.

## What this settles, and what it does not

- §4's **boot behaviour**: `trusted_ca_keys support disabled`, on a fifth boot, two roots installed.
- §4's **consequence**: **measured.** The extension is received and has no effect on chain selection.
- The extension in a `-2` ClientHello does **not** upset their stack — every arm reached the certificate
  exchange, and three of four ran a full DC session through the mux.
- **Not measured:** what `IsoMux` would do with *two* names, or with an identifier type other than
  `cert_sha1_hash`. Neither matters while the feature is off at boot, and both would matter after a fix.
- **Not a new finding.** §4 predicted exactly this from their own boot line. Prediction and measurement
  are different states, and the report now carries the second.

## Their PKI

Restored: exactly the four files that were added are removed, `ca/v2g/` holds `V2G_ROOT_CA.pem` and
`V2G_ROOT_CA.der` again, and chain A verifies under root A
(`F6:BA:4E:BD:D2:D5:8D:71:1B:99:51:A8:39:65:C9:E3:34:4D:48:C4`). No file of theirs was modified at any
point — this run only ever added.

## What the run cost in our own rig

`config-mux-tls-ours.yaml` is a **DC** station (`charge_mode: DC`) and the first pass drove the arms as
AC. All four then failed at `ServiceDiscovery: the station offers no AC energy transfer mode (offered:
DC_extended)` — *after* TLS, which is the only part the measurement needs, so the discriminator was
already visible in that pass. It is recorded anyway: **an arm that fails downstream of the thing being
measured still has to be re-run**, because a note that reports a mixed result invites the reader to
discount the part that was sound.

The capture also cost two attempts. Traffic to the host's own link-local address goes through `lo`, not
through the interface that owns the address, so `tcpdump -i eth0` captured **0 packets** while the
session ran to completion. `-i any` with the port filter is the form that works here.

## Artifacts

[`ours.a.log`](ours.a.log) · [`ours.b.log`](ours.b.log) · [`ours.c.log`](ours.c.log) ·
[`ours.a2.log`](ours.a2.log) — the four arms.
[`ours.c-capture.log`](ours.c-capture.log) — arm c repeated under capture.
[`clienthello.txt`](clienthello.txt) · [`wire-chain.txt`](wire-chain.txt) ·
[`their-served-chain.pem`](their-served-chain.pem) — the two halves of the finding, decoded.
[`their-station.log`](their-station.log) — their boot and the connections.

Offline gate: **1 429 green**, four assemblies, exit code 0 — the run on merged master earlier the same
day. This run changed no code, only documents, so that is the figure rather than a fresh one.

## Next

- Nothing here. §4 is measured in both halves and the report says so.
- The two questions this run deliberately left open — several named roots, other identifier types — are
  worth asking only after `IsoMux` gains a trust anchor, and they belong to whoever fixes it.
