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
| `d20-meter-info` | present | present | present |
| `d20-trust-anchor` | present | present | present |
| `d20-rng-entropy` | present | present | fixed |
| `d20-ocsp-absent` | present | present | fixed |
| `client-auth` §1 | present | present | present |
| `client-auth` §2 | present | present | present |
| *totals* | *20 / 0* | *17 / 3* | *11 / 9* |

The totals include one row not shown, `evsev2g-renegotiation-cablecheck`, present on all three — which
is correct, because that report is **withdrawn**: their behaviour is right and ours was not, so the
marker tracks a behaviour that should stay.

**Where the cross-check had read a file by hand, the release column agrees — eleven of eleven.** Claims
made that day one file at a time are now reproduced by one command; the two hand-read release claims
without a marker, `d20-eim-rejection` and `client-auth` issue 3, stay hand-read. The other eight rows of
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

## Not done in this pass

- **The citations.** [`check_citations.py`](../../../tools/reports-audit/check_citations.py) resolves
  every `file:line` against local checkouts and prints the line it lands on; its last full run was
  2026-08-11 to -13 on the rig. Several banners now carry 2026.02.2 line numbers, but the citations
  in the report **bodies** were not re-resolved, and many have drifted — the 2026-11-04 re-sweep
  already noted that every EVerest line number is stale. That is a reading job of a few hundred lines
  and needs the trees at the cited revisions.
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
