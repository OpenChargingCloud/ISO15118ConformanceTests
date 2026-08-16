# 2026-08-16 — isomux §4 with a real `trusted_ca_keys` client: reached, and not decided

The extension was built hours earlier ([`[V2G2-651]`](../../normative-basis.md)), which is what made
[`everest-isomux`](../../reports/everest-isomux.md) §4's failing case reachable at all. This is the first
attempt to run it. **It did not decide §4**, and the reason is ours.

| | |
|---|---|
| Counterparty | everest-core **2026.02.1** (`b61bb12`), `IsoMux` + `EvseV2G`, `config-mux-tls-ours.yaml` |
| Setup | a **second V2G root** with a valid SECC chain under it, installed beside the first |
| Design | name root **B** and trust only B — a station honouring `[V2G2-871]` serves the B chain and the handshake succeeds; one ignoring the extension serves its configured A chain and our validation refuses it |
| Outcome | **both arms fail identically**, and they fail on *our* side. The discriminator does not discriminate |

## What was established

**Their station still boots with the extension switched off, on a fresh start with two roots installed:**

```
iso_mux:IsoMux :: TLS server on eth0 is listening on port [fe80::…]:64110
iso_mux:IsoMux :: No trust anchors for certificate: C:DE CN:SECCCert DC:CPO O:EVerest
iso_mux:IsoMux :: trusted_ca_keys support disabled
```

That is §4's finding, re-confirmed on a third boot and now with a second root in place — the
*"then configure a trust anchor"* reading stays closed.

**Our client sends the extension without upsetting them.** The connection gets past the ClientHello,
through their `OcspCache::lookup` and into the certificate exchange — 181 ms rather than the 39 ms of a
version mismatch. Whatever else is true, `trusted_ca_keys` in a `-2` ClientHello is not something
`IsoMux` chokes on.

**And the B chain is sound**, checked before the arms so the objection cannot be raised afterwards:
`openssl verify -CAfile root-b.pem -untrusted CPO_CERT_CHAIN_B.pem SECC_LEAF_B.pem` → `OK`.

## Why it does not decide anything, and it is our defect

Both arms end at `Org.BouncyCastle.Tls.TlsFatalAlert: bad_certificate(42)`, raised in
`BcV2GTls.ValidatePeer` — **including the control that names the root their station actually serves.**
A control that fails is not a control.

The cause is one line that was never written. On the BouncyCastle backend the peer's certificates arrive
as a DER list and there is no platform `X509Chain`; `TlsPlatform.Adapt` therefore invokes the interop
validation callback with `chain: null`, and that callback builds its path from
`TrustRoots.PeerIntermediates(chain)` — which yields nothing. With a **root-only** trust store, as both
arms deliberately use, no path can be built and every station is refused.

`BcTlsOptions.ValidatePeerChain` exists for exactly this — *"the whole chain as the peer sent it, leaf
first"* — and `ToBcClientOptions` does not set it.

**This is the same defect as 2026-08-14, in its third costume.** That day the *SslStream* interop
callback was found discarding the peer's chain, and fixed by routing it through
`TrustRoots.PeerIntermediates`; the fix answered the question *"where do the intermediates come from"*
for a backend that supplies them. The BouncyCastle path supplies them somewhere else, and nobody
connected the pipe. Both times the symptom was a run refusing a station that was fine, and both times a
**wider trust bundle would have hidden it** — narrowing the anchor to a single root is what makes it
visible, which is the rule this directory already wrote down on 2026-08-14 and did not apply here until
the run failed.

## What this run is worth, stated plainly

- §4's **boot behaviour**: confirmed again, with two roots.
- §4's **consequence** — which chain they serve to a car that named a root: **not measured.**
- The extension itself: **on the wire**, and their stack processes the hello.
- One defect of ours found, in the harness rather than the stack.

The report's §4 paragraph is updated to say *reachable, attempted, blocked by our own validation* rather
than *not yet run*, because those are different states and the difference is the useful part.

## Their PKI

Restored: exactly the four files that were added are removed, `ca/v2g/` holds `V2G_ROOT_CA.pem` and
`V2G_ROOT_CA.der` again, and chain A verifies under root A (`88:F8:C2:D5:13:6E:…`). No file of theirs
was modified at any point — the 2026-08-15 lesson (*a backup taken inside a script that has already run
once is not a backup*) is why this run only ever added.

## Artifacts

[`ours.names-a.log`](ours.names-a.log) · [`ours.names-b.log`](ours.names-b.log) — the two arms.
[`their-station.log`](their-station.log) — their boot and both connections.

Offline gate: **1 421 green**, four assemblies, exit code 0 (the guard fix below is covered by the
existing backend tests).

## What the run cost in our own code

`TlsPlatform.ResolveBackend` refused TLS 1.2 on the BouncyCastle backend — a guard from when that
backend was TLS 1.3-only, left standing when the `-2` profile was added hours earlier. The first two arms
died in 39 ms with our own exception.

**The suite could not have caught it.** `BcTrustedCaKeysTests` constructs `BcTlsOptions` directly and
never passes through `TlsPlatform`, so the loopback was green while the only path that goes through the
gate — the interop fixture — was broken. **A guard one layer above the thing it guards is invisible to a
test that starts below it**, and that is worth more than the fix.

## Next

- **Wire `ValidatePeerChain` in `ToBcClientOptions`** and re-run these two arms. Until then §4's
  consequence is unmeasured, and the report says so.
- Only after that is the *"does the mux serve the named root"* question answerable — and the answer is
  predicted by their own boot line, which is not the same as measured.
