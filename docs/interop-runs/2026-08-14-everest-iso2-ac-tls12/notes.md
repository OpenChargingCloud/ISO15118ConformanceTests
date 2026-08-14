# 2026-08-14 — ISO 15118-2 AC over TLS 1.2, and a defect of ours that every TLS run had been hiding

**The last untried cell in EVerest's column.** Four complete `-2` AC sessions over TLS 1.2, 13 exchanges
each, every response `OK` — and the arm that was meant to be a formality found that **our own interop
fixture had never read the certificate chain a peer sends**, which is the same defect the app fixed on
2026-08-09 in a second copy nobody looked at.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `EvseV2G`, ISO 15118-2 AC |
| Config | [`config-ac2-tls-ours.yaml`](config-ac2-tls-ours.yaml) — their `config-sil.yaml` as we already had it, with **one** line changed: `tls_security: allow` → `force` |
| Ours | `EverestInteropTests.OurEvcc_AgainstTheirEvseV2G_RunsToCompletion`, `V2G_INTEROP_TLS=1`, the `-2` profile pinned by the fixture |
| Outcome | **4 sessions, 13/13 `OK` each** — three with root + both CPO Sub-CAs as the anchor, **one with the V2G root alone** |

## What their TLS actually is

Measured with `openssl s_client` before anything of ours connected, then confirmed by every session:

| | |
|---|---|
| Version | **TLS 1.2** |
| Suite | **`TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256`** — one of the two ISO 15118-2 prescribes, and the *first* entry of our pinned `-2` list |
| Chain sent | **all three**: `SECCCert` ← `CPOSubCA2` ← `CPOSubCA1`, so an EV holding only `V2GRootCA` can build the path |
| Client certificate | none requested — `-2` TLS is unilateral, so this run needed no vehicle credential and **no PKI regeneration at all** |
| OCSP | **`no response sent`** |

Two of those are worth keeping.

**The suite is the conformant one**, which is the sentence [`tux-evse-tls.md`](../../reports/tux-evse-tls.md)
exists because tux-evse cannot say: their GnuTLS priority string offers neither of ISO 15118-2's two.
EVerest offers the first one and our pinned list took it, so this session asserts the profile rather than
inheriting whatever both ends happened to support.

**Their chain is complete, and that is a contrast rather than a detail.** `Evse15118D20` sends its leaf
alone — the rig README says so, and tells you to put the Sub-CAs in the trust bundle. That advice comes
from the `-20` module and is **wrong for this one**: `EvseV2G` sends the whole path. Which is how the next
section happened.

## The finding, and it is ours

The root-only arm was meant to confirm the paragraph above from our side. It failed:

```
System.Security.Authentication.AuthenticationException :
  The remote certificate was rejected by the provided RemoteCertificateValidationCallback.
```

— while `openssl s_client -CAfile <the same root-only file>` against **the same station, minutes apart**,
returned `Verify return code: 0 (ok)`.

`InteropEnvironment.DevTlsOrNull` builds its own `X509Chain` and its callback was
`(_, certificate, _, _)`. **The third argument is the chain the platform built from what the peer put on
the wire**, and discarding it means every counterparty is judged on its bare leaf. So the trust bundle had
to carry the intermediates, and it always had — which is exactly why nothing noticed:

> a bundle of root + Sub-CAs passes whether or not the peer's certificates are read. Only a **root-only**
> anchor tells the two apart, and no run had ever used one.

**This is the second copy of a defect the app fixed on 2026-08-09.** `TrustRoots.PeerIntermediates` exists
for it, both runnable peers go through it, and its own documentation records that the first version *"cost
a wrong conformance finding"* — a station that sent its full chain was written up as one that sent a bare
leaf. The interop fixtures never went through that helper; they had their own callback.

Fixed the same hour, through the helper rather than beside it, and the root-only session then ran
complete — [`rootonly/`](rootonly/), 13/13 `OK`, anchored at `V2GRootCA` with the path built from what
their station sent.

The regression is
[`ChainValidationTests.TheInteropFixturesCallback_AlsoReadsWhatThePeerSent`](../../../ISO15118ConformanceTests.Simulation/Security/ChainValidationTests.cs)
— and **it is the only one of the seven in that file that fails when the fix is removed**, checked by
removing it. It asserts both halves: a peer that sends its Sub-CAs validates against a root-only store,
and a peer that really does send a bare leaf is still refused.

## The negative control, so "validated" means something

With their **MO** root as the anchor — a real self-signed root from the same PKI, wrong branch — the
handshake is refused, and their log records our alert:

```
SSL routines:ssl3_read_bytes:ssl/tls alert bad certificate … SSL alert number 42
```

So the three green sessions above are a statement about their chain, not about a callback that returns
`true`.

**And their station survived it.** The next session, on the same manager, ran complete — 1 of 1. That is
worth recording next to [`everest-loop-shutdown`](../../reports/everest-loop-shutdown.md), whose
mechanism takes `Evse15118D20`'s whole accept loop down on a failed `SSL_accept()`: `EvseV2G` is a
different module and does not share it.

## What cost an attempt: a second HLC session needs a fresh plug-in

The second session, run without touching the car simulator, died at `PowerDelivery`:

```
SessionAborted: the station answered PowerDeliveryResType with FAILED_ContactorError
```

Their log has the reason, and it is not a defect:

```
14:53:07  CAR ISO AC HLC Open contactor        <- session 1's PowerDelivery(Stop)
14:53:29  SessionSetupReq                      <- session 2
14:53:29  Waiting for contactor is closed
14:53:32  timeout while waiting for contactor to close, signaling error
```

Session 1 opened the AC contactor on its way out and nothing closed it again inside the same plug-in
cycle. Re-plugging the simulated car — which is what a second charge actually is — makes the same session
complete, and did so three times. **The control is the re-plug itself**: same station, same TLS, same
config, one variable.

Not to be confused with [`everest-d20-ac-contactor-edge`](../../reports/everest-d20-ac-contactor-edge.md).
That one is `libiso15118` unable to *learn* about a contactor that closed early; this is a contactor that
is genuinely open, correctly reported as such, in a rig state no real car reaches.

## Reproduce

`-2` TLS is unilateral, so unlike every `-20` TLS run this one installs nothing:
[`tls-pki-setup.sh`](../../../tools/interop-everest/tls-pki-setup.sh) is **not** needed and the pristine
dist PKI is untouched (V2G root `88:F8:C2:D5…` before and after).

```bash
# station: their config-sil.yaml with tls_security: force, EvseV2G on eth0
~/everest/dist/bin/manager --config ~/everest/configs-ours/config-ac2-tls-ours.yaml

# the TLS port is assigned, not configured — read it off their log or off ss(8)
socat TCP-LISTEN:15141,fork,reuseaddr "TCP6:[fe80::…%eth0]:64109"

# the anchor: the V2G root alone is enough, because EvseV2G sends its Sub-CAs
cp ~/everest/dist/etc/everest/certs/ca/v2g/V2G_ROOT_CA.pem root-only.pem

CP_AT_PLUGIN=1 bash sil-car.sh &                 # …and again before every further session
```

```bash
V2G_INTEROP_SECC=127.0.0.1:15141 V2G_INTEROP_PROTOCOL=2 V2G_INTEROP_MODE=ac \
V2G_INTEROP_TLS=1 V2G_INTEROP_TLS_TRUST=root-only.pem V2G_INTEROP_RECORD=/tmp/ac2-tls \
  dotnet test ISO15118ConformanceTests.Simulation -c Release \
    --filter "FullyQualifiedName~EverestInteropTests.OurEvcc"
```

`tls_key_logging` is deliberately **off**: nothing here captures a pcap, and it is one of the known
triggers of the accept-loop shutdown class in the module next door.

## Artifacts

[`s1/`](s1/), [`s2/`](s2/), [`s3/`](s3/) and [`rootonly/`](rootonly/) — frames, both octet streams,
`flow.md` and a replayable `trace.json` each. [`their-charger.log`](their-charger.log) covers the first
three sessions and the refused handshake; [`their-charger.rootonly.log`](their-charger.rootonly.log) the
restarted station and the root-only arm. [`trust.pem`](trust.pem) is the root + Sub-CA bundle,
[`root-only.pem`](root-only.pem) the anchor that found the defect.

## Next

- **`-2` AC with Plug & Charge over this transport.** The `-2` PnC cell is DC-only, and PnC is what `-2`
  TLS is *for*. It needs the `token_provider` wiring [the contract-validator arm](../../../tools/interop-everest/README.md)
  documents, so it is a rig session rather than a variable.
- **The rig README's "pass root + both Sub-CAs" advice is now measurably per-module** — true for
  `Evse15118D20`, false for `EvseV2G` — and every earlier TLS run's trust bundle was carrying weight it
  should not have had. None of those runs is invalidated: a bundle that is a superset validates the same
  chains. But *"we verified their chain"* was a weaker claim than it read, in every one of them, until
  today.
