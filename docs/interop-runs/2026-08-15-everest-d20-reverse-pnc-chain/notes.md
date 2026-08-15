# 2026-08-15 — the `-20` reverse Plug & Charge cell, this time with the contract chain validated

**The matrix said *"their EV's signed `AuthorizationReq` verified by our SECC"*. It meant the
signature.** Every inbound Plug & Charge result this project had recorded — both protocols, every reverse
run since the first — was taken with `ChainResult.NotConfigured`: the ECDSA signature checked against the
leaf the car presented, and nobody asking who issued that leaf. This run re-takes the `-20` cell with the
anchor configured, and with a negative control.

| | |
|---|---|
| Counterparty | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12`), `PyEvJosev` |
| Config | [`config-ac20-reverse-ours.yaml`](config-ac20-reverse-ours.yaml) — **unchanged** since 2026-08-13 |
| Ours | mutual TLS 1.3, `V2G_INTEROP_CONTRACT_ROOTS` at the **installed** `ca/mo/MO_ROOT_CA.pem` |
| Outcome | **chain trusted, anchored at `CN=MORootCA`** — and refused against the wrong root, with the signature still `OK` |

## The two arms

```
tls        contract DC=MO, C=DE, O=EVerest, CN=UKSWI123456791A;
           challenge OK, digest OK, signature OK (ecdsa-sha256, grammar=xmldsig-standalone);
           chain trusted, anchored at DC=MO, C=DE, O=EVerest, CN=MORootCA.

wrongroot  … same three checks, same values;
           chain unable to get local issuer certificate.
```

Same session otherwise — 56 exchanges, 44 charge loops, service 1, `OK` throughout, in both arms. **Our
station reports the verdict; it does not refuse on it**, which is why the control matters: without it,
"trusted" would rest on a code path nothing had exercised in the failing direction.

The chain their EV actually presents is `CN=UKSWI123456791A` ← `PKI-Ext_CRT_MO_SUB2_VALID` ← … ←
`MORootCA`, and our validator builds it from the `SubCertificates` the car sends in its
`AuthorizationReq` — so this is the peer's own chain being walked, not one assembled here.

**The anchor has to come from the installed tree.** `tls-pki-setup.sh` regenerates the *whole* PKI, MO
branch included, so a `-20` TLS run's contract chains to the regenerated MO root; pointing at a
previously exported copy would fail for a reason that looks like a counterparty defect. The run script
takes it from `$CERTS/ca/mo/MO_ROOT_CA.pem` for that reason.

## What had to change, and it was one line

`PnCAuthResult` has carried a `Chain` field since it existed. `ReportWhatOurStationSaw` printed the three
signature checks and **not** the chain, so even after `V2G_INTEROP_CONTRACT_ROOTS` was wired
[yesterday](../2026-08-15-everest-iso2-ac-reverse-tls12/notes.md) the `-20` cells still could not say
whether the contract was trusted. Printed unconditionally now, because *not checked* and *checked and
bad* must never read the same — which is the same reason `ChainResult` keeps `NotConfigured` distinct
from a rejection.

That makes **six** in three days of the same shape: a value our own side already held that no caller
could reach. The others were the reverse fixture's defaulted power mode, the interop TLS callback's
discarded peer chain, the fixture's unused TLS options, the station's unreported control mode, and the
`-2` branch's dropped PnC and metering verdicts.

## What this does and does not settle

**Settled:** the `-20` `←SECC` Plug & Charge cell now means the contract, not just the signature — for
this counterparty, this run, and any run that sets the variable.

**Not settled, and worth stating:** the *earlier* recordings are not retroactively upgraded. The runs of
2026-08-06 through 08-15 remain sessions in which the chain was not validated; what changed is that the
claim in the matrix now matches a run that validated it. Josev's and eVDriveFlow's `←SECC` PnC cells are
in the same position the `-20` EVerest one was in this morning, and closing them is the same one
variable — but it is their PKI, their MO root, and a run each.

## Reproduce

```bash
bash tools/interop-everest/tls-pki-setup.sh      # mutual TLS 1.3 needs their vehicle credential
```

```bash
V2G_INTEROP_LISTEN=15118 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=ac \
V2G_INTEROP_TLS_SERVER=~/everest/tlsac/secc.p12:123456 V2G_INTEROP_TLS_REQUIRE_CLIENT=1 \
V2G_INTEROP_CONTRACT_ROOTS=~/everest/dist/etc/everest/certs/ca/mo/MO_ROOT_CA.pem \
V2G_INTEROP_CHARGELOOP=20000 \
  dotnet test -c Release --artifacts-path ~/wsl-artifacts \
              --filter FullyQualifiedName~EverestInteropTests.TheirPyEvJosev &
until ss -lnt | grep -q ':15118 '; do sleep 1; done

~/everest/dist/bin/manager --config ~/everest/configs-ours/config-ac20-reverse-ours.yaml
```

Point `V2G_INTEROP_CONTRACT_ROOTS` at `ca/v2g/V2G_ROOT_CA.pem` for the control, and run
[`tls-pki-restore.sh`](../../../tools/interop-everest/tls-pki-restore.sh) afterwards.
`V2G_INTEROP_CHARGELOOP=20000` is there for the reason
[the forty-seventh filing](../../reports/josev-iso20-evcc-charge-loop-pacing.md) exists, and **a run that
used it is not a passing charge-loop conformance result.**

## Artifacts

[`tls/`](tls/) and [`wrongroot/`](wrongroot/) — flow, frames, both octet streams, both sides' logs. No
`trace.json`: a Plug & Charge recording carries a signature that is theirs.

Offline gate: **1 405 green**, four assemblies, exit code 0. Pristine PKI restored, root `88:F8:C2:D5…`
verified back in place.

## Next

- **The Josev and eVDriveFlow `←SECC` PnC cells**, which carry the same overstatement and need the same
  one variable against each counterparty's own MO root.
