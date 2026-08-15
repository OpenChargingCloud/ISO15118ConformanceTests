# 2026-08-15 — the leaf Josev's car presents at the TLS handshake, and what it cost us to check

[Last night's Josev run](../2026-08-15-josev-reverse-pnc-chain/notes.md) validated their EV's TLS client
chain for the first time and left one thing open rather than claiming it: the chain anchors at their
**OEM root**, which is the class `-20` asks for, but the leaf is `CN=OEMProvCert` — the OEM
*provisioning* certificate. The open question was whether that is a defect or a test-PKI shape.

**It is a defect, it is upstream-only, and the fix is already written in a fork of their own code.** It
also turned out to be a defect in one of *our* unsent drafts, which is the more useful half.

| | |
|---|---|
| Method | source audit — a **static sweep**, not a session — on two trees, corroborated against last night's live handshake and the certificates on disk |
| Upstream | [`SwitchEV/iso15118`](https://github.com/SwitchEV/iso15118) **`d645255`**, still `origin/main` |
| Fork | [`EVerest/ext-switchev-iso15118`](https://github.com/EVerest/ext-switchev-iso15118) **`26f7988`**, as vendored in everest-core `2026.02.1` |
| Outcome | **filed** — [`josev-iso20-vehicle-certificate.md`](../../reports/josev-iso20-vehicle-certificate.md) — and one existing filing corrected |

## What `-20` actually separates

Two EVCC-side credentials, two *shall*s, and the document keeps them apart in four places:

- **`[V2G20-2339]`** — the EVCC shall contain a **vehicle certificate**, and it is what establishes the
  TLS session.
- **`[V2G20-2342]`** — the EVCC shall contain one **OEM provisioning certificate**, for contract
  certificate installation via the EVSE. Separate requirement, separate purpose, conditional on
  supporting installation at all.
- **Clause 7.3.1** draws the map in one paragraph: OEM roots with vehicle certificates are the TLS-layer
  pair by which the SECC authenticates the EVCC; provisioning certificates are named afterwards, for
  installing and updating contracts.
- **`[V2G20-2598]`** is the one that decides this case. A vehicle certificate's subject carries the
  **EVCCID** as Common Name and the OEM as Organization, **and no further RDNs**.

Annex B gives them different profiles — B.7 provisioning, B.8 vehicle — and it is the vehicle profile
that carries `ExtendedKeyUsage` with `id-kp-clientAuth`, which the document itself recommends including.
Both classes may anchor at an OEM root or a V2G root (`[V2G20-2331]`, `[V2G20-2333]`), **which is why the
anchor alone cannot tell them apart** — and why last night's run, which only printed the anchor, was
right to stop short of a verdict.

## The three trees, side by side

| | TLS client credential | Vehicle certificate in the tree? |
|---|---|---|
| **Josev `d645255`** | `CertPath.OEM_CERT_CHAIN_PEM` (`security.py:209`) | **none.** `grep -c VEHICLE shared/security.py` → 0; `vehicle_cert` / `VEHICLE_LEAF` / `vehicleCert` match nothing under `iso15118/`; `create_certs.sh` mints no branch |
| **EVerest's fork `26f7988`** | `CertPath.VEHICLE_CERT_CHAIN_PEM` (`security.py:193`) | **yes** — a full `VEHICLE_*` path set, its own PKI branch, `VEHICLE_ROOT_PEM` → the V2G root |
| **Ours** | `--vehicle-cert`, printed as *"Presenting Vehicle certificate for mutual TLS"* | **yes**, and `--oem-cert` is a separate flag with a comment saying it is a different certificate |

The measured leaves make the same point without any reading:

```
Josev upstream    CN=OEMProvCert, O=Switch, C=UK, DC=OEM
                  Key Usage: Digital Signature, Key Agreement   (no ExtendedKeyUsage)

EVerest fork      CN=WMIV1234567890ABCDEX, O=Pionix, C=DE, DC=OEM
                  <- VehicleSubCA2 <- VehicleSubCA1 <- V2GRootCA
```

`WMIV…` is a WMI-derived EVCCID as Common Name — `[V2G20-2598]` done properly, by a fork of the very
code that has no word for it. **That is what turns this from a design suggestion into a diff**, and it
is why the filing leads with the fork rather than with the requirement.

Upstream's single leaf does two jobs, which is the part that makes it a conflation rather than a
mis-named constant: `OEM_LEAF` is the TLS credential at `security.py:209` **and** the
`OEMProvisioningCert` of `CertificateInstallationReq` at `evcc/states/iso15118_20_states.py:198`.

## And then it caught one of ours

The same distinction, pointed at our own drafts, broke a claim in
[`everest-d20-trust-anchor.md`](../../reports/everest-d20-trust-anchor.md) — written 2026-08-10, never
sent. Its arm A presented `client/oem/OEM_LEAF.pem` and called it *"the vehicle credential"*; it is
`CN=OEMProvCert`, the provisioning certificate, exactly the thing this audit is about.

The label alone would be a small fix. The conclusion was the problem:

> ~~*so it refuses vehicle certificates*~~

`connection_ssl.cpp:270` loads `path_certificate_v2g_root` **as well as** the MO root, and EVerest's own
`create_certs.sh` mints the vehicle branch under the **V2G** root. So a vehicle certificate from their
own PKI verifies against their own station. What arm A demonstrates is that an **OEM-rooted** client
chain is refused — true, still worth filing, and narrower than what was written.

**Why the draft got there is worth naming.** The installed `dist` tree genuinely had no vehicle leaf —
`client/` holds `cps csms cso mo oem v2g` and nothing else, which is why
[`tls-pki-setup.sh`](../../../tools/interop-everest/tls-pki-setup.sh) opens by saying there is no vehicle
credential to present until one is generated. Faced with an OEM-hierarchy leaf and no vehicle one, the
draft used the available leaf and then described it as the one it was standing in for. **The measurement
was right; the sentence generalised past it.** Corrected in place — title, arm table, and the
consequence — with the correction block saying which half survives.

That is the fourth time an audit of somebody else's credential handling has found something here, and
the first where what it found was a *published claim* rather than a code path.

## What this settles

**Settled:** upstream Josev has no vehicle certificate, in the code or in the PKI, and presents the OEM
provisioning certificate in its place under `ENABLE_TLS_1_3`. Filed, with the fork's implementation as
the suggested direction and the wire measurement as corroboration.

**Also settled, and it is ours:** `everest-d20-trust-anchor.md` overstated its conclusion; the
OEM-rooted half stands, the *"refuses vehicle certificates"* half does not.

**Not settled:** whether upstream considers the single leaf deliberate. The filing asks, and says that a
*yes* narrows the report rather than closing it. Nothing here was run against them beyond last night's
session; no new session was needed and none was taken.

**Not claimed:** that anything breaks today. A station trusting their OEM root accepts this leaf, and
ours did, through a complete DC session. The cost is that the credential does not identify the vehicle,
which `[V2G20-2677]` then carries into the resume binding.

## Reproduce

No rig. Both trees are already on disk from earlier work:

```bash
grep -n 'OEM_CERT_CHAIN_PEM\|VEHICLE_CERT_CHAIN_PEM' ~/josev-src/iso15118/shared/security.py
grep -c VEHICLE ~/josev-src/iso15118/shared/security.py            # 0
```

```bash
grep -n 'VEHICLE' ~/everest/everest-core/build/_deps/josev-src/iso15118/shared/security.py
```

The requirement text is read locally per [`normative-basis.md`](../../normative-basis.md) —
`pdftotext -layout` over the `-20` draft, then `[V2G20-2339]`, `[V2G20-2342]`, `[V2G20-2598]` and
Annex B.7/B.8.

`check_citations.py` was re-run over the whole directory afterwards: **262 citations, 239 resolved, 23
ambiguous, 0 unresolved.** Every citation in the new filing lands where it claims — all seven come back
*ambiguous* because upstream and the fork share every path, which is the tool naming the hazard this
audit is entirely about. The two `OUT OF RANGE` lines it prints are the same thing seen from the other
side: a basename matching a second tree in which the line does not exist. Nothing has drifted. One
citation was tightened while passing (`config.hpp:31` → its full path, since `everest-core` has three).

## Next

- **Nothing follows from this run.** The filing is written and joins the queue; the queue is the
  standing item, not this.
