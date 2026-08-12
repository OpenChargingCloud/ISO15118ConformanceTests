#!/usr/bin/env python3
"""Re-check every `file:line` citation in docs/reports/ against the counterparty trees.

Every draft in docs/reports/ carries a *Before sending* item that says to re-read the
citations against the tree rather than against the draft, because a line number that has
drifted is the fastest way to have a real finding dismissed. This does the mechanical half:
resolve each citation, print the source line it lands on, and flag the ones that cannot be
resolved or fall off the end of the file. Whether the line still *says what the report
claims* is a reading job and stays one.

Walks docs/reports/ recursively, so a report split into postable issues in a subdirectory of
its own is covered too.

Point it at wherever the counterparty checkouts live:

    TREE_EVEREST=~/everest/everest-core TREE_JOSEV=~/josev-src … python3 check_citations.py

Unset roots are skipped with a warning; citations into them come back unresolved rather
than silently correct.
"""
import glob
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPORTS = os.environ.get("REPORTS_DIR", os.path.join(HERE, "..", "..", "docs", "reports"))

# name -> environment variable holding the checkout path
ROOT_VARS = {
    "everest": "TREE_EVEREST",
    "everest-gen": "TREE_EVEREST_GENERATED",   # build/generated — the generated type headers
    "josev-fork": "TREE_JOSEV_FORK",           # EVerest/ext-switchev-iso15118
    "josev": "TREE_JOSEV",                     # SwitchEV/iso15118
    "edf": "TREE_EDF",
    "libcbv2g": "TREE_LIBCBV2G",
    "tux": "TREE_TUX",
    "v2gdecoder": "TREE_V2GDECODER",
}

SKIP_DIRS = {".git", "build", "node_modules", "__pycache__", ".venv", "target"}

EXT = r"(?:cpp|hpp|h|c|py|sh|yaml|yml|json|rs|java|cs|txt)"
CITE = re.compile(r"`?([A-Za-z0-9_][A-Za-z0-9_./+-]*\." + EXT + r")[:]([0-9]+)(?:\s*[-–]\s*([0-9]+))?")


def build_index():
    """basename -> [(root name, full path)] over every configured checkout."""
    index = {}
    for name, var in ROOT_VARS.items():
        root = os.environ.get(var)
        if not root:
            print(f"!! {var} not set - citations into '{name}' will not resolve", file=sys.stderr)
            continue
        root = os.path.expanduser(root)
        if not os.path.isdir(root):
            print(f"!! {var}={root} is not a directory", file=sys.stderr)
            continue
        # build/ is skipped everywhere except when it is itself the configured root
        skip = SKIP_DIRS - {os.path.basename(root.rstrip("/"))}
        for dirpath, dirnames, filenames in os.walk(root):
            dirnames[:] = [d for d in dirnames if d not in skip]
            for fn in filenames:
                index.setdefault(fn, []).append((name, os.path.join(dirpath, fn)))
    return index


def nlines(path):
    try:
        with open(path, "r", errors="replace") as f:
            return sum(1 for _ in f)
    except OSError:
        return 0


def resolve(index, cited, line):
    """A cited path suffix -> the concrete files it could mean."""
    cands = index.get(os.path.basename(cited), [])
    if not cands:
        return []
    if "/" in cited:
        exact = [c for c in cands if c[1].endswith("/" + cited)]
        if exact:
            return exact
    inrange = [c for c in cands if nlines(c[1]) >= line]
    if inrange:
        cands = inrange
    prod = [c for c in cands if "/test" not in c[1] and "/Testing/" not in c[1]]
    return prod or cands


def line_at(path, n):
    try:
        with open(path, "r", errors="replace") as f:
            lines = f.readlines()
    except OSError as e:
        return f"<unreadable: {e}>"
    if n < 1 or n > len(lines):
        return f"<OUT OF RANGE: file has {len(lines)} lines>"
    return lines[n - 1].rstrip()


def main():
    index = build_index()
    total = ok = ambiguous = missing = 0

    # Recurse: a report may be split into postable issues in a subdirectory of its own
    # (docs/reports/everest-d20-client-auth/), and those carry citations too. Globbing only
    # the top level left three files unchecked for a day.
    for md in sorted(glob.glob(os.path.join(REPORTS, "**", "*.md"), recursive=True)):
        name = os.path.relpath(md, REPORTS)
        if os.path.basename(md) == "README.md":
            continue
        with open(md, "r", encoding="utf-8", errors="replace") as f:
            text = f.read()

        seen, out = set(), []
        for m in CITE.finditer(text):
            cited, a, b = m.group(1), int(m.group(2)), m.group(3)
            if (cited, a, b) in seen:
                continue
            seen.add((cited, a, b))
            total += 1
            span = f"{a}-{b}" if b else f"{a}"
            cands = resolve(index, cited, a)

            if not cands:
                missing += 1
                out.append(f"  ??  {cited}:{span}   <NO SUCH FILE under any configured root>")
                continue

            roots_hit = sorted({c[0] for c in cands})
            if len(roots_hit) > 1 or len(cands) > 3:
                ambiguous += 1
                out.append(f"  ~~  {cited}:{span}   ambiguous: {len(cands)} matches in {roots_hit}")
                for r, p in cands[:4]:
                    out.append(f"        [{r}] {p}  L{a}: {line_at(p, a).strip()[:110]}")
                continue

            ok += 1
            r, p = cands[0]
            out.append(f"  ->  {cited}:{span}   [{r}]")
            for n in ([a] if not b else range(a, min(int(b), a + 6) + 1)):
                out.append(f"        L{n}: {line_at(p, n).strip()[:120]}")

        if out:
            print(f"\n### {name}")
            print("\n".join(out))

    print(f"\n==== {total} citations: {ok} resolved, {ambiguous} ambiguous, {missing} unresolved ====",
          file=sys.stderr)
    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
