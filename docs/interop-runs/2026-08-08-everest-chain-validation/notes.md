# Chain validation against EVerest's real V2G hierarchy — 2026-08-08

The chain validator added the same day had only ever seen certificates this project minted itself:
the PKI builder's, and an openssl hierarchy written for its tests. Both send their full chain, and
both were built by the same hand that wrote the validator. This is the first foreign one.

**Counterparty:** EVerest 2026.02.1, native WSL2 build, `config-dc2-pnc-ours.yaml` (`EvseV2G`, ISO
15118-2, `tls_security: allow`). Their shipped test PKI, untouched:
`V2GRootCA → CPOSubCA1 → CPOSubCA2 → SECCCert`, all `O=EVerest`.

**Ours:** `WWCP_ISO15118_EVCC --connect 127.0.0.1:64209 --protocol 2 --mode dc --tls --trust-roots …`
over a socat IPv4→IPv6 relay to `[fe80::215:5dff:fe6b:526%eth0]:64109`. Only the TLS handshake is
under test, so no car simulation and no contactor were needed.

## Three runs, one question each

| `--trust-roots` | Verdict |
|---|---|
| their V2G root alone | `chain REJECTED — a certificate chain to a trusted root authority could not be built` |
| root + `CPO_SUB_CA1` + `CPO_SUB_CA2` | `chain valid, anchored at DC=V2G, C=DE, O=EVerest, CN=V2GRootCA` |
| both Sub-CAs, **no** root | `chain REJECTED — the certificate signature could not be verified; a chain was processed but terminated in a root certificate which is not trusted` |

**The first is not a defect of theirs and not of ours.** EVerest's station sends only its leaf — no
CPO Sub-CAs on the wire — which this repository had already recorded as a property of their
`Evse15118D20` and which holds for `EvseV2G` too. A chain to the root therefore cannot be built from
what arrives, and rejecting it is right. `openssl verify` agrees exactly: `-CAfile root` fails, and
`-CAfile root -untrusted <sub-CAs>` passes.

**The third run is the one that settled a design worry.** `TrustRoots.Load` puts every certificate it
is given into `X509Chain`'s `CustomTrustStore`, and the concern was that a Sub-CA passed that way
would become a *trust anchor* — so a bundle would silently accept anything issued by CPOSubCA2 even
without the root. It does not. .NET's `CustomRootTrust` requires the chain to terminate in a
self-signed certificate that is in the store; a non-self-signed member is usable only as an
intermediate. With the Sub-CAs alone the chain is refused in exactly those words. The bundle form is
therefore safe, and the second run's anchor report — the root, not the Sub-CA it passed through — is
the truth rather than a coincidence.

That is the finding worth keeping: **the semantics were assumed, and are now measured.**

## What this does not cover

The validator also runs at message level, on the station side, over the contract chain and the OEM
provisioning chain. Those paths were exercised the same day against an openssl hierarchy — accepted
under the right root, rejected under an unrelated one, reported as "not checked" without roots — but
**never against a foreign peer's chain.** Doing that needs the reverse direction: their EV
(`PyEvJosev`) sending our SECC a contract chain. Not attempted here.

So the honest state after this run is: TLS-level chain validation is cross-checked against an
independent hierarchy; message-level chain validation is only self-checked.

## Rig notes

Nothing new about bringing EVerest up; the per-session ritual is unchanged. One small thing worth
recording: `pkill` and the `pgrep` that verifies it must not share a `bash -lc` one-liner — the check
runs before the signal has taken effect and reports the process still up. Two calls, and it is
correct.

The station survived the two refused handshakes with its TCP port still bound and served the third
run normally. The accept-path defect that kills the whole event loop is an `Evse15118D20` property;
`EvseV2G` did not show it here.

## Next

- The reverse direction, for the message-level chains.
- `--trust-roots` needs to say in its own help text that a station sending a bare leaf requires the
  intermediates in the bundle. Nothing in the flag's name suggests that, and the first run above is
  what the surprise looks like.
