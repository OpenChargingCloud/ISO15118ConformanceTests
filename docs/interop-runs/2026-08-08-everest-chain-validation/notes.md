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

## The reverse direction, same day

The gap this section used to record — message-level validation never having met a foreign chain — is
closed. `PyEvJosev` drove our SECC (`--listen 55000 --protocol 20 --mode dc --sdp --interface eth0`,
`config-mcs-reverse-ours.yaml`, our station built and run inside WSL so SDP reaches it), and sent a
signed `-20 AuthorizationReq` carrying its real contract certificate.

Their contract branch has a **root of its own**, separate from the V2G one, as the CharIN CP
prescribes: `MORootCA → PKI-Ext_CRT_MO_SUB1_VALID → PKI-Ext_CRT_MO_SUB2_VALID → UKSWI123456789A`
(the leaf's CN is the eMAID).

| `--trust-roots` | Our station's verdict |
|---|---|
| their MO root + both MO Sub-CAs | `signature OK …; chain valid (anchored at CN=MORootCA)` |
| their MO root **alone** | `signature OK …; chain valid (anchored at CN=MORootCA)` |
| their **V2G** root — a real root, wrong branch | `signature OK …; chain REJECTED — unable to get local issuer certificate` |

**Their EV sends its SubCertificates; their station does not send its Sub-CAs.** Same vendor, two
opposite behaviours, and the message-level one is the complete one — root-alone is enough here and
was not enough at TLS. Worth knowing before assuming either shape.

**The third row is the point of the whole exercise.** The signature verified and the chain did not,
and the output says both. Before today our station would have printed exactly the first half and
stopped — "digest OK, signature OK" against a leaf nobody vouched for. That is the difference between
proving a message is well-formed and deciding a contract is good, and it is now visible in one line.

So after these two runs: chain validation is cross-checked against an independent hierarchy at
**both** levels, in both directions, with a working negative control on each.

Still self-checked only: the **OEM provisioning** chain. Their EV does not request
CertificateInstallation in this configuration, so that path has met no foreign material.

## Rig notes

Nothing new about bringing EVerest up; the per-session ritual is unchanged. One small thing worth
recording: `pkill` and the `pgrep` that verifies it must not share a `bash -lc` one-liner — the check
runs before the signal has taken effect and reports the process still up. Two calls, and it is
correct.

The station survived the two refused handshakes with its TCP port still bound and served the third
run normally. The accept-path defect that kills the whole event loop is an `Evse15118D20` property;
`EvseV2G` did not show it here.

## Next

- ~~The reverse direction, for the message-level chains.~~ **Done the same day, above.**
- ~~`--trust-roots` needs to say in its own help text that a station sending a bare leaf requires the
  intermediates in the bundle.~~ **Done** — and the flag turned out to be absent from both `--help`
  texts entirely, which three earlier sweeps had reported as fixed.
- The **OEM provisioning** chain is the one path still judged only by material we minted. It needs a
  peer that asks for CertificateInstallation; `PyEvJosev` in this configuration does not.

## Driving WSL from the agent shell — two traps, one of them new

Both cost time here and both produce "nothing happened" rather than an error.

**Command substitution inside `wsl -- bash -lc '…'` is evaluated by the *outer* Git Bash**, single
quotes notwithstanding: `$(pwd)` in the payload returned the Windows working directory, and `$C` from
an assignment on the same line arrived empty, so `openssl -in $C/x.pem` looked for `/x.pem`. The
existing note about "variable assignments arrive empty" understates it — it is all `$` expansion, and
the fix is to put the work in a **script file** and run that. `MSYS_NO_PATHCONV=1` is still needed on
the outer invocation, or `/mnt/c/...` becomes `C:/Program Files/Git/mnt/c/...`.

**`pgrep -f <pattern>` matches the shell that is running the check**, so "is it still up?" answers yes
forever while the process is long gone. `pgrep -c <name>` or a separate call gives the truth. This is
the read-side twin of the known `pkill -f` self-kill.
