# 2026-11-04 — the other five counterparties, re-read against their current upstreams

The companion to [`2026-11-04-everest-main-resweep`](../2026-11-04-everest-main-resweep/notes.md),
which found ten of twenty-four EVerest reports obsolete and ended with *"the same sweep is owed to the
other five counterparties."* This is that sweep: twenty report files covering Josev, eVDriveFlow,
tux-evse, libcbv2g and V2Gdecoder.

**The result is the opposite of EVerest's, and that is the finding.** Of the twenty, **one is fixed,
one is half fixed, and eighteen are live and unchanged** — most of them because the repository they
are about has not had a commit since we wrote them. EVerest moved; nobody else did.

**How it was read.** For each counterparty, the cheapest decisive measurement first: the current HEAD
of the default branch against the commit the report pinned. Three of the five had **not moved at all**,
which settles every finding against them without reading a line of code. The two that had moved were
read by content, never by the cited line.

| counterparty | repository | pinned by the reports | HEAD 2026-11-04 | |
|---|---|---|---|---|
| Josev | `SwitchEV/iso15118` | `d645255c` | `1d29f5398` (2026-10-13) | moved |
| Josev (EVerest's fork) | `EVerest/ext-switchev-iso15118` | `26f79889` | `1b3ea741e` (2026-10-05) | moved |
| eVDriveFlow | `EDF-Lab/eVDriveFlow` | `60249c3` | **`60249c30c`** (2023-04-17) | **unmoved** |
| tux-evse | `tux-evse/iso15118-simulator-rs` | `fc51088` | **`fc51088bd`** (2025-03-07) | **unmoved** |
| tux-evse | `tux-evse/iso15118-network-rs` | `f1ab338` | **`f1ab3385b`** (2024-07-25) | **unmoved** |
| tux-evse | `tux-evse/iso15118-encoders-rs` | `fe6c0aa` | **`fe6c0aa9b`** (2025-02-28) | **unmoved** |
| libcbv2g | `EVerest/libcbv2g` | `03350be048b3` | **`03350be04`** (2025-11-10) | **unmoved** |
| libcbv2g's generator | `EVerest/cbexigen` | — | `6b91be009` (2026-10-20) | moved |
| V2Gdecoder | `FlUxIuS/V2Gdecoder` | v1.1 jar | `2ee5bd94d` (2026-07-28) | moved |

---

## Josev — six reports, all six live

`SwitchEV/iso15118` moved from `d645255c` to `1d29f5398`, so every one was re-read by content.

| report | still live because |
|---|---|
| [`josev-iso20-charge-loop-timeout`](../../reports/josev-iso20-charge-loop-timeout.md) | `V2G_SECC_SEQUENCE_TIMEOUT_AC_CL = 0.5` and `_DC_CL = 0.5` are at `iso15118_20/timeouts.py:42,49` — and a grep across the whole `iso15118/` tree returns **only those two definition lines**. Still defined, still referenced nowhere; the SECC still arms the generic 60 s. `_WPT_CL` at `:58` is a third of the same |
| [`josev-iso20-pause-resume`](../../reports/josev-iso20-pause-resume.md) | the `-20` SECC still compares against the **live** connection — `secc/states/iso15118_20_states.py:155`, `== self.comm_session.session_id` — where the `-2` branch at `iso15118_2_states.py:191` compares `ev_session_context.session_id`, the preserved context. The six-line fix the report proposes is still sitting in the file next to it |
| [`josev-iso20-pki-curve`](../../reports/josev-iso20-pki-curve.md) | `shared/pki/create_certs.sh:133` still reads `EC_CURVE=prime256v1  # TODO Check correct version for ISO 15118-20` — the `-2` curve on the `iso-20` branch, with the TODO still attached |
| [`josev-iso20-renegotiation`](../../reports/josev-iso20-renegotiation.md) | `SessionStop.process_message` in the `-20` EVCC still assigns `self.next_state = ServiceDiscovery` and returns with **no `create_next_message(...)` anywhere in the function**, so the framework's own check raises. Moved from `:1934-1940` to `:1161-1170`. *Reading the assignment alone makes this look fixed — it is the defect; the missing call is the finding* |
| [`josev-iso20-vehicle-certificate`](../../reports/josev-iso20-vehicle-certificate.md) | `shared/security.py:208-211` still loads `CertPath.OEM_CERT_CHAIN_PEM` / `KeyPath.OEM_LEAF_PEM` as the EVCC's TLS client credential |
| [`josev-iso20-evcc-charge-loop-pacing`](../../reports/josev-iso20-evcc-charge-loop-pacing.md) | `V2G_EVCC_Sequence_Performance_Time` is still **absent** from `iso15118_20/timeouts.py`. What is there — `AC_CHARGE_LOOP_REQ = 0.5`, `DC_CHARGE_LOOP_REQ = 0.5`, now referenced from six EVCC sites — is the EV's *request timeout*, not its performance time, and 0,5 s is twice the 0,25 s Tables 216/217 give it |

[`pyevjosev-manifest-services`](../../reports/pyevjosev-manifest-services.md) is **live** too, and it is
in everest-core rather than Josev, which is why the EVerest sweep missed it: `modules/EV/PyEvJosev/manifest.yaml:57`
still documents `(DC, DC_BPT, AC, AC_BPT)` — **four of the twelve** accepted values, `MCS` among the
missing, which their own `config-sil-mcs.yaml` uses.

## eVDriveFlow — five reports, all live, and the repository has not been touched since 2023

`EDF-Lab/eVDriveFlow`'s `main` is **`60249c30c`, 2023-04-17** — byte for byte the commit all five
reports pinned. Three and a half years, no commits, no pushes. Nothing was read: there is nothing to
read differently.

That makes `sending-order.md`'s rule for this counterparty — *"a dormant project gets a PR, not an
issue, and it goes last"* — a measurement rather than an impression. All five stand exactly as written:
[`evdriveflow-headless-session`](../../reports/evdriveflow-headless-session.md),
[`evdriveflow-authorization-setup`](../../reports/evdriveflow-authorization-setup.md),
[`evdriveflow-service-discovery-filter`](../../reports/evdriveflow-service-discovery-filter.md),
[`evdriveflow-session-id`](../../reports/evdriveflow-session-id.md),
[`evdriveflow-session-id-entropy`](../../reports/evdriveflow-session-id-entropy.md).

## tux-evse — three reports, all live, all three repositories frozen

Every one of the three repositories the reports pin is at **exactly** the pinned commit:
`iso15118-simulator-rs` `fc51088bd`, `iso15118-network-rs` `f1ab3385b`, `iso15118-encoders-rs`
`fe6c0aa9b`. The newest of the three is 2025-03-07. So
[`tux-evse-tls`](../../reports/tux-evse-tls.md), [`tux-evse-spin`](../../reports/tux-evse-spin.md) and
[`tux-evse-capture-fidelity`](../../reports/tux-evse-capture-fidelity.md) — six issues between them —
are unchanged, including the 200 000-line-per-second log spin.

## libcbv2g — the only fix outside EVerest, and it is not where the report is addressed

This one needs the distinction spelled out, because the answer differs by *which tree you mean*:

- **`EVerest/libcbv2g` has not moved** — `main` is still `03350be04`, the commit the report observed
  against. All three issues are present there, trivially.
- **`EVerest/cbexigen`, the generator, has** — `6b91be009` — and everest-core regenerated its vendored
  `lib/everest/cbv2g` at it on 2026-10-20 (#3181, after #3180). The generated grammars there are
  **different code**, and that is where two of the three findings moved.

Read against everest-core's regenerated copy:

| issue | verdict | evidence |
|---|---|---|
| [**C** — `minOccurs="2"` loop with no exit](../../reports/libcbv2g/issue-c-minoccurs-loop-has-no-exit.md) | **FIXED** | every `LOOP` state in `iso20_WPT_Encoder.c` now carries a second production, and a search for the defect shape — a `LOOP` state whose only production is the loop — returns **nothing**. The three types the report named: `PulseSequenceOrder` is `ID=76; LOOP (PulseSequenceOrder), START (PulseSeparationTime)`, `TxSpecData` is `ID=84; LOOP (TxSpecData), START (TxPackageSpecData), END Element`, `RxSpecData` is `ID=93; LOOP (RxSpecData), END Element` |
| [**B** — an optional element after an optional list](../../reports/libcbv2g/issue-b-mid-sequence-particle-drops-a-field.md) | **half fixed** | the **cap at two is gone**: `ID=182` and `ID=188` are now `LOOP (VendorSpecificDataContainer), START (WPT_LF_DataPackageList), END Element`, which is the report's own "state B" done right, so a third item and the following particle are both reachable. But the **no-items state still drops the field**: `ID=181` is `START (VendorSpecificDataContainer), END Element` with no `START (WPT_LF_DataPackageList)`, so a document with an empty list and a present `WPT_LF_DataPackageList` still cannot be encoded. Half of this report is obsolete and half is not — it cannot be sent as it stands |
| [**A** — document element codes ordered by type name](../../reports/libcbv2g/issue-a-acdp-document-element-order.md) | **live** | `iso20_ACDP_Decoder.c` still dispatches `ConnectReq(0), DisconnectReq(1), ConnectRes(2), DisconnectRes(3)` — grouped by type, because `ACDP_DisconnectReq` points at `ACDP_ConnectReqType`. EXI §8.5.1 sorts by local-name, which puts `ACDP_ConnectRes` at 1 and `ACDP_DisconnectReq` at 2. The two still swap identity |

**And the standalone repository is being retired.** `EVerest/libcbv2g` has an open PR #24 adding a
deprecation notice: *"This repository is now archived… libcbv2g has been integrated into EVerest. All
future development, updates, and issue tracking will continue within EVerest… This standalone
repository is now read-only."* So all three issues belong to **everest-core**, against the regenerated
code and `cbexigen` — not to the repository the report names. This is the second time this shape has
appeared: the same is true of `libiso15118`, where `everest-core`'s `lib/everest/iso15118/` is the tree
that decides whether a finding is live.

## V2Gdecoder — live, and two releases newer than our pin

The reports pin the **v1.1 release jar**; `master` is `2ee5bd94d` and there have been two releases
since — **v1.2** (`6c26c8171`, 2022-06-09) and **v1.3** (`ab2c2ea0f`, 2026-07-28).

That looked like it might have overtaken the finding, because v1.2's subject is *"Fixing a corruption
issue when running out of allocated memory within the HTTP service and parsing corrupted data"* — which
is nearly our words. It did not: that commit changes **`server/WorkerRunnable.java` only, +2/−2**.
Everything else since v1.1 is Dependabot bumping log4j, plus a `pom` fix. `dataprocess.fuzzyExiDecoded`
and `V2Gdecoder.java` are untouched, so
[`v2gdecoder-fuzzy-grammar`](../../reports/v2gdecoder-fuzzy-grammar.md) stands — against a jar that is
now two releases old, which the report should say.

---

## What the two sweeps say together

Twenty-four EVerest reports and twenty for everyone else; **eleven fixed, one half fixed, thirty-two
live.** Every fix but one is EVerest's.

1. **"Unchanged upstream" costs one HEAD comparison to check, and three of five counterparties answer
   it for free.** For eVDriveFlow and tux-evse the pinned commit *is* HEAD, so nine reports were
   settled without reading code. That check belongs in every report's *Before sending*, not in a
   once-a-quarter sweep.
2. **Holding a report is only a risk where the project moves.** The EVerest sweep's lesson — an unsent
   report cannot learn it is obsolete — is real, and it is a lesson about EVerest. For the four dormant
   counterparties the cost of holding was zero, and the reason to send is that nobody else will find
   these.
3. **Ask which tree, not which project.** libcbv2g's three issues have different answers depending on
   whether you mean the repository the report is addressed to or the regenerated copy in everest-core,
   and the repository is being archived. Twice now a counterparty's standalone library has turned out
   to be a mirror of the tree that actually decides.

## Next

- `everest-d20-client-auth` issue 1 and `libcbv2g` issue B both need **rewriting, not re-filing**: each
  is now partly wrong, and sending either as it stands sends a false claim.
- Add *"re-check HEAD against the pinned commit"* to the report template's *Before sending*.
- Re-address the three `libcbv2g` issues to everest-core and `cbexigen`.
- Say in `v2gdecoder-fuzzy-grammar` that v1.3 exists and does not touch the decoder.
- Still owed, from the EVerest sweep: whether the `-2` findings were carried into everest-core's new
  C++ `d2/` path. That is the one piece of real measurement either sweep leaves behind.
