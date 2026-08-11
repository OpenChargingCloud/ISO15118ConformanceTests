# `reports-audit` — keeping the drafts in `docs/reports/` honest

Two scripts for the two mechanical items that recur in every *Before sending* checklist. Neither
decides anything; both narrow a reading job down to the handful of places worth reading.

First use: [`2026-08-11-reports-upstream-audit`](../../docs/interop-runs/2026-08-11-reports-upstream-audit/notes.md).

## `check_citations.py` — do the line numbers still point where they say

> *"Re-read the citations against the tree before posting."*

Resolves every `` `file.cpp:123` `` in `docs/reports/*.md` against the counterparty checkouts and
prints the source line it lands on, so the claim beside it can be compared to the code. Unresolvable
paths and line numbers past the end of a file are flagged.

```bash
TREE_EVEREST=~/everest/everest-core \
TREE_EVEREST_GENERATED=~/everest/everest-core/build/generated \
TREE_JOSEV_FORK=~/everest/everest-core/build/_deps/josev-src \
TREE_JOSEV=~/josev-src \
TREE_EDF=~/edf/eVDriveFlow \
TREE_LIBCBV2G=~/libcbv2g \
TREE_TUX=~/tux-evse \
TREE_V2GDECODER=~/v2gdec/V2Gdecoder \
python3 check_citations.py
```

Unset roots are announced on stderr and their citations come back **unresolved**, never silently
correct — a checkout you forgot to configure must not look like a clean bill of health.

Two roots are not obvious and both cost a false alarm the first time:

- **`TREE_EVEREST_GENERATED`.** `types/*.hpp` are generated from the `types/*.yaml` into `build/`,
  which the tree walk skips. Without this the reports' generated-header citations look out of range.
- **`TREE_JOSEV_FORK`.** Several reports cite upstream Josev *and* EVerest's fork of it, with the
  same paths and different line numbers. Configure both or the fork's citations resolve against
  upstream and appear wrong.

Ambiguity is reported rather than guessed: where a basename exists in more than one tree the
candidates are printed with the line from each. `power_delivery.cpp` exists five times in
everest-core alone.

**One thing it cannot check: which *revision* a citation means.** The checkout is one commit, and a
report may legitimately cite two — [`everest-loop-shutdown`](../../docs/reports/everest-loop-shutdown.md)
quotes 2026.02.1 for what it measured and `main` for what survives, and the checker resolves both
against whichever tree is configured. Reports that do this label every citation with its tree; when
the printed line does not match the claim, check the label before believing the tool.

## `check_upstream.py` — has anybody fixed it since we wrote it down

> *"Check whether `main` has moved."*

Fetches each cited file at the project's default branch and tests the marker that makes the defect
true. No checkout, no build, network only.

```bash
python3 check_upstream.py          # EVerest/everest-core @ main -- the live tree
python3 check_upstream.py --lib    # EVerest/libiso15118 @ main -- history only, see below
```

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
