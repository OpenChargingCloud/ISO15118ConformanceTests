# `reports-audit` — keeping the drafts in `docs/reports/` honest

Two scripts for the two mechanical items that recur in every *Before sending* checklist. Neither
decides anything; both narrow a reading job down to the handful of places worth reading.

First use: [`2026-08-11-reports-upstream-audit`](../../docs/interop-runs/2026-08-11-reports-upstream-audit/notes.md).

## `check_citations.py` — do the line numbers still point where they say

> *"Re-read the citations against the tree before posting."*

Resolves every `` `file.cpp:123` `` in `docs/reports/` — **recursively**, so a report split into
postable issues in a subdirectory of its own is covered — against the counterparty checkouts and
prints the source line it lands on, so the claim beside it can be compared to the code. Unresolvable
paths and line numbers past the end of a file are flagged.

The recursion was added on 2026-08-12 after a re-run found the three
[`everest-d20-client-auth/`](../../docs/reports/everest-d20-client-auth/README.md) issue files
uncovered — eight citations that had gone unchecked for a day because the glob stopped at the top
level. Splitting a report is exactly when its citations get copied, and copied citations are the ones
that drift.

```bash
TREE_EVEREST=~/everest/everest-core \
TREE_EVEREST_GENERATED=~/everest/everest-core/build/generated \
TREE_JOSEV_FORK=~/everest/everest-core/build/_deps/josev-src \
TREE_JOSEV=~/josev-src \
TREE_EDF=~/edf/eVDriveFlow \
TREE_LIBCBV2G=~/libcbv2g \
TREE_TUX=~/tux-evse \
TREE_TUX_NET=~/tux-net \
TREE_V2GDECODER=~/v2gdec/V2Gdecoder \
python3 check_citations.py
```

Unset roots are announced on stderr and their citations come back **unresolved**, never silently
correct — a checkout you forgot to configure must not look like a clean bill of health.

Three roots are not obvious and each cost a false alarm the first time:

- **`TREE_EVEREST_GENERATED`.** `types/*.hpp` are generated from the `types/*.yaml` into `build/`,
  which the tree walk skips. Without this the reports' generated-header citations look out of range.
- **`TREE_JOSEV_FORK`.** Several reports cite upstream Josev *and* EVerest's fork of it, with the
  same paths and different line numbers. Configure both or the fork's citations resolve against
  upstream and appear wrong.
- **`TREE_TUX_NET`.** tux-evse is **several repositories**, not one. `iso15118-network-rs` is a
  sibling of `tux-evse` that cargo pulls by git URL, so it is nowhere inside the `TREE_TUX` checkout
  and no amount of walking that tree will find it:
  ```bash
  git clone https://github.com/tux-evse/iso15118-network-rs.git ~/tux-net/iso15118-network-rs
  ```
  Configure it or [`tux-evse-spin.md`](../../docs/reports/tux-evse-spin.md)'s central citation — the
  `get_data` that cannot tell EOF from an empty read — comes back unresolved. It sat that way from the
  day it was written until 2026-08-13, which is the whole argument for the "unresolved, never silently
  correct" rule above: **the tool did its job, and nobody read the one line it printed.** The citation
  turned out to be exactly right. Note the repo is at `f1ab338` (2024-07-25) and has not moved since,
  so it is a slower-moving tree than the rest and its line numbers are correspondingly stable.

Ambiguity is reported rather than guessed: where a basename exists in more than one tree the
candidates are printed with the line from each. `power_delivery.cpp` exists five times in
everest-core alone.

**One thing it cannot check: which *revision* a citation means.** The checkout is one commit, and a
report may legitimately cite two — [`everest-loop-shutdown`](../../docs/reports/everest-loop-shutdown.md)
quotes 2026.02.1 for what it measured and `main` for what survives, and the checker resolves both
against whichever tree is configured. Reports that do this label every citation with its tree; when
the printed line does not match the claim, check the label before believing the tool.

**Three `OUT OF RANGE` lines are expected with the roots above, and all three are correct.** With
`TREE_EVEREST` on the release, `tbd_controller.cpp` is 188 lines; on `main` (`ebcd36d`) it is 388, and
two reports cite the longer one:

| report | citation | at `main` |
|---|---|---|
| [`everest-loop-shutdown`](../../docs/reports/everest-loop-shutdown.md) | `tbd_controller.cpp:320-321` | `handle_sdp_server_input()` and its untried `get_peer_request()` — as claimed |
| [`everest-d20-ocsp-absent`](../../docs/reports/everest-d20-ocsp-absent.md) | `tbd_controller.cpp:357-367` | the connection lambda building `ConnectionSSL` on `Security::TLS` — as claimed |

Both were re-verified against `ebcd36d` on 2026-08-13, and both reports already say *"on `main`"* in
the surrounding prose. **Do not "fix" them against the release tree.** Configuring a second everest
root would silence the warning and cost more than it saves: every basename would then match twice, so
roughly 150 clean citations would turn ambiguous, and a local `main` checkout drifts from the real one
— a citation resolving against a stale clone is the `--lib` trap below wearing a different hat.

## `triage_citations.py` — which of those lines need a human

`check_citations.py` prints the line each citation lands on; whether that line *says what the report
claims* is still reading. This narrows the reading: it takes the identifiers quoted beside each
citation and looks for them on the cited line, then within ±15 lines, and writes one TSV row per
citation — `MATCH`, `DRIFT` (with the offset), `MISS`, `NOTOKEN`, `AMBIGUOUS`, `OUT_OF_RANGE`,
`UNRESOLVED`. Same `TREE_*` variables; it imports `check_citations.py`'s index and resolver.

```bash
TREE_EVEREST=~/everest/everest-core … python3 triage_citations.py > t_0221.tsv
TREE_EVEREST=~/everest/core-ebcd    … python3 triage_citations.py > t_ebcd.tsv   # August main
```

**Run it once per tree a citation can mean and join the runs on (report, citation).** The reports
label their citations `[tag]`, `[main]`, *"2026.02.1 line numbers"* or a commit, so one tree can never
verify all of them. A citation is verified when it `MATCH`es on the tree its report names; what
matches on no tree is the list to read. On 2026-11-05 four everest-core trees turned 294 citations into
107 to read, and those held the two that were wrong
([reports audit](../../docs/interop-runs/2026-11-05-reports-audit/notes.md)).

**A verdict decides what to read first, never what to conclude.** The identifiers come from the
surrounding paragraph, which often quotes the next citation's subject; a checklist line that lists a
dozen citations gives all of them the same tokens; a log quote carries the compiler's `__LINE__` and
no identifier. Most `MISS` rows are correct citations — and a correct-looking `MATCH` can still sit on
the wrong branch of an `if`, which is exactly what one of the two errors did.

**Check the tree before trusting it.** Before resolving anything, run `git status` in every root: a
rig tree carries probes and patches, and a patched file shifts every line below the patch. And verify
the commit a new worktree actually landed on — a failed SHA lookup that falls back to `HEAD` gives you
a tree that is not the one you asked for and looks exactly like it.

## `check_upstream.py` — has anybody fixed it since we wrote it down

> *"Check whether `main` has moved."* — and whether the release has.

Fetches each cited file at a ref and tests the marker that makes the defect true. No checkout, no
build, network only.

```bash
python3 check_upstream.py                    # EVerest/everest-core @ main -- the development tree
python3 check_upstream.py --ref 2026.02.2    # ... @ the newest release -- what anyone can install
python3 check_upstream.py --ref 2026.02.1    # ... @ the tree the findings were measured on -- the control
python3 check_upstream.py --lib              # EVerest/libiso15118 @ main -- history only, see below
```

**Run it on three trees and read them as columns.** A fix on `main` is not a fix anyone has: on
2026-11-05 the pair `main` / newest release turned the re-sweep's *"ten fixed"* into *"one fixed in a
release"*. The third tree is the **control** — the release most findings were measured on. Every
marker must fire there; one that does not is a broken marker, not a fixed defect, and that is how the
`client-auth` §1 entry was caught pointing at the wrong file
([reports audit](../../docs/interop-runs/2026-11-05-reports-audit/notes.md)).

**A marker must not survive its own fix.** Three entries did, and reported `STILL PRESENT` on `main`
for defects fixed there: the PaymentDetails crash (the fix *reorders* two lines that both survive),
the OCSP conversion (the marker was the function's own signature) and IsoMux §3 (the log line
survives; what changed is the `return` after it). Entries can therefore carry a **predicate** —
`fn(get) -> (present, detail)` — instead of a substring: an ordering check, a function-body check, a
two-location check for code that moved between trees, and, for `certificate-update`, a test for
whether the handler writes a `ResponseCode` at all. That last one is why the checker now disagrees
with the 2026-11-04 re-sweep and is right to: the init call the re-sweep read as the fix resets one
optional flag.

**`--lib` does not report status.** Standalone `EVerest/libiso15118` is **not maintained**; the live
code is everest-core's vendored `lib/everest/iso15118/`. The mirror answers, has a `main`, and returns
the files you ask for, so a `STILL PRESENT` from it looks exactly like a live finding and is not one —
it means nobody has touched that file since 2025-11-25. This cost a wrong conclusion on 2026-08-11:
three findings fixed in everest-core still show as defects there, and the audit briefly recommended
filing all three against the mirror. Use `--lib` to see how far behind it is, never to decide whether
something is live.

Reading the output:

- **`STILL PRESENT`** — the draft is current. File it.
- **`LOOK AGAIN`** — the marker moved. That is a **signal, not a verdict**: go and read the file. It
  means fixed, refactored, or relocated, and only reading tells you which. All three outcomes have
  happened here.

The marker set is deliberately over-specific — a substring narrow enough to break when the code
around it changes. A marker broad enough to survive a refactor (a function name, a struct name)
reports `STILL PRESENT` for code that has been rewritten underneath it, which is how the AC-namespace
fix was nearly missed: the line the report quotes survives its own fix unchanged, and only the `if`
that was wrapped around it is new. That entry is therefore inverted — its marker names the *fix*.

Both scripts are worth re-running before any batch of filings goes out. The 2026-08-11 pass found
three findings already fixed in one tree and still open in another, which moved where they get filed.
