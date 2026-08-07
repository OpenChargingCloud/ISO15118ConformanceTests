#!/usr/bin/env python3
"""Trace one -20 frame through EXIficient event by event, and say where it stops making sense.

`roundtrip20.py` gives a verdict per frame. When that verdict is `decode-fail` the detail is
`Premature EOS found while reading data`, which says only that EXIficient ran out of bits before its
grammar was satisfied — not where. That distinction is the whole diagnosis: a frame fails that way
both when the first event code was wrong and everything after it was noise, and when 240 of 241 bytes
were read correctly and one trailing particle was missing.

So this drives EXIficient's event API rather than its SAX bridge (`Walk20.java` next to this file) and
prints every decoded event with the byte offset it came from. The last line before the failure is the
last thing the two codecs agreed about.

Usage
-----
    python3 walk20.py [--repo <path>] VECTORFILE FRAME [FRAME...]

    python3 walk20.py libs/.../Vectors/Iso15118_20.AC_DER_SAE.vectors.json \\
        AC_ChargeParameterDiscoveryRes_DER AC_ChargeParameterDiscoveryReq_DER

Pass the working sibling alongside the failing one — a trace is much easier to read against a trace
that is known good.

Same rig as `roundtrip20.py`: the jar from `tools/interop-v2gdecoder/setup.sh`, a JDK, and ISO's
schemas read in place from the app submodule.
"""

import argparse
import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from roundtrip20 import BY_VECTOR_FILE, JAR, Exificient   # noqa: E402


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("vectors", type=Path)
    ap.add_argument("frames", nargs="+")
    ap.add_argument("--repo", type=Path, default=Path.cwd())
    args = ap.parse_args()

    schema_root = args.repo / "libs" / "EVSimulatorApp" / "libs" / "WWCP_ISO15118"
    if not JAR.exists():
        raise SystemExit(f"no jar at {JAR} — run tools/interop-v2gdecoder/setup.sh")

    doc = json.loads(args.vectors.read_text(encoding="utf-8-sig"))
    by_name = {v["name"]: v.get("expectedHex", "").replace(" ", "") for v in doc["vectors"]}

    key = next((k for k in sorted(BY_VECTOR_FILE, key=len, reverse=True)
                if f".{k}." in args.vectors.name), None)
    if key is None:
        raise SystemExit(f"{args.vectors.name}: cannot tell which -20 message set this is")

    # Reuse roundtrip20's staging and schema cache wholesale; only the driver class differs.
    exi = Exificient(schema_root, Path.home() / "v2gdec" / "exificient-work")
    classes = exi.work / "walk-classes"
    classes.mkdir(parents=True, exist_ok=True)
    build = subprocess.run(
        ["javac", "-nowarn", "-cp", str(JAR), "-d", str(classes),
         str(Path(__file__).resolve().parent / "Walk20.java")],
        capture_output=True, text=True, timeout=300)
    if build.returncode != 0:
        raise SystemExit("javac failed:\n" + (build.stderr or "")[:4000])

    schema = exi.schema_root / BY_VECTOR_FILE[key]
    jobs = exi.work / "walk-jobs.tsv"
    lines = []
    for frame in args.frames:
        if frame not in by_name or not by_name[frame]:
            raise SystemExit(f"{frame}: not in {args.vectors.name}")
        lines.append(f"{frame}\t{schema}\t{by_name[frame]}\n")
    jobs.write_text("".join(lines), encoding="utf-8")

    run = subprocess.run(["java", "-cp", f"{JAR}:{classes}", "Walk20", str(jobs)],
                         capture_output=True, text=True, timeout=600)
    print(run.stdout, end="")
    if run.stderr.strip():
        print(run.stderr, file=sys.stderr)
    return run.returncode


if __name__ == "__main__":
    sys.exit(main())
