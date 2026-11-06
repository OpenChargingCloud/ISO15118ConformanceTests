# 2026-11-05 — reports audit: every finding against `main`, the newest release, and the advisory databases

**What this run is:** the mechanical half of an audit of all forty-eight drafted issues, network only,
run the same evening as the [advisory cross-check](../2026-11-05-everest-advisory-cross-check/notes.md).
It covers **liveness** — for EVerest on three trees at once — and the two axes that day taught us to
check for every counterparty: **the newest release** and **the published advisory database**. It does
**not** re-resolve the `file:line` citations in the report bodies; that half needs the rig's checkouts
and is the open item at the end.

The headline: the checker, once repaired, disagreed with the 2026-11-04 re-sweep **twice and was right
both times**, and disagreed with banners written earlier the same day **three times** — all three of
those ours.

## The checker had to be fixed before it could be believed

[`tools/reports-audit/check_upstream.py`](../../../tools/reports-audit/check_upstream.py) fetches each
cited file and tests a marker. Run as it stood against `main`, it reported **STILL PRESENT** for three
findings that are fixed there:

| finding | why the marker survived its own fix |
|---|---|
| `paymentdetails-crash` | the fix **reorders** two lines that both still exist; a substring cannot see order |
| `evse-security-ocsp-dropped` | the marker was the conversion function's **signature**, which the fix leaves alone |
| `isomux` §3 | the marker was the **log line**, which survives; what changed is the `return` after it |

And it reported **LOOK AGAIN** on both releases for `client-auth` §1, which is live there — the
mechanism lives in `connection_ssl.cpp`'s `client_hello_cb` in the 2026.02 line and moved to
`tls.cpp` on `main`, and the marker only knew the new home.

Changes, all in the checker and its README: `--ref` (any branch or tag); entries may carry a
**predicate** instead of a substring, and five now do — an ordering check for PaymentDetails, a
function-body check for the OCSP conversion, a *"what follows the log line"* check for IsoMux §3, a
two-location check for client-auth §1 and the AC-namespace gate, and a **`ResponseCode` write** check
for `certificate-update` (below). IsoMux's old combined *"§1/§2"* entry is §2 only now; §1 is a
property of a return expression and is checked by reading.

## Three trees, read together

`2026.02.1` is where nearly every finding was measured, so it is the **control**: every marker must
fire there, and every one does.

| finding | 2026.02.1 | 2026.02.2 | `main` |
|---|---|---|---|
| `d20-sequence-timeout` | present | present | present |
| `evsev2g-session-id-zero` | present | present | present |
| `evsev2g-certificate-update` | present | present | **present** |
| `evsev2g-metering-chain` (receipt back) | present | present | present |
| `evsev2g-metering-chain` (signature out) | present | present | present |
| `evsev2g-paymentdetails-crash` | present | **fixed** | fixed |
| `evsev2g-session-log-responses` | present | present | fixed |
| `evse-security-ocsp-dropped` | present | present | fixed |
| `isomux` §2 | present | present | **present** |
| `isomux` §3 | present | **fixed** | fixed |
| `iso20-ac-contactor-latch` | present | present | fixed |
| `loop-shutdown` (TLS-accept trigger only) | present | **fixed** | fixed |
| `d20-ac-namespace` | present | present | fixed |
| `d20-eim-rejection` *(entry added 2026-11-06)* | present | present | **present** |
| `d20-meter-info` | present | present | present |
| `d20-trust-anchor` | present | present | present |
| `d20-rng-entropy` | present | present | fixed |
| `d20-ocsp-absent` | present | present | fixed |
| `client-auth` §1 | present | present | present |
| `client-auth` §2 | present | present | present |
| *totals* | *21 / 0* | *18 / 3* | *12 / 9* |

The totals include one row not shown, `evsev2g-renegotiation-cablecheck`, present on all three — which
is correct, because that report is **withdrawn**: their behaviour is right and ours was not, so the
marker tracks a behaviour that should stay.

**Where the cross-check had read a file by hand, the release column agrees — eleven of eleven.** Claims
made that day one file at a time are now reproduced by one command; the two hand-read release claims
without a marker, `d20-eim-rejection` and `client-auth` issue 3, stayed hand-read — and one of those two
was wrong in its reasoning (see *Correction, 2026-11-06* below); `d20-eim-rejection` has a marker now. The other eight rows of
the release column are facts nobody had checked against a release before — all eight present. That is
what makes the column usable; the two findings that follow are the reason it was worth building.

## What the checker found that people had got wrong

**`isomux` §2 is live on both trees, and the re-sweep had dissolved it.** *"The cap is gone — no
version pinning is left anywhere in `IsoMux/`"* was true of `IsoMux/` and false of the module:
`connection/tls_connection.cpp:282` sets `config.ciphersuites = ""`, and `lib/everest/tls` turns an
empty TLS 1.3 suite list into `SSL_CTX_set_max_proto_version(ctx, TLS1_2_VERSION)` — `main` even says
so in a comment, `tls.cpp:668-673`. The pin is one library down. A banner written earlier the same day
had then copied the error into `client-auth` issue 1 (*"do not send IsoMux §2 separately"*); withdrawn.

**`certificate-update` is not fixed on `main`, and the re-sweep had retired it.** `main`'s dispatch now
sets `CertificateUpdateRes_isUsed` and calls `init_iso2_CertificateUpdateResType` before the same
`// TODO` handler. The generated init is one line, `RetryCounter_isUsed = 0u;`
(`iso2_msgDefDatatypes.c:376-378`); `init_iso2_BodyType` clears `*_isUsed` flags only. So
`ResponseCode` and all five mandatory elements are still the previous message's bytes — the report's
own claim, made in advance: *"none of the three inits touches its members."* `main` added a fourth.

**Three banners of ours from earlier the same day, corrected:** IsoMux §3's fix was credited to five
header-read checks that **2026.02.1 already had**, line for line — the fix is in the caller,
`v2g_detect_iso20_support()`, which now returns `HandshakeResult::Failed`; `client-auth` issue 1's
third point (the IsoMux error above); and `client-auth` issue 3 claimed `ChainTlsVersion` occurs
nowhere in the release tree, which was checked for two files, not the tree — it now says what was
checked, that `ssl_config_builder.cpp` does not exist there.

## The other five counterparties: release and advisory axes

| repository | published advisories | newest release | HEAD vs. the reports' pin |
|---|---|---|---|
| `SwitchEV/iso15118` | 0 | `0.31.2`, 2025-10-02 | moved; the pin `d645255c` is `0.31.2` **plus one commit** |
| `EVerest/ext-switchev-iso15118` | 0 | `2025.12.0` | moved — but see below |
| `EDF-Lab/eVDriveFlow` | 0 | `v0.1.1`, 2022-02-21 | **= pin** |
| `tux-evse/iso15118-simulator-rs` | 0 | — (tag `0.2`) | **= pin** |
| `tux-evse/iso15118-network-rs` | 0 | — (tag `0.0.1`) | **= pin** |
| `tux-evse/iso15118-encoders-rs` | 0 | — | **= pin** |
| `EVerest/libcbv2g` | 0 | `v0.3.2`, 2025-12-10 | **= pin** (being archived) |
| `EVerest/cbexigen` | 0 | `v1.0.0`, 2026-02-20 | moved |
| `FlUxIuS/V2Gdecoder` | 0 | `v1.3`, 2026-07-28 | `v1.3` **is** HEAD |

**None of the nine has ever published an advisory**, so the axis that changed four EVerest reports is
empty for the others — checked, not assumed. No HEAD moved since the 2026-11-04 re-sweep. Three facts
from this table carry weight:

- **Josev's release is the tree we measured.** The pin is `0.31.2` plus one commit, and there has been
  no release since. So every Josev finding the re-sweep found live at HEAD is also what PyPI ships, and
  a fix at HEAD would reach nobody until a release.
- **EVerest ships our Josev-fork pin exactly.** everest-core **2026.02.2**'s `dependencies.yaml` pins
  `ext-switchev-iso15118` at `26f79889` — the commit the reports cite. Every fork finding is in the
  newest EVerest release byte for byte; `main` pins the fork's HEAD, `1b3ea741e`.
- **`libcbv2g` is read in everest-core's vendored copy, on both trees.** The standalone repository is
  unmoved and being archived. In `lib/everest/cbv2g`:

  | issue | 2026.02.2 | `main` (regenerated 2026-10-20, #3181) |
  |---|---|---|
  | **A** — document codes ordered by type | **live** — `case 1` decodes `ACDP_DisconnectReq` with the `ConnectReqType` decoder | live |
  | **B** — optional element after an optional list | **half** — no cap (`ID=185` loops with an exit), no-items state `ID=184` still drops the field | half, the same |
  | **C** — `minOccurs="2"` loop without exit | **live** — `PulseSequenceOrder`'s state is a `LOOP` and nothing else | fixed |

  Issue C was banner-marked *obsolete* on 2026-11-04; it is a backport ask like the EVerest ones.

## The citations — the second half, done the same night

**294 `file:line` citations in `docs/reports/`; 292 point where they say, two did not, both corrected.**

**The trees.** Every citation was resolved on the WSL rig against the tree it was written for, and
every one of those trees was checked first for local edits that could shift a line:

| root | tree | local edits |
|---|---|---|
| everest-core | `2026.02.1` (`b61bb12b8`) — the `[tag]` / "2026.02.1 line numbers" citations | none |
| everest-core | `main` @ `ebcd36d` (2026-08-11) — the August `[main]` citations | none (new worktree) |
| everest-core | `2026.02.2` (`45dba2c0`) — today's banners | none (new worktree) |
| everest-core | `main` @ `01f36de1` — the 2026-11-04/05 re-reads | none |
| Josev | `d645255c2`; EVerest's fork `26f79889e` | `template.Dockerfile` only |
| eVDriveFlow | `60249c30c` | 26 regenerated test certificates and keys; **no source file** — the five cited EDF ranges are byte-identical to the pinned blob |
| libcbv2g, tux-evse (×2) | the pins | none |
| V2Gdecoder | `v1.1`, fetched for the purpose — the report cites the v1.1 jar | — |

The two `main` worktrees and the `2026.02.2` one live beside `core-main` and `core-rc` in `~/everest/`
and are kept for the next audit. A first attempt at the `ebcd36d` worktree resolved the SHA through an
unauthenticated API call that failed and silently checked out **today's HEAD** instead; it was caught
by the log line and rebuilt from the full SHA. A tree that is not the one you asked for looks exactly
like one that is.

**How the reading job was narrowed.** [`triage_citations.py`](../../../tools/reports-audit/triage_citations.py),
new tonight, takes the identifiers a report quotes near each citation and looks for them at the cited
line, then within ±15 lines. Run once per tree and joined:

| verdict | citations |
|---|---|
| an identifier quoted beside the citation is on the cited line, in one of the four trees | **187** |
| read by hand against the source, because the heuristic could not decide | **107** |
| — of those, correct for the tree their report names | 105 |
| — of those, **wrong** | **2** |

A match in *some* tree is not yet a match in the tree the report names, so the nine citations that
matched only on today's `main` were checked against their labels: seven are labelled `main` or a
2026-10/11 commit, and the other two — `iso2_msgDefDatatypes.c:418-420` and `power_delivery.hpp:21`,
both in August bodies citing 2026.02.1 — were read on 2026.02.1 and are right there too; the tool had
resolved them to a sibling file.

The 107 were mostly the heuristic's blind spots, not the reports': a checklist line that lists twelve
citations gives all twelve the same tokens; a log quote (*"`openssl_util.cpp:775`: Assertion…"*) has
the compiler's `__LINE__` and no identifier; a table row quotes the *next* column's subject. Six were
accepted on the cited line alone, because the report quotes nothing to compare it with — they sit in
*Before sending* checklists (`client-auth` `config.hpp:26-27` and `:34`, `isomux` `v2g_server.cpp:30`,
`session-log-responses` `ISO15118_chargerImpl.cpp:131` and `v2g_ctx.cpp:317`, `authorization-setup`
`ev_dummy_controller.py:111`), and each lands on a line that fits its neighbours.

**The two that were wrong, both in [`everest-evsev2g-certificate-update`](../../reports/everest-evsev2g-certificate-update.md)**
and both copied from its run note
[`2026-08-16-everest-iso2-cert-update-live`](../2026-08-16-everest-iso2-cert-update-live/notes.md):

- *"`WAIT_FOR_PAYMENTDETAILS_CERTINST_CERTUPD` (`iso_server.cpp:948`)"* — line 948 of 2026.02.1 is in
  the **ExternalPayment** branch, which goes to `WAIT_FOR_AUTHORIZATION`. The Contract branch's state
  assignment is **`:942-943`**. The claim was right; the line pointed at the branch that disproves it.
- *"`TODO: Support of the "Update" service` (`charger/ISO15118_chargerImpl.cpp:226`)"* — the comment is
  on **`:227-228`**, and the bare path matches four modules' `charger/ISO15118_chargerImpl.cpp`. Now
  `EvseV2G/charger/ISO15118_chargerImpl.cpp:227-228`.

Both were in the August sweep's *"all 189 verified"*. Neither affects the finding; both would have
been the first thing a maintainer checked.

**What the citations do not say, and this pass does not change:** most EVerest citations name
2026.02.1 or August `main`, and both have moved. They are correct *for the tree they name*, which is
the repository's convention and what a maintainer can open — `git show 2026.02.1:<path>` — but a
report sent today against `main` or 2026.02.2 should re-point its anchors on the day it is posted. The
banners added on 2026-11-05 already carry the current numbers for the lines that decide each finding.

## Correction, 2026-11-06 — the checker's blind spot was ours as well

Working through the pre-send edits the banners asked for, the one for `d20-eim-rejection` — *"the
mechanism paragraph needs re-pointing at the caller"* — turned out to be the error. The cross-check
had verified that release claim by hand: it listed every `call_authorization_response` in
`EvseManager.cpp`, found only `Unknown` and `Accepted`, and concluded the report named the wrong
cause. The report names `evse_managerImpl.cpp`, a different file of the same module, where
`} else if (pnc) {` forwards verdicts for PnC only. That gate is in every tree, the newest `main`
included, so:

- the report is **right as written**, and the banner that said otherwise for a day is corrected;
- the 2026-11-04 re-sweep's *"fixed"* for it is wrong as well — **the fifth** such verdict
  (metering-chain, isomux §4, isomux §2, certificate-update, and this): it credited the receiving
  handler's new branch, which nothing ever calls with an EIM verdict;
- [`check_upstream.py`](../../../tools/reports-audit/check_upstream.py) gains an entry for it, with the
  gate itself as the marker, so the next pass does not depend on someone searching the right file.

The lesson is the one this note already draws, applied to us: **a hand check is a marker too, and it
can look in the wrong file.** The checker would have caught this one if it had had an entry; it did
not, because the claim felt settled.

## The libcbv2g issues, measured on the trees that ship them (2026-11-06)

The three libcbv2g reports had been measured against the archived standalone repository at
`03350be04` and *read* against everest-core's vendored copy. For the pre-send rewrite of issue B the
[probe](../../../tools/cbv2g-defect-probe/README.md) was compiled — same sources, same flags as its
`build.sh` — against `lib/everest/cbv2g` of **2026.02.2** and of **`main`** `01f36de1`, which is after
the 2026-10-20 regeneration:

| | 2026.02.2 | `main` |
|---|---|---|
| **A** — document codes: `ACDP_ConnectRes` / `ACDP_DisconnectReq` | written as 2 / 1 — **live** | 2 / 1 — **live** |
| **B** — field after an empty list | 23 B with and without it — **live** | 23 B with and without it — **live** |
| **C** — the three `minOccurs="2"` cases | all three `-150` — **live** | all three encode — **fixed** |
| *the probe's control in C's part: one container, nothing after it* | *encodes, 28 B* | ***fails, `-150`, 27 B*** |

Every report claim held where the reports said it would, and B's rewrite now cites these numbers
instead of the standalone's.

**The last row is not explained, and is deliberately not a finding yet.** A `WPT_FinePositioningSetupRes`
with exactly one `VendorSpecificDataContainer` and no `LF_SystemSetupData` is schema-valid, encodes on
2026.02.2 and on the standalone, and fails on `main` after the regeneration. That could be a regression
the regeneration introduced while fixing C, or the probe — written against the old generated API —
filling a structure whose meaning changed. Which one is a reading job in the regenerated encoder's
state for that type, and it is open.

## Not done in this pass

- **Findings without a marker.** IsoMux §1 (a return expression), `loop-shutdown`'s mechanism half
  (the `catch` that ends the loop), and every non-EVerest finding, which the 2026-11-04 re-sweep read
  by content; none of those HEADs has moved since.

## What this changes about how we work

1. **A marker must not survive its own fix.** A substring cannot see order, and a function signature
   survives every fix inside the function. Ordering bugs get ordering checks.
2. **Run the checker on three trees, and read the pinned one as the control.** If a marker does not
   fire on the tree where the finding was measured, the marker is wrong, not the finding.
3. **A sweep's "fixed" is a hypothesis until a mechanical check agrees.** Four of the 2026-11-04
   verdicts — metering-chain, isomux §4, isomux §2, certificate-update — would have damaged a report
   that was right. The last two take a one-minute run to see.
