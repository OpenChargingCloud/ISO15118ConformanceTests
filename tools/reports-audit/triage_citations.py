#!/usr/bin/env python3
"""Does the cited line contain what the report says is there? -- the reading job, narrowed.

check_citations.py resolves every `file:line` in docs/reports/ and prints the line it lands on.
Comparing those few hundred lines against the claims beside them is a reading job. This narrows it:
for each citation it collects the identifiers the report quotes near it and looks for them at the
cited line(s), and if they are absent, within +-15 lines -- so "the line says what we claim",
"the number drifted" and "this needs reading" come out as separate verdicts.

    TREE_EVEREST=~/everest/everest-core TREE_JOSEV=~/josev-src ... python3 triage_citations.py > t.tsv

Same TREE_* variables as check_citations.py, whose index and resolver this reuses. Output is a TSV:
report, citation, zone (banner/body), verdict, offset, tokens, the cited line, the report context.

Verdicts: MATCH (an identifier quoted near the citation is on the cited line), DRIFT (it is within
+-15 lines; the offset says where), MISS (nowhere near), NOTOKEN (the report quotes nothing to compare
with), AMBIGUOUS, OUT_OF_RANGE, UNRESOLVED.

**Run it once per tree a citation can mean, and join the runs.** Most citations name a tree --
`[tag]`, `[main]`, "2026.02.1 line numbers", a commit. A citation is verified when it MATCHes on the
tree its report names; one that matches nowhere is the list to read. On 2026-11-05, four everest-core
trees (2026.02.1, main@ebcd36d, 2026.02.2, main@01f36de1) turned 294 citations into 107 to read, of
which two were wrong -- see docs/interop-runs/2026-11-05-reports-audit/notes.md.

**A MISS is not a wrong citation and a MATCH is not a right one.** The identifiers come from the
surrounding paragraph, which often quotes the *next* citation's subject; a checklist line that lists
twelve citations gives the same tokens to all twelve. The verdict decides what to read first, never
what to conclude.
"""
import glob
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import check_citations as cc  # noqa: E402

STOP = {"line", "lines", "file", "main", "true", "false", "null", "nullptr", "return", "const",
        "auto", "void", "static", "struct", "this", "that", "with", "from", "into", "only", "else",
        "case", "break", "include", "define", "std", "the", "and", "for", "int", "bool", "char"}
TOK = re.compile(r"[A-Za-z_][A-Za-z0-9_]{3,}")
CITE_STRIP = re.compile(r"[\w./+-]+\.(?:cpp|hpp|h|c|py|sh|yaml|yml|json|rs|java|cs|txt):\d+(?:\s*[-–]\s*\d+)?")
WINDOW = 15


def context_tokens(text, start, end, cited):
    """Identifiers the report quotes near a citation: backtick spans first, code-shaped words after."""
    ls = text.rfind("\n", 0, start)
    ls = text.rfind("\n", 0, ls) if ls > 0 else 0
    le = text.find("\n", end)
    le = text.find("\n", le + 1) if le >= 0 else len(text)
    ctx = text[max(ls, 0):le if le >= 0 else len(text)]
    base = os.path.splitext(os.path.basename(cited))[0]
    toks = set()
    for s in re.findall(r"`([^`]+)`", ctx):
        if re.fullmatch(r"[\w./+-]+\.\w+:\d+(?:-\d+)?", s.strip()):
            continue
        toks.update(t for t in TOK.findall(s) if t.lower() not in STOP and t != base)
    if not toks:
        # A code block or a table row quotes nothing in backticks. Widen to +-4 lines and keep only
        # code-shaped words (snake_case, camelCase, Scope::member) and the words of quoted strings.
        a = start
        for _ in range(4):
            a = text.rfind("\n", 0, a - 1) if a > 0 else 0
        z = end
        for _ in range(5):
            nz = text.find("\n", z + 1)
            z = nz if nz >= 0 else len(text)
        wide = CITE_STRIP.sub(" ", text[max(a, 0):z])
        for t in re.findall(r"[A-Za-z_][A-Za-z0-9_]*(?:::[A-Za-z_][A-Za-z0-9_]*)*", wide):
            core = t.split("::")[-1]
            if len(core) >= 5 and core.lower() not in STOP and core != base and \
                    ("_" in core or re.search(r"[a-z][A-Z]", core) or "::" in t):
                toks.add(core)
        for q in re.findall(r'"([^"\n]{6,80})"', wide):
            toks.update(w for w in re.findall(r"[A-Za-z][A-Za-z0-9_]{4,}", q) if w.lower() not in STOP)
    return toks, ctx.replace("\n", " ")[:220]


def lines_of(path):
    try:
        with open(path, "r", errors="replace") as f:
            return f.readlines()
    except OSError:
        return []


def main():
    index = cc.build_index()
    print("report\tcitation\tzone\tverdict\toffset\ttokens\tline\tcontext")
    for md in sorted(glob.glob(os.path.join(cc.REPORTS, "**", "*.md"), recursive=True)):
        if os.path.basename(md) == "README.md":
            continue
        name = os.path.relpath(md, cc.REPORTS)
        text = open(md, encoding="utf-8", errors="replace").read()
        m_status = re.search(r"^Status:", text, re.M)
        banner_end = m_status.start() if m_status else 0
        seen = set()
        for m in cc.CITE.finditer(text):
            cited, a, b = m.group(1), int(m.group(2)), m.group(3)
            if (cited, a, b) in seen:
                continue
            seen.add((cited, a, b))
            zone = "banner" if m.start() < banner_end else "body"
            span = f"{a}-{b}" if b else f"{a}"
            hi = int(b) if b else a
            toks, ctx = context_tokens(text, m.start(), m.end(), cited)
            tk = ",".join(sorted(toks))
            row = lambda v, off="", line="": print(f"{name}\t{cited}:{span}\t{zone}\t{v}\t{off}\t{tk}\t{line}\t{ctx}")  # noqa: E731

            cands = cc.resolve(index, cited, a)
            if not cands:
                row("UNRESOLVED")
                continue
            if len({c[0] for c in cands}) > 1 or len(cands) > 3:
                hits = [c for c in cands if toks and any(t in "".join(lines_of(c[1])[a - 1:hi]) for t in toks)]
                if len(hits) != 1:
                    row("AMBIGUOUS", len(cands))
                    continue
                cands = hits
            L = lines_of(cands[0][1])
            if a < 1 or a > len(L):
                row("OUT_OF_RANGE", len(L))
                continue
            first = L[a - 1].strip()[:110]
            if not toks:
                row("NOTOKEN", "", first)
                continue
            if any(t in "".join(L[a - 1:min(hi, len(L))]) for t in toks):
                row("MATCH", 0, first)
                continue
            off = next((s for d in range(1, WINDOW + 1) for s in (-d, d)
                        if 0 <= a - 1 + s and min(hi, len(L)) + s <= len(L)
                        and any(t in "".join(L[a - 1 + s:min(hi, len(L)) + s]) for t in toks)), None)
            row("DRIFT" if off is not None else "MISS", "" if off is None else off, first)


if __name__ == "__main__":
    main()
