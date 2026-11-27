# 2026-11-26 — EVerest 2026.10.0 is released: every finding against the release tag

**What this run is:** a static sweep **and a live run** on release day. Every EVerest finding was
checked against the **`2026.10.0`** tag (`74845c57`, tagged 2026-11-25), with **`2026.02.2`**
(`45dba2c0`) and **`main`** (`4fa0fa2c`, 2026-11-26) beside it; then the tag was **built from source in
the WSL rig** and the 2026-11-06 reverse arms and the PaymentDetails crash probe were re-run against it.
Three instruments:

- [`tools/reports-audit/check_upstream.py --ref <tag>`](../../../tools/reports-audit/check_upstream.py)
  for the twenty-one findings that have a marker.
- A shallow fetch of the three refs, read out of the object database with `git grep` / `git show`, for
  the findings that have no marker. Every check was made by content; the cited lines are this tag's.
- The rig: `~/everest/dist-2610` (22 modules, 12 min from a clean worktree), their `Ev15118` EV against
  our SECC over SDP, and their `EvseV2G` station against our PaymentDetails probe. The
  [live section](#live-against-the-release-the-ev-charges-and-the-crash-is-fixed) has it.

**The headline:** for every finding the checker covers, **the release and `main` give the same
verdict**, and the hand-read ones agree wherever both trees were read. So
the picture from the [2026-11-04 re-sweep](../2026-11-04-everest-main-resweep/notes.md) and the
[2026-11-05 audit](../2026-11-05-reports-audit/notes.md) now holds for an installable tree, not only for
`main`:

- **Ten findings are fixed.** Seven of them were not fixed in 2026.02.2: six markers, plus
  `client-auth` §3, whose `ChainTlsVersion` is absent from that tag.
- **Fifteen are still present**: ten with a marker and five read by hand. That count leaves out
  `isomux` §4, which was not re-read, and the withdrawn `evsev2g-renegotiation-cablecheck`, whose
  marker is meant to stay present.
- `Ev15118` is in the release, and `PyEvJosev` is gone from it entirely. On the wire it charges our
  SECC to `SessionStop` in all eight arms, at the same 25,8 ms / 100,8 ms cadence as the PR head.
- **The PaymentDetails crash is fixed on the wire against the release**, not only in review: the probe's
  malformed certificate draws `FAILED_CertChainError` and the station stays up. First time this has been
  observed against a release — `2026.10.0-rc1` aborted on the same bytes on 2026-11-05.
- Nothing new was published in the advisory database.

---

## The checker, three trees

| finding | 2026.02.2 | **2026.10.0** | `main` |
|---|---|---|---|
| `evsev2g-paymentdetails-crash` | fixed | **fixed** | fixed |
| `isomux` §3 | fixed | **fixed** | fixed |
| `loop-shutdown` (TLS-accept trigger) | fixed | **fixed** | fixed |
| `evsev2g-session-log-responses` | present | **fixed** | fixed |
| `evse-security-ocsp-dropped` | present | **fixed** | fixed |
| `iso20-ac-contactor-latch` | present | **fixed** | fixed |
| `d20-ac-namespace` | present | **fixed** | fixed |
| `d20-rng-entropy` | present | **fixed** | fixed |
| `d20-ocsp-absent` | present | **fixed** | fixed |
| `d20-sequence-timeout` | present | present | present |
| `evsev2g-session-id-zero` | present | present | present |
| `evsev2g-certificate-update` | present | present | present |
| `evsev2g-metering-chain` (receipt back) | present | present | present |
| `evsev2g-metering-chain` (signature out) | present | present | present |
| `isomux` §2 | present | present | present |
| `d20-eim-rejection` | present | present | present |
| `d20-meter-info` | present | present | present |
| `d20-trust-anchor` | present | present | present |
| `client-auth` §1 | present | present | present |
| `client-auth` §2 | present | present | present |
| `evsev2g-renegotiation-cablecheck` *(withdrawn, should stay present)* | present | present | present |
| *totals (present / changed)* | *18 / 3* | ***12 / 9*** | *12 / 9* |

**The PaymentDetails crash is now fixed in a release of the 2026.10 line**, not only on `main`. The
predicate checks the order of the parse guard and the first use. `2026.10.0-rc1` aborted on these bytes
on 2026-11-05 ([run](../2026-11-05-everest-rc-paymentdetails-crash/notes.md)); the final tag has the
order the fix (#3047) put there, and the [live probe below](#live-against-the-release-the-ev-charges-and-the-crash-is-fixed)
confirms it on the wire.

## By hand: the findings without a marker

| finding | at `2026.10.0` | where |
|---|---|---|
| `client-auth` §3 (server chain selection) | **fixed**, as on `main` | `lib/everest/iso15118/include/iso15118/config.hpp:38` `enum class ChainTlsVersion`; selection on the negotiated version in `src/iso15118/io/ssl_config_builder.cpp:36-39` |
| `client-auth` §1, the default | **unchanged** | `config.hpp:83` `enforce_tls_1_3{false}`, so the rewrite around the default (2026-11-04) still applies |
| `d20-ac-contactor-edge` | **live** | the flag is still per state entry (`d20/state/power_delivery.hpp:21`). `EvseManager.cpp:1324-1327` still calls `call_contactor_closed(true)` only on the `CPEvent::PowerOn` edge, and `subscribe_ac_close_contactor` (`:493-496`) still only sets a permission. A contactor that is already closed is never reported to a `PowerDelivery` that waits for it. The module documentation now describes the wait (`Evse15118D20/docs/index.rst:477-479`); it does not change the trigger |
| `evsemanager-renegotiation-supply` | **live** | `EvseV2G/iso_server.cpp:1944-1950` resets `evse_processing[PHASE_ISOLATION]` and `evse_isolation_status`, both internal, and publishes nothing |
| `isomux` §1 (`Priority` never read) | **live** | `IsoMux/v2g_server.cpp:163` still returns `iso20 ? Iso20Offered : Iso20NotOffered`. `Priority` appears only in the log line at `:129-131` |
| `isomux` §4 | **not re-read** | the warning path is still in `lib/everest/tls/src/tls.cpp:1402`; its mechanism against the multi-chain `trust_anchor` remains the open item from 2026-11-04 |
| outgoing `MeterSignature`, `-2` C++ path | **live** | the codec can carry it (`message_2/common_types.cpp:118-128`), but no state under `src/iso15118/d2/` ever sets `sig_meter_reading` |
| libcbv2g issue A (ACDP document codes) | **live** | `lib/everest/cbv2g/lib/cbv2g/iso_20/iso20_ACDP_Decoder.c:7051` still decodes document code 1 as `ACDP_DisconnectReq`, with the `ConnectReqType` decoder |
| libcbv2g issue B (empty list drops the next field) | **live** | `iso20_WPT_Encoder.c:9173`, `ID=181; START (VendorSpecificDataContainer), END Element`, still has no way to `WPT_LF_DataPackageList` |

Both vendored codec files are **byte-identical** between the tag and today's `main` (`29a4ef6f`,
`7e350edf`). So the probe measurement of 2026-11-06 carries over to the release without a re-run.

## What the release changes beyond our findings

**`Ev15118` is in, and `PyEvJosev` is out.** [everest-core#3031](https://github.com/EVerest/everest-core/pull/3031)
is part of the release. Every config that used `PyEvJosev` now uses `Ev15118`
(`config-sil-ac-d20-der-iec.yaml`, `config-CB-EVAL-EV.yaml`, …). No path in the tree contains
`josev` any more. Three consequences:

- [`josev-iso20-evcc-charge-loop-pacing`](../../reports/josev-iso20-evcc-charge-loop-pacing.md) is now
  true of the **2026.02 line only**. In 2026.10.0 the ≈532 ms turnaround has no module left to live
  in. The report asked for exactly this decision ("a release-line issue now, or nothing"), and the
  release answered half of it.
- The same holds for [`pyevjosev-manifest-services`](../../reports/pyevjosev-manifest-services.md):
  the manifest it is about does not exist in 2026.10.0.
- The premise of [`josev-is-everests-ev`](../../matrix/josev-is-everests-ev.md) has ended in a
  release, not only in a PR. A `←SECC` run against 2026.10.0 measures EVerest's own EV on cbV2G in both
  directions.

**Our 2026-11-06 change is what a `-2` DC session with their EV needs.** `Ev15118`'s
`ev/d2/state/cable_check.cpp:32-34` accepts `EVSEIsolationStatus` only when it is present and `Valid`
or `Warning`, which is the behaviour measured against the PR head. Our station states `Valid` from
`CableCheck` on since that day
([decision](../2026-11-06-everest-ev15118/notes.md#decided-the-same-day-our-station-says-what-it-found)).
Any earlier build of ours stops after `CableCheck` against every 2026.10.0 config.

**`EvseV2G` and `IsoMux` ship deprecated**, earliest removal in 2027.04.0. Both manifests say so in
their first line. Eight of the still-present findings aim at those two modules, so a fix there buys at
most one release cycle. For each one, the question that matters is whether it lives on in
`Evse15118D20`. Of the `-2` findings, only the outgoing `MeterSignature` does (above, and
[the `d2/` follow-up](../2026-11-04-everest-main-resweep/notes.md#the-d2-follow-up-measured)).

## Live against the release: the EV charges, and the crash is fixed

The tag was built from source (`build-2610.sh`, the PR #3031 build's module list and flags, 22 modules,
12 min) as `~/everest/dist-2610`, and the 2026-11-06 reverse arms were re-run against it, our SECC
unchanged at its committed `Valid` isolation behaviour and its conformant timers. Every arm's turnaround
is from a packet capture, computed by [`v2gtp-turnaround.py`](../../../tools/interop-everest/v2gtp-turnaround.py).

| arm | protocol, mode | transport | result | loops | their EV turnaround, median (min–max) |
|---|---|---|---|---:|---|
| [`ac20-strict`](ac20-strict/) | `-20` AC, Scheduled | plain | ✅ `SessionStop` | 881 | **25,9 ms** (25,6–76,3) |
| [`dc20-strict`](dc20-strict/) | `-20` DC, Scheduled | plain | ✅ | 694 | **25,8 ms** (25,6–26,8) |
| [`ac20-dynamic`](ac20-dynamic/) | `-20` AC, **Dynamic** | plain | ✅ | 922 | **25,7 ms** (25,5–26,7) |
| [`ac20-tls13`](ac20-tls13/) | `-20` AC, Scheduled | **mutual TLS 1.3** | ✅ | — | — (encrypted) |
| [`ac2-plain`](ac2-plain/) | `-2` AC, EIM | plain | ✅ | 237 | **100,9 ms** (100,6–193,8) |
| [`ac2-tls12`](ac2-tls12/) | `-2` AC, EIM | **TLS 1.2** | ✅ | — | — (encrypted) |
| [`dc2-plain`](dc2-plain/), [`…-2`](dc2-plain-2/) | `-2` DC, EIM | plain | ✅ **2 of 2** | 177 | **100,8 ms** (100,6–166,8) |

- **The `-2` DC arm that stopped against the PR head now runs here with no patch.** Our committed
  station states `EVSEIsolationStatus=Valid` from `CableCheck` on, both plain `-2` DC arms reach
  `SessionStop` with 177 `CurrentDemand` pairs each, and the `CableCheckRes` is 19 bytes — the length and
  content of the `CableCheckRes_IsolationValid` cbV2G reference vector. So the 2026-11-06 decision holds
  against the released EV, not only the PR head.
- **The pacing is identical to the PR head**: 25,8 ms for `-20`, 100,8 ms for `-2`, our own responses
  0,2–0,4 ms median. The `#3391` schedule-bound fix between the PR head and the tag (the one EV-side
  commit this release adds over `35d3eda52`) changed none of it.
- **Mutual TLS 1.3 over `-20`**: `TLS_AES_256_GCM_SHA384`, their EV presenting `CN=WMIV1234567890ABCDEX`
  (issuer `VehicleSubCA2`), our station serving the build's own `SECCCert`. **TLS 1.2 over `-2`**:
  `TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256`, no client certificate. Both as on the PR head.

**The PaymentDetails crash probe: fixed on the wire.** The same three-arm probe as the 2026-11-05 rc1/main
A/B, against the release's `EvseV2G` over `-2` TLS 1.2 Plug & Charge
([`paymentdetails-probe/`](paymentdetails-probe/)):

| arm | bytes | the release answers | the station |
|---|---|---|---|
| control | a well-formed, untrusted contract cert | `ResponseCode = OK` reaching PaymentDetails | up |
| probe | 64 unparseable bytes as the contract cert | **`FAILED_CertChainError`** | **up** |
| liveness | the control again | `OK` | up |

Where `2026.10.0-rc1` aborted (`signal=6`, manager gone — [rc run](../2026-11-05-everest-rc-paymentdetails-crash/notes.md)),
the release parses the bytes, refuses them with the response code the fix intends, and keeps serving. The
manager is still alive after the probe. This is the first time the fix has been observed working on the
wire against a *release*; 2026-11-05 observed it on `main` and watched rc1 crash.

*(The release ships no `PyEvJosev`, so the idle car in the crash-probe config and the reverse arms is
`Ev15118` on `lo` — the same module under test, parked. `config-dc2-pnc-crashprobe-2610.yaml` carries
that one change from the rc1 config.)*

## The advisory database: nothing new

`EVerest/EVerest` still lists **72** published advisories, all of them already in the
[2026-11-05 cross-check](../2026-11-05-everest-advisory-cross-check/notes.md); none was published later.
GHSA-qg7p-grpm-cg3f is unchanged since publication: no CVE, `updated_at` still 2026-11-05. Its row
*`< 2026.02.0` → patched in `2026.10.0`* now names a released version that does carry the fix. So
**point 2 of our [amendment](../../reports/everest-evsev2g-paymentdetails-advisory-amendment.md) is
void**: it said 2026.10.0 did not exist yet and its only artifact, rc1, had the defect. The consequence
sentence and the CVSS 4.0 vector are untouched by the release, and remain the amendment.

## What this did not do

- **Only two findings were exercised on the wire** — the PaymentDetails crash and, through the reverse
  arms, the isolation-status interaction. "Fixed" and "present" for the other twenty-one mean fixed or
  present in the source of the tag; the live run did not drive the thirteen still-present findings to
  reproduce them, which would be a session each against the release.
- `isomux` §4 was not re-read; its mechanism against the multi-chain `trust_anchor` config is the open
  item carried from 2026-11-04.

## Next

- Rewrite the advisory amendment without its point 2 before it is sent. **Done 2026-11-27**: three
  asks, and the advisory re-read the same day, unchanged since publication.
- Decide `josev-iso20-evcc-charge-loop-pacing` and `pyevjosev-manifest-services`: they are now
  2026.02-line reports or nothing.
- If any of the thirteen still-present findings is to be filed, a live reproduction against the release
  is now one `dist-2610` session away. **Done 2026-11-27**: every one a session can reach reproduces,
  two with different numbers ([run](../2026-11-27-everest-2026.10.0-findings-live/notes.md)).
