# Draft report to SwitchEV — the EVCC presents its **OEM provisioning** certificate as the TLS client credential, because the stack has no vehicle certificate at all

Status: **draft, not sent.** Source audit of **`SwitchEV/iso15118` @ `d645255`** (still `origin/main`),
corroborated on the wire: measured 2026-08-15 against a live TLS 1.3 session in which our station
validated their EV's client chain and printed the leaf it got. Post it under your own name; see
*Before sending* at the bottom.

Evidence in this repository:
[`2026-08-15-josev-tls-vehicle-cert-audit`](../interop-runs/2026-08-15-josev-tls-vehicle-cert-audit/notes.md) —
the source reading on both trees, the certificate dumps, and the run whose store made the leaf visible
([`…-josev-reverse-pnc-chain`](../interop-runs/2026-08-15-josev-reverse-pnc-chain/notes.md)).

Other reports go to SwitchEV: [`josev-iso20-pause-resume.md`](josev-iso20-pause-resume.md),
[`josev-iso20-renegotiation.md`](josev-iso20-renegotiation.md),
[`josev-iso20-charge-loop-timeout.md`](josev-iso20-charge-loop-timeout.md),
[`josev-iso20-evcc-charge-loop-pacing.md`](josev-iso20-evcc-charge-loop-pacing.md) — and
[`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md), which is the other `create_certs.sh` report and
**should be sent with this one or just before it**: they touch the same script for different reasons.

**Read the last section first if you are short of time.** Your own downstream fork already implements
this, so the fix is a diff that exists rather than a design we are proposing.

---

**Title:** `get_ssl_context` loads the **OEM provisioning** certificate chain as the EVCC's TLS client
credential; `[V2G20-2339]` makes that a **vehicle** certificate, `[V2G20-2342]` makes the provisioning
certificate a separate credential for a separate job, and neither `iso15118/` nor `create_certs.sh` has
any notion of a vehicle certificate

**Version:** `SwitchEV/iso15118` @ **`d645255`** ("Pydantic upgrade to v2", #455), `origin/main` when
read on 2026-08-15. Paths relative to `iso15118/`.

## What the code does

`shared/security.py:208-212` — the client half of `get_ssl_context`, behind the `ENABLE_TLS_1_3` gate at
`:206`:

```python
ssl_context.load_cert_chain(
    certfile=CertPath.OEM_CERT_CHAIN_PEM,
    keyfile=KeyPath.OEM_LEAF_PEM,
    password=load_priv_key_pass(KeyPasswordPath.OEM_LEAF_KEY_PASSWORD),
)
```

The same leaf is the OEM provisioning certificate everywhere else in the tree, which is what makes this
a conflation rather than a naming choice:

| Where | What it is used for |
|---|---|
| `shared/security.py:209` | the **TLS client certificate** |
| `evcc/states/iso15118_20_states.py:198` | `CertificateInstallationReq`'s `OEMProvisioningCert` |
| `evcc/states/iso15118_2_states.py:423` | the same, `-2` |
| `secc/states/iso15118_2_states.py:794` | the SECC side reading it back |

`grep -c VEHICLE shared/security.py` returns **0**, and `vehicle_cert`, `VEHICLE_LEAF` and `vehicleCert`
match **nothing anywhere under `iso15118/`**. `CertPath` (`:1445`) has entries for contract, CPO, CPS and
OEM material and none for a vehicle certificate. `shared/pki/create_certs.sh` mints no vehicle branch.
So this is not a wrong constant in one call — the credential class is absent from the stack.

## What we measured

Not strictly needed for a source finding, but it is what made us look. On 2026-08-15 our station ran a
`-20` DC session over mutual TLS 1.3 against your EVCC (Docker, `ENABLE_TLS_1_3=True`) with a trust store
configured for the first time, and printed what it validated:

```
TLS client: chain valid, anchored at DC=OEM, C=UK, O=Switch, CN=OEMRootCA.
```

and the leaf behind it, from your own `create_certs.sh -v iso-2` output:

```
subject=CN=OEMProvCert, O=Switch, C=UK, DC=OEM
X509v3 Key Usage: critical
    Digital Signature, Key Agreement
```

No `ExtendedKeyUsage`. Session completed normally — 10 charge loops to `SessionStop` — because our
station accepts what the anchor accepts. Nothing failed, which is rather the point: this is invisible
from the wire unless somebody looks at the leaf.

## Why we think it is worth fixing

**Because `-20` defines two EVCC-side credentials with two separate obligations, and this presents one
where the other belongs.**

- **`[V2G20-2339]`** — the EVCC **shall** contain a vehicle certificate, and that certificate is what
  establishes the TLS session.
- **`[V2G20-2342]`** — the EVCC **shall** contain one OEM provisioning certificate, and its stated
  purpose is contract certificate installation via the EVSE. A *separate* requirement, for a separate
  job, conditional on supporting installation at all.
- Clause **7.3.1**'s overview separates them in one paragraph: OEM roots and **vehicle** certificates
  are the pair the SECC uses at the TLS layer to authenticate the EVCC, while OEM **provisioning**
  certificates are named afterwards, for installing and updating contract certificates.
- **`[V2G20-2598]`** — the vehicle certificate's subject **shall** carry the EVCCID as Common Name and
  the OEM's identifier as Organization, **and no further values in the DN**. `CN=OEMProvCert, O=Switch,
  C=UK, DC=OEM` is none of that: it names a certificate role rather than a vehicle, and it carries two
  RDNs the requirement excludes. **A relying party cannot learn which car it is talking to.**
- **Annex B** gives the two classes different profiles — B.7 for OEM provisioning, B.8 for vehicle — and
  the vehicle profile is the one carrying `ExtendedKeyUsage` with `id-kp-clientAuth`, which the document
  itself recommends including. The measured leaf has no `ExtendedKeyUsage` at all.
- **`[V2G20-2677]`** is how it propagates: the pause/resume binding is computed over the **vehicle**
  certificate from the handshake. Whatever is presented there becomes the identity a resumed session is
  bound to — so a binding built on this leaf is bound to a provisioning credential, and to the same one
  for every car of that build.

We cite requirement identifiers and paraphrase what they oblige rather than quoting; the rule is
[`docs/normative-basis.md`](../normative-basis.md). All `-20` identifiers, no document caveat.

**And a second, smaller half in the same function.** `security.py:171`, the server side, verifies the
client against `CertPath.OEM_ROOT_PEM` and nothing else. `[V2G20-2331]` permits a vehicle certificate
chain anchored at an OEM root **or a V2G root**, and `[V2G20-2401]`/`[V2G20-2402]` have an SECC advertise
V2G and/or OEM roots. A conformant EV with a V2G-rooted vehicle certificate cannot authenticate to your
station — and that is exactly the shape your own downstream fork mints, which is how we noticed.

## The fix already exists, in a fork of this code

`EVerest/ext-switchev-iso15118` @ **`26f7988`** carries it. Their `security.py` client half:

```python
ssl_context.load_cert_chain(
    certfile=os.path.join(get_PKI_PATH(), CertPath.VEHICLE_CERT_CHAIN_PEM),
    keyfile=os.path.join(get_PKI_PATH(), KeyPath.VEHICLE_LEAF_PEM),
    password=load_priv_key_pass(os.path.join(
        get_PKI_PATH(), KeyPasswordPath.VEHICLE_LEAF_KEY_PASSWORD)),
)
```

with a full path set beside the OEM one — `VEHICLE_LEAF_DER`, `VEHICLE_SUB_CA2_DER`,
`VEHICLE_SUB_CA1_DER`, `VEHICLE_CERT_CHAIN_PEM`, the matching keys and password file — and
`VEHICLE_ROOT_PEM` pointing at the V2G root, which their server half then verifies against
(`:174`). Their `create_certs.sh` mints the branch, signing `VEHICLE_SUB_CA1` with the V2G root, and the
leaf it produces is `CN=WMIV1234567890ABCDEX, O=Pionix` — a WMI-derived EVCCID as Common Name, which is
`[V2G20-2598]` done properly.

So: **the credential class, the paths, the PKI branch and the verification anchor are all already
written against this codebase**, by a fork that tracks it. We are not proposing a design.

## Suggested direction

1. **Port the fork's `VEHICLE_*` entries** into `CertPath`, `KeyPath` and `KeyPasswordPath`, and switch
   `get_ssl_context`'s client half to them. That is the whole functional change.
2. **Add the vehicle branch to `create_certs.sh`** — sub-CA1, sub-CA2 and a leaf whose CN is an EVCCID
   per `[V2G20-2598]`. The fork's version is a diff away.
3. **Verify against V2G root as well as OEM root** on the server side (`:171`), since `[V2G20-2331]`
   permits both anchors and `[V2G20-2401]` says a station advertises both.
4. **Leave the provisioning certificate exactly where it is** for `CertificateInstallationReq`. Nothing
   about that use is wrong; the report is only that it should not be doing two jobs.

## Not part of this

- **Your PKI's curves.** `prime256v1` throughout, outside the `-20` profile, is
  [`josev-iso20-pki-curve.md`](josev-iso20-pki-curve.md) and comes from the same script. Fixing the
  vehicle branch is a good moment to read that one, and they are still two issues.
- **`-2`.** ISO 15118-2 TLS is unilateral and your EVCC presents no client certificate there, which is
  correct. This is a `-20` report only; `ENABLE_TLS_1_3` is the switch that reaches it.
- **Whether anything currently breaks.** Nothing we ran did. A station that trusts your OEM root accepts
  this leaf, and ours did. The cost is that the credential does not identify the vehicle and cannot
  satisfy `[V2G20-2677]` meaningfully — not that sessions fail today.
- **The single `certfile` for both roles being deliberate.** If it is — a test-fixture simplification
  you would rather keep — say so and this narrows to *"then `create_certs.sh` should mint a vehicle
  certificate and the docs should say which is which"* rather than closing.

---

## Before sending

- [x] **Read it on the tree you are filing against**, not on a fork or an install layout.
      `security.py:206`, `:208-212`, `:171`, `:1445`; `evcc/states/iso15118_20_states.py:198`;
      `evcc/states/iso15118_2_states.py:423`; `secc/states/iso15118_2_states.py:794` — all read on
      `d645255` on 2026-08-15, and re-resolved through
      [`tools/reports-audit/check_citations.py`](../../tools/reports-audit/README.md), which reports each
      of them **ambiguous between upstream and the fork** because the paths are shared. That is the tool
      working: read the `[josev]` line, not the `[josev-fork]` one.
- [x] **Establish the absence, do not infer it.** `grep -c VEHICLE shared/security.py` → 0; `vehicle_cert`
      / `VEHICLE_LEAF` / `vehicleCert` → no match under `iso15118/`. An absence claim needs the command
      that produced it.
- [x] **Check the fork actually does it**, rather than assuming a downstream is ahead. Read at
      `26f7988`, including its `create_certs.sh` and the leaf it mints.
- [x] **Corroborate on the wire.** The leaf and its anchor were validated in a live TLS 1.3 session, not
      only read off disk.
- [ ] **Lead with the two requirement numbers**, `[V2G20-2339]` and `[V2G20-2342]`. Two *shall*s, two
      credentials, one leaf — that is the report in one line.
- [ ] **Point at the fork early.** It converts *"you should build this"* into *"this is already written
      against your code"*, which is a different conversation.
- [ ] **Say plainly that nothing observably breaks.** Overstating the consequence is the fastest way to
      lose a source-only finding, and `[V2G20-2677]` is enough on its own.
- [ ] **Ask whether the single leaf was deliberate.** A test-PKI simplification is a legitimate answer
      and changes the ask rather than the finding.
- [ ] **File one issue**, and mention `josev-iso20-pki-curve.md` if that one is not already open — same
      script, same reviewer.
- [ ] **Post under your own name, in your own words.**
