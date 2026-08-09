# The chain against tux-evse — the peer that hands you its own root, 2026-08-09

Fourth counterparty for the chain validator, and the one that tests the property the other three only
assumed: **their car puts its own root certificate on the wire.** A validator that took the peer's word
for its anchor would accept anything. Ours does not, and now that is measured rather than argued.

| | |
|---|---|
| Counterparty | [tux-evse/iso15118-simulator-rs](https://github.com/tux-evse/iso15118-simulator-rs) `main` @ **`fc51088`**, source build, their PKI from their own `mkcerts.sh` |
| Direction | `←SECC` — their injector over TLS, our station serving theirs |
| Session | ISO 15118-2 DC, EIM, `audi-relaxed-autorun.json`, mutual TLS 1.2 |
| Ours | `WWCP_ISO15118_SECC --protocol 2 --mode dc --sdp --interface evse-veth --tls --server-cert <their _server.pfx> --require-client-cert --trust-roots …` |

## Their hierarchy is two deep, and its middle certificate is also their station's

```
_root    DC=root, CN=tux-evse-secure-by-iot-bzh   self-signed, CA:TRUE
  → _server   DC=sub,  same CN                    CA:TRUE  ← also what a station serves as its leaf
      → _client / _contract   DC=end, CN=eMaid    CA:FALSE
```

All `prime256v1`. **One intermediate, not two** — every other counterparty here builds the ISO/CharIN
shape of root → Sub-CA 1 → Sub-CA 2 → leaf, and this validator had never seen a two-deep chain. It also
had never seen a CA certificate used as a TLS server leaf, which is what `_server` is: our station
presents it exactly as their own EVSE binding does.

## Three runs, one question each

| `--trust-roots` | Our station |
|---|---|
| their `_root` alone | `TLS client: chain valid, anchored at DC=root, CN=tux-evse-secure-by-iot-bzh, …` |
| their `_server` sub-CA alone, no root | `chain REJECTED — self-signed certificate in certificate chain` |
| a **foreign** root (EVerest's `V2GRootCA`) | `chain REJECTED — self-signed certificate in certificate chain` |

`openssl verify` agrees on all three, against the same four files.

## The reason string is the finding

With EVerest and eVDriveFlow, the negative controls failed with *"unable to get local issuer
certificate"* — the chain ran out of material before reaching an anchor. Here it fails differently, and
the difference is the whole point: **their `_client_chain.pem` carries three certificates — leaf,
sub-CA, and their root** — so nothing was missing. `X509Chain` built the path all the way to a
self-signed certificate and then refused it, because that certificate was not in the
`CustomTrustStore`.

That is the property `X509ChainTrustMode.CustomRootTrust` exists for, stated by a peer that actually
exercises it: **trust comes from the store, never from what the peer hands over.** Three earlier runs
depended on it and none of them could show it, because none of those peers sent a root. A stack that
merely walked the presented chain to its end and stopped at "self-signed, therefore a root" would have
accepted every one of these three runs, including the two that must fail.

Nothing about tux-evse's behaviour is wrong here. Shipping the root inside the chain file is common and
harmless; it is the *receiver's* job not to be impressed by it.

**Do not read the wording as a contract.** `ChainResult.Reason` is the platform's own status text, and
writing the test for this property surfaced that it is both OS-dependent and localised: the same
refusal that OpenSSL words as *"self-signed certificate in certificate chain"* comes back from Windows
as *"a certificate chain was processed but terminated in a root certificate which is not trusted"* —
and on a German Windows, in German. Every string quoted in this note and its predecessors was captured
on Linux. The verdicts are stable; their prose is not, and
[`ChainValidationTests`](../../../ISO15118ConformanceTests.Simulation/Security/ChainValidationTests.cs)
therefore asserts outcomes and never wording.

## The session over it, unchanged

Run 1's handshake succeeded and the session then went exactly as far as the
[2026-08-06 TLS run](../2026-08-06-tux-tls/notes.md): four exchanges, then

```
--[pkg:68] SimulationStatus::Fail  iso2:authorization_req
           error: {"uid":"iso2-pki-sign-sign","info":"error:no_challenge"}
```

Their EVCC signs the `AuthorizationReq` because a `pki` block is configured, not because the session
selected Contract — [issue A of their filing](../../reports/tux-evse-tls.md), unchanged and confirmed
against their own responder back then. Chain validation neither helped nor hindered it.

## What this needed on our side: nothing

Worth recording because the earlier TLS run needed a deviation. The **station program does not pin
cipher suites** — only the recording fixture does (`TlsProfiles.Iso2CipherSuites`, and
`V2G_INTEROP_TLS_SUITES=platform` was invented on 2026-08-06 to escape it) — so the CLI reaches the
intersection their GnuTLS profile does offer, and this run needed no flag, no code change and no
deviation. The flip side is that **no conformance claim about suites is made here either**: this path
does not read the negotiated suite back, so all that is asserted is that the handshake completed and
what the peer's chain came to.

## Rig hazard, and it cost seven minutes and 1.18 GB

Their `run-injector-tls.sh` caps the binder with `timeout "$2"` and **no `-k`**. A refused handshake is
precisely the "peer disconnected" case that sends this binder into its known ~20 MB/s log spin, and
SIGTERM stops the logging without ending the process. Run 2 therefore held the runner open past seven
minutes and wrote a **1.18 GB** log before it was killed by hand.

The cap has to come from outside and be a hard one:

```bash
timeout -k 5 60 ip netns exec tuxev bash run-injector-tls.sh <scenario> 40 <log>
pkill -9 -x afb-evcc          # they rename the process to the --name value
```

With that, runs 2 and 3 finished in under a minute each. The netns itself does not survive a WSL
restart; `run/netns.sh` is idempotent and recreates it.

Script: [`chain-validation.sh`](../../../tools/interop-tux-evse/chain-validation.sh). Logs:
[`secc-rootonly.log`](secc-rootonly.log), [`secc-subonly.log`](secc-subonly.log),
[`secc-foreignroot.log`](secc-foreignroot.log), and their injector's own account of run 1,
[`injector-rootonly.log`](injector-rootonly.log) (trimmed — the rest is the spin).

## Where the four counterparties now stand

| Counterparty | Depth | What its peer puts on the wire | Root alone enough? |
|---|---|---|---|
| EVerest, station | 3 | leaf + both CPO Sub-CAs | yes |
| EVerest, car (contract, OEM) | 3 | leaf + both Sub-CAs | yes |
| eVDriveFlow, car | 3 | leaf + both VEHICLE Sub-CAs | yes |
| tux-evse, car | **2** | leaf + sub-CA **+ its own root** | yes |

Every peer measured so far sends its intermediates. The one time this project concluded otherwise, it
was [our own defect](../2026-08-09-edf-chain-validation/notes.md) — which is worth remembering the next
time a bundle "needs" the intermediates added to make a peer work.
