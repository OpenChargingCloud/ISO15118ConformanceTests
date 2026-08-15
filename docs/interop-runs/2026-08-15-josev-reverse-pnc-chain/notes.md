# 2026-08-15 — the Josev `←SECC` Plug & Charge cells, with the contract chain validated

**Both Josev inbound Plug & Charge cells said *"signed messages verified"*. Both meant the signature.**
Same overstatement the EVerest `-20` cell carried until [this morning](../2026-08-15-everest-d20-reverse-pnc-chain/notes.md),
and closed the same way: point the station at the counterparty's own MO root, re-run, and keep a control
that refuses the chain while the signature still verifies. Two cells, two arms each.

| | |
|---|---|
| Counterparty | [SwitchEV/iso15118](https://github.com/SwitchEV/iso15118) @ **`d645255`**, the pinned commit, in their own Docker images |
| Ours | the station CLI, `--trust-roots` — `-2` over unilateral TLS 1.2, `-20` over mutual TLS 1.3 |
| `-2` outcome | **chain valid, anchored at `CN=MORootCA`** — signed `AuthorizationReq` **and** signed `MeteringReceiptReq` |
| `-20` outcome | **chain valid, anchored at `CN=MORootCA`**, and their TLS client chain validated at `CN=OEMRootCA` |

## The four arms

```
-2   MO root      contract CN=UKSWI123456791A; challenge OK, digest OK, signature OK
                  (grammar=xmldsig-standalone); chain valid (anchored at CN=MORootCA).
                  MeteringReceipt: digest OK, signature OK.
-2   V2G root     … same three checks, same values, same receipt;
                  chain REJECTED — unable to get local issuer certificate.

-20  OEM+MO       TLS client: chain valid, anchored at CN=OEMRootCA.
                  contract CN=UKSWI123456791A; challenge OK, digest OK, signature OK
                  (ecdsa-sha256, grammar=xmldsig-standalone); chain valid (anchored at CN=MORootCA).
-20  OEM only     TLS client: chain valid, anchored at CN=OEMRootCA.   ← byte-identical line
                  … same three checks; chain REJECTED — unable to get local issuer certificate.
```

Each pair of station logs differs in **exactly two lines** — the store it loaded and the verdict it
reached — plus the elapsed milliseconds. From the other side it is tighter still: each pair of *their*
logs is the same length to the byte (44 348 and 30 385), because every line differs only in its timestamp
and its session id and both are fixed-width. The sessions themselves are identical: `-2` runs
`PaymentDetails` → signed `Authorization` → 11 `ChargingStatus` with one signed `MeteringReceipt` → two
`PowerDelivery` → `SessionStop` `OK` in both arms, `-20` runs 10 `DC_ChargeLoop` on service 2 in both.
**Our station reports the verdict; it does not refuse on it** — which is exactly why the control is not
optional. Without it, "valid" would rest on a code path that nothing had ever exercised in the failing
direction against this counterparty.

The chain their car presents is `CN=UKSWI123456791A` ← `PKI-Ext_CRT_MO_SUB2_VALID` ←
`PKI-Ext_CRT_MO_SUB1_VALID` ← `CN=MORootCA`, walked from the `SubCertificates` in their own
`PaymentDetailsReq` (`-2`) and `AuthorizationReq` (`-20`).

**One PKI serves both arms**, and that is their decision rather than a shortcut here: every certificate
path in Josev resolves under `iso15118_2/certs/` whatever the protocol, hard-coded, with their own
`TODO: Make filepath flexible, so we can choose between -2 and -20 certificates` above it
(`iso15118/shared/security.py:1445`). `create_certs.sh -v iso-20` writes a second tree that nothing reads.

**The `-2` receipt needed no second anchor.** `Secc2.MeteringReceipt` verifies through the same
`VerifyBodySignature` and the same contract public key established at `PaymentDetails`, so one chain
verdict covers both signatures — which is why the receipt line prints a signature and no chain of its own.

## Why these cells were stale, and it is not the reason yesterday's was

Yesterday's `-20` EVerest cell was understated because a value our side already held could not be
printed. **These two are older than the capability.** Every Josev Plug & Charge run in this project is
dated **2026-07-22**; `--trust-roots`, the station's contract-chain validation and the chain in its
report line all arrived together on **2026-08-08** (`d5c0f36`). Nothing was unreachable — for six weeks
there was simply nothing to reach, and then there was, and no one went back.

That is worth naming separately from the six *unreachable value* instances, because it needs a different
guard: **a matrix cell records what a run showed, and when the harness gains a capability the cells that
predate it quietly become weaker than they read.** Nothing flags them. Both of yesterday's discoveries
and both of today's were found by pointing one variable at one cell and asking whether the answer moved.

## What the `-20` arm also settled, unasked

Their EVCC's TLS client credential had never been checked here: the reverse `-20` script ran
`--require-client-cert` **without** `--trust-roots`, which the CLI honours as dev accept-any and warns
about. With the store configured it is validated, and it anchors at **`CN=OEMRootCA`**.

The anchor class is right. `[V2G20-2331]` puts a vehicle certificate's chain under an OEM root, with a
V2G root as the permitted alternative; `[V2G20-2401]`/`[V2G20-2402]` let a station advertise exactly
those two; and clause 7.3.1 pairs OEM roots with vehicle certificates as what the SECC authenticates the
EVCC by. Josev lands inside that. It is the **opposite** of what an EVerest station did on 2026-08-10,
which took a contract certificate where a vehicle certificate belongs
([filing](../../reports/everest-d20-trust-anchor.md)) — and the contrast is the useful part, because it
shows the requirement is implementable and someone implemented it.

**The leaf is a different question, and it is left open here rather than answered.** What their car
actually presents is `CN=OEMProvCert` — the OEM *provisioning* certificate, which clause 7.3.1 names
separately from the vehicle certificate, and `[V2G20-2339]` says it is a vehicle certificate that
establishes the TLS session. Their PKI mints no vehicle certificate at all, and
`security.py:209` hard-codes the provisioning chain into the client context, so one leaf does two jobs.
Whether that is filable needs their fork read beside upstream first, which this run did not do —
recorded in *Next*, not claimed.

## What this does and does not settle

**Settled:** both Josev `←SECC` Plug & Charge cells now mean the contract and not just the signature,
for this counterparty, this run, and any run that passes `--trust-roots`. `-2` additionally covers the
signed metering receipt, which no other counterparty has produced here.

**Not settled:** the July recordings are not retroactively upgraded, and neither is anything else. The
`-20` **forward** leg (we sign, their SECC verifies) is untouched by this run — their station's own
chain check is [ruled out as a finding](../../josev-cross-validation.md) and unaffected either way.

~~eVDriveFlow's `←SECC` cell is now the last one carrying the weaker claim.~~ **Withdrawn the same day.**
There is no such cell — [they implement no Plug & Charge](../2026-08-11-edf-pnc-source-audit/notes.md),
audited four days before this run, and the matrix has read `— they implement none` since. What is
actually true is stronger and was available without checking anything: **with these two, every inbound
Plug & Charge result in the matrix is anchored.** See the correction at the end.

## Reproduce

The rig had to be rebuilt from the pinned checkout — the images and the generated PKI were both gone:

```bash
bash tools/interop-josev/prepare-josev.sh ~/josev-src && (cd ~/josev-src && docker compose build)
```

`docker compose` names the images after the directory, so tag them the way the scenario scripts expect:

```bash
docker tag josev-src-evcc:latest iso15118-evcc:latest
```

Then, per arm — the station's own `--sdp`, their EVCC in host network mode, and the store as the only
variable:

```bash
dotnet WWCP_ISO15118_SECC.dll --listen 55000 --protocol 2 --mode ac --tls \
  --server-cert /tmp/secc.p12 --server-cert-pass 12345 \
  --trust-roots <dir> --sdp --interface eth0
```

`secc.p12` is their `seccLeafCert` + key + both CPO Sub-CAs, so their car's V2G root accepts our TLS
server certificate; the trust-root directory is `moRootCACert.pem` for the arm and `v2gRootCACert.pem`
for the control. For `-20`, add `--require-client-cert` and put `oemRootCACert.pem` in **both**
directories — their car presents an OEM-rooted client certificate, and dropping that from the control
would change the handshake instead of the contract check.

## Artifacts

[`iso2-mo/`](iso2-mo/), [`iso2-v2g-control/`](iso2-v2g-control/), [`d20-oem-mo/`](d20-oem-mo/),
[`d20-oem-control/`](d20-oem-control/) — both sides' logs per arm. The three root certificates the arms
differ by are here too, and they are roots: public certificates, no key material. No `trace.json`, for
the same reason as this morning — a Plug & Charge recording carries a signature that is theirs.

Offline gate: **1 405 green**, four assemblies, exit code 0.

## The correction this note needed, added hours after it was written

**This note's *Next* named an eVDriveFlow `←SECC` Plug & Charge cell. There is none.** They implement no
Plug & Charge in either role — no `CertificateInstallation` handler, the whole PnC vocabulary present
only in generated bindings, both halves shipping `authorization_services = [EIM]` — established by
[source audit on 2026-08-11](../2026-08-11-edf-pnc-source-audit/notes.md) and shown in the matrix as
`— they implement none` ever since. The claim came from [this morning's EVerest
note](../2026-08-15-everest-d20-reverse-pnc-chain/notes.md), which said it first, and was copied here
without being checked against the table it described.

**It is worth keeping rather than deleting, because of what it is not.** The whole subject of this note
is a claim that outlived its evidence by six weeks with nothing to flag it. This one had nothing to
outlive: it was **false when written**, contradicted by a document in the same directory, four days old,
that this project had produced deliberately to answer exactly that question. A stale claim is a process
gap. This was a **claim about a counterparty made from the shape of the sentence rather than from the
matrix** — three cells sounded better than two, and *"the last one left"* is a satisfying way to end a
run note.

The check that would have caught it costs one grep and belongs in the routine: **before naming a next
cell, read the cell.** The matrix says what is in it, including `—`.

What is true instead is stronger and needed no run: with these two cells closed, **every inbound Plug &
Charge result in the matrix is chain-anchored** — Josev `-2` and `-20` here, EVerest `-20` this morning,
EVerest `-2` the same day. Four cells, two counterparties, each with its own control. There is no fifth
because no other counterparty sends a signed `AuthorizationReq` at all.

## Next

- ~~**Their car's TLS client leaf**: `CN=OEMProvCert` where `[V2G20-2339]` and clause 7.3.1 put a vehicle
  certificate. Read `security.py` against EVerest's fork before deciding whether it is a filing or a
  PKI-shape note.~~ **Done the same day, and it is a filing**: the class is absent from their stack
  entirely and their own fork already implements it
  ([`…-josev-tls-vehicle-cert-audit`](../2026-08-15-josev-tls-vehicle-cert-audit/notes.md),
  [the forty-eighth](../../reports/josev-iso20-vehicle-certificate.md)). The audit also found the same
  conflation in one of our own unsent drafts.
