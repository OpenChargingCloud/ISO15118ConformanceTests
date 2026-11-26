# 2026-11-26 — EVerest 2026.10.0 is released: every finding against the release tag

**What this run is:** a static sweep, with no session on the wire. On release day, every EVerest finding
was checked against the **`2026.10.0`** tag (`74845c57`, tagged 2026-11-25), with **`2026.02.2`**
(`45dba2c0`) and **`main`** (`4fa0fa2c`, 2026-11-26) beside it. Two instruments were used:

- [`tools/reports-audit/check_upstream.py --ref <tag>`](../../../tools/reports-audit/check_upstream.py)
  for the twenty-one findings that have a marker.
- A shallow fetch of the three refs, read out of the object database with `git grep` / `git show`, for
  the findings that have no marker. Every check was made by content; the cited lines are this tag's.

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
- `Ev15118` is in the release, and `PyEvJosev` is gone from it entirely.
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
order the fix (#3047) put there. This was not measured on the wire against the release.

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

## The advisory database: nothing new

`EVerest/EVerest` still lists **72** published advisories, all of them already in the
[2026-11-05 cross-check](../2026-11-05-everest-advisory-cross-check/notes.md); none was published later.
GHSA-qg7p-grpm-cg3f is unchanged since publication: no CVE, `updated_at` still 2026-11-05. Its row
*`< 2026.02.0` → patched in `2026.10.0`* now names a released version that does carry the fix. So
**point 2 of our [amendment](../../reports/everest-evsev2g-paymentdetails-advisory-amendment.md) is
void**: it said 2026.10.0 did not exist yet and its only artifact, rc1, had the defect. The consequence
sentence and the CVSS 4.0 vector are untouched by the release, and remain the amendment.

## What this did not do

- **Nothing ran on the wire.** The WSL rig still has `2026.10.0-rc1` (`~/everest/dist-rc`), not the
  release. "Fixed" above means fixed in the source of the tag. For the PaymentDetails crash, that
  confirms a fix already observed working on `main`; for the others it is the first look at a release.
- `isomux` §4, as above.

## Next

- Build `2026.10.0` in the rig and run it: `-2` DC against `Ev15118` reverse, plus the PaymentDetails
  probe as a release control. Roughly nine minutes of build and one session per scenario.
- Rewrite the advisory amendment without its point 2 before it is sent.
- Decide `josev-iso20-evcc-charge-loop-pacing` and `pyevjosev-manifest-services`: they are now
  2026.02-line reports or nothing.
