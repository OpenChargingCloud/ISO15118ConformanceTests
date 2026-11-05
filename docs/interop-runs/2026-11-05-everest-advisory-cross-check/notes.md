# 2026-11-05 — all twenty-four EVerest reports against their advisory database and the 2026.02.2 release

**What this run is:** not a session on the wire, and not a re-read of their source either. A read of the
two surfaces this repository had never looked at: the **34 security advisories** published alongside
everest-core **2026.02.2** (released the same day) and the **72** advisories their organisation has
published in total — among them a whole **Quarkslab audit sponsored by OSTIF**. It was triggered by one
question: *are our EVerest findings mentioned and fixed in 2026.02.2?*

**The answer as asked: not one of them is mentioned.** The release credits 25 contributors; the batch
credits 14 named reporters (`realsung`, `Mehrrun`, `pavanchow`, `kwlee2025cpp`, `LYHyoung`,
`OsamaMohammed`, `F0rt2`, `kta1kri`, `alx01011`, `ByungwooChoi`, `SebaLukas`, `hikinggrass`, `djchhp`,
plus anonymous entries). None of them is ours, for the reason the 2026-11-04 re-sweep already recorded:
nothing was ever sent. The inverse held too — `grep -rn 'CVE-20\|GHSA-\|Quarkslab\|OSTIF' docs/` was
**empty** before this run.

**But three of our findings are fixed in that release, two of them because somebody else reported
them** — and one of ours turns out to be the regression left by the fix for a CVE.

## Method, so the next reader does not re-derive it

```bash
gh api "repos/EVerest/EVerest/security-advisories?per_page=100" --paginate   # 72, with credits,
                                                                            # ranges, patched versions
gh api "repos/EVerest/everest-core/contents/<path>?ref=2026.02.2"            # the release tree itself
gh api graphql -f query='{repository(...){object(expression:"main"){... on Commit{
          blame(path:"..."){ranges{startingLine endingLine commit{oid messageHeadline}}}}}}}'
```

Three things made the difference between this and the 2026-11-04 re-sweep, which read `main` only:

1. **The advisory list is one call** and carries `credits`, `vulnerable_version_range` and
   `patched_versions` per entry. No page scraping, and the release page's prose is not needed.
2. **Every claim here was read in the `2026.02.2` tag**, not in the release notes. The notes say
   *"contract certificate checked before use"*; the tag says which line moved.
3. **Blame answers provenance.** Where a defect of ours sits in code that an advisory's fix touched,
   `git blame` on the current line says whether their patch created it.

## Ours, fixed in the release — and who got the credit

| our report | the advisory | in 2026.02.2 |
|---|---|---|
| [`everest-evsev2g-paymentdetails-crash`](../../reports/everest-evsev2g-paymentdetails-crash.md) | **GHSA-qg7p-grpm-cg3f**, medium 6.5, `Mehrrun` | fixed: `if (err != 0)` now precedes `getEmaidFromContractCert`, and the `assert(conn->pubkey)` is an `if` |
| [`everest-isomux`](../../reports/everest-isomux.md) issue 3 | **GHSA-9cfv-mjv8-x4fw** (7.1, `pavanchow`) and **GHSA-qvxq-v3r8-rmmv** (7.1, `kwlee2025cpp`) | fixed: the header read tests `rv < 0`, treats `rv == 0` as a peer close, rejects a short read, and adds overflow and buffer-size guards |
| [`everest-loop-shutdown`](../../reports/everest-loop-shutdown.md), the TLS-accept trigger | — | fixed: `connection_ssl.cpp:480` returns instead of throwing, with the comment *"Throwing here would end the controller loop, only tear down connection instead."* |

The first two are the measurable cost of holding a report: both were reported by other people between
August and November, out of a source reading in at least one case (`Mehrrun`'s PoC section says *"I did
not send this on the wire"*), while ours sat finished with a live station behind it.

**The PaymentDetails backport question, asked this morning, is closed by the same batch.** 2026.02.2
carries the fix, so *"still live in every release"* was true for about six hours. What is left of that
report is an **amendment to their advisory**, which is its own document now:
[`everest-evsev2g-paymentdetails-advisory-amendment`](../../reports/everest-evsev2g-paymentdetails-advisory-amendment.md).

## The one that changed what it is: `session-id-zero` is an incomplete fix of CVE-2025-68140

Quarkslab's audit reported the **opposite direction of the same check**:
[CVE-2025-68140](https://github.com/EVerest/EVerest/security/advisories/GHSA-w385-3jwp-x47x),
*"Null session ID bypasses session ID verification"* — with no session registered the stored id
defaults to 0, so a request carrying 0 matches it. The advisory quotes the pre-fix conjunction, which
has no zero term in it.

The fix is [PR #1403](https://github.com/EVerest/everest-core/pull/1403), commit `7e3b03dc`
(2025-10-08, *"EvseV2G: ensure that session id is != 0"*), and its diff **adds**
`received_session_id != 0`:

```c
/* [V2G2-460]: check whether the session id matches the expected one of the active session */
*v2g_response_code =
    ((conn->ctx->current_v2g_msg != V2G_SESSION_SETUP_MSG) && (conn->ctx->ev_v2g_data.received_session_id != 0) &&
     (conn->ctx->evse_v2g_data.session_id != conn->ctx->ev_v2g_data.received_session_id))
        ? iso2_responseCodeType_FAILED_UnknownSession
        : *v2g_response_code;
```

That is the defect our report describes, verbatim: a zero id no longer *matches the unset default*, it
**skips the check**, mid-session included. Present at `iso_server.cpp:107` on `main` (`01f36de1`) and
at `:98` in 2026.02.2. Blame confirms the provenance — the only later commit touching the block,
`7c43143a` (#1484), merely moved it to prioritise `SequenceError` over `UnknownSession`.

So the report goes out as an advisory referencing CVE-2025-68140, not as a bug report. Their own batch
has the wording precedent: **GHSA-fc82-8hg4-wwj5** is filed as *"incomplete fix of CVE-2025-68133"*.

## The asymmetry that costs the most: the same RNG defect, one site fixed at High

`realsung` reported `EvseSlac`'s session NMK — one `std::random_device` read seeding `std::mt19937`,
all 16 bytes drawn from it — as **GHSA-f35j-hqfr-4p6p**, CVSS **7.1**, CWE-331/338, with the 2^32 seed
recovered by exhaustive search. 2026.02.2 ships it as *"EvseSlac: full-entropy session NMK"*.

Our [`everest-d20-rng-entropy`](../../reports/everest-d20-rng-entropy.md) is the identical
construction in `lib/everest/iso15118`, for the 64-bit SessionID and the 128-bit `GenChallenge`. In the
2026.02.2 tree, `include/iso15118/detail/` has **no** `random.hpp` and `d20/session.cpp` still reads
`std::random_device rd; std::mt19937 generator(rd());` at **three** sites (lines 87, 100, 109). The
`main`-only CSPRNG helper the re-sweep called the fix is not in the release at all.

One fundamental, two sites, one reported and closed as a High, one held and still shipping.

## Four of the ten the re-sweep called "fixed" are not fixed in any release

*Fixed* meant *fixed on `main`* throughout that note. Checked in the tag:

| report | on `main` | in 2026.02.2 |
|---|---|---|
| `d20-rng-entropy` | fixed | **live** — no `random.hpp`, `mt19937` ×3 |
| `evsev2g-certificate-update` | fixed | **live** — still `// TODO: implement CertificateUpdate handling` |
| `evsev2g-session-log-responses` | fixed (#2566) | **live** — still `conn->payload_len`, no `exi_bitstream_get_length` |
| `loop-shutdown` | fixed | **half** — TLS accept contained; `poll_manager.poll()` still `break`s out of `loop()`, and the catch takes `std::runtime_error` only |
| `paymentdetails-crash` | fixed | **fixed**, and released |
| `isomux` issue 3 | fixed | **fixed**, and released |

The `-20` ones have a structural reason: `lib/everest/iso15118` in the 2026.02 line is the pre-#2485
tree, so the whole `d2/` rewrite and everything that came with it is `main`-only. The five remaining
reports of the ten (`d20-ac-namespace`, `iso20-ac-contactor-latch`, `evse-security-ocsp-dropped`,
`d20-ocsp-absent`, `d20-eim-rejection`) were not re-checked against the release here; for them *fixed*
still means *fixed on `main`*, and saying so is the point of this note.

## What they have already refused in public — which is an input to two of our reports

- **CVE-2025-68139** (`terminate_connection_on_failed_response` defaults to `false`): the advisory's
  own mitigation section says the default *"will stay at the current value"*, because forcing it
  *"can trigger errors in vehicle ECUs requiring ECU resets"*. That is the same shape as client-auth
  issue 1's `enforce_tls_1_3 = false`: a default they have thought about and defended. Any report of
  ours that turns on a default has to answer that argument rather than discover it.
- **CVE-2026-24003** (sequence-state validation bypass, `EvseV2G`, range *all*, **patched: none**):
  still open, and built on 68139. Adjacent to our `session-id-zero` and to the withdrawn
  `renegotiation-cablecheck`.

## What this changes about how we work

1. **The counterparty's advisory database is a surface of its own, and one call.** It answers *is this
   new*, *what document should this be*, and *which arguments have they already accepted or refused*.
   It is now part of the report shape in [`reports/README.md`](../../reports/README.md).
2. **"Fixed" needs an axis: fixed where.** For a stale draft of ours the distinction is free. For a
   remotely-triggerable pre-auth defect it is the difference between *nothing to do* and *ask for a
   backport* — and it ran both ways today: one report was wrongly filed as live-in-every-release, four
   were wrongly filed as fixed.
3. **Holding a finished report has a measured price now.** Two of ours were reported by other people
   while we held them. Neither of those reporters had a running station; one says so in the advisory.
4. **An advisory's text outlives its number.** GHSA-qg7p-grpm-cg3f has no CVE and no global-database
   entry yet, so its *"stops charging"* is still editable — and will not be once it propagates.

## Artefacts

Nothing binary to keep. The advisory dump is reproducible with the first command above; everything
else in this note is a path plus a ref, and each report's banner now names what was checked in which
tree.
