#!/usr/bin/env python3
"""Round-trip the ISO 15118-20 corpus through EXIficient — the oracle -20 did not have.

Why
---
`tools/interop-v2gdecoder/` closed the independence gap for ISO 15118-2, and could not close it for
-20: RISE-V2G predates the standard and ships no schemas for it. So -20 kept exactly one byte oracle,
libcbv2g — which is also our vector generator, and also what EVerest and tux-evse encode with. Every
`-20` byte agreement this project could show was agreement with one implementation.

EXIficient is a general schema-informed EXI processor, not a V2G tool: point it at an XSD and it will
encode and decode against it. Point it at ISO's own `-20` schemas and it becomes the second opinion,
from the codec family that has never shared a line with cbexigen.

    our bytes --(EXIficient decode)--> XML --(EXIficient encode)--> bytes  ==?  our bytes

Same method as the -2 run, and the same three outcomes: **decode-fail** (they cannot read what we
wrote), **mismatch** (they read it and write it back differently — EXI is not canonical, so a byte diff
to look at rather than a defect), **ok**.

The jar
-------
EXIficient 1.0.4, taken out of the V2Gdecoder fat jar that `tools/interop-v2gdecoder/setup.sh` already
fetches — `com.siemens.ct.exi.main.cmd.EXIficientCMD` is in it, and driving that class directly skips
RISE-V2G's -2-only wrapper entirely. One download on the rig, two oracles out of it.

Options matter: our streams are schema-informed and **not** strict, so the CLI runs with its defaults.
Passing `-strict` makes EXIficient throw on the very first frame, which is the expected answer and not
a finding — cbV2G, Josev and we all write non-strict.

Schemas
-------
ISO's, from the app submodule, where `download-schemas.sh` puts them. They are not redistributed by
this repository and nothing here copies them anywhere; the paths are read in place.

Usage
-----
    python3 roundtrip20.py [--repo <path>] [--json <out>] FILE...

FILE is a `-20` vector file (schema chosen by filename) or a session trace (schema chosen per frame
from the V2GTP payload type, which is what actually distinguishes CommonMessages from AC from DC).
"""

import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path

V2GTP_HEADER_BYTES = 8
JAR = Path.home() / "v2gdec" / "decoder.jar"

# V2GTP payload type -> schema. This is the dispatch the wire itself provides: a -20 session carries
# three different message sets over one connection and tells the peer which is which.
BY_PAYLOAD_TYPE = {
    "0x8001": "WWCP_ISO15118_EXI/Schemas/V2G_CI_AppProtocol.xsd",
    "0x8002": "WWCP_ISO15118_20.CommonMessages/Schemas/V2G_CI_CommonMessages.xsd",
    "0x8003": "WWCP_ISO15118_20.AC/Schemas/V2G_CI_AC.xsd",
    "0x8004": "WWCP_ISO15118_20.DC/Schemas/V2G_CI_DC.xsd",
}

# Vector files have no payload type; their name says which set they are.
BY_VECTOR_FILE = {
    "CommonMessages": "WWCP_ISO15118_20.CommonMessages/Schemas/V2G_CI_CommonMessages.xsd",
    "AC_DER_IEC":     "WWCP_ISO15118_20.AC_DER_IEC/Schemas/V2G_CI_AC_DER_IEC.xsd",
    "AC_DER_SAE":     "WWCP_ISO15118_20.AC_DER_SAE/Schemas/V2G_CI_AC_DER_SAE.xsd",
    "ACDP":           "WWCP_ISO15118_20.ACDP/Schemas/V2G_CI_ACDP.xsd",
    "WPT":            "WWCP_ISO15118_20.WPT/Schemas/V2G_CI_WPT.xsd",
    "AC":             "WWCP_ISO15118_20.AC/Schemas/V2G_CI_AC.xsd",
    "DC":             "WWCP_ISO15118_20.DC/Schemas/V2G_CI_DC.xsd",
}


class Exificient:
    """One JVM for the whole corpus, via the Roundtrip20 driver next to this file.

    The obvious alternative — EXIficient's own CLI, one conversion per invocation — rebuilds the
    schema model every time, which on this rig fails intermittently often enough that a full run
    never finished. Building each grammar once removes ~99% of that exposure along with most of the
    wall clock. `Roundtrip20.java` documents the rest.
    """

    def __init__(self, schema_root: Path, work: Path):
        self.work = work
        self.retries = 0
        work.mkdir(parents=True, exist_ok=True)
        self.schema_root = self._stage(schema_root, work / "schemas")
        self.classes = self._compile(work / "classes")

    @staticmethod
    def _stage(source_root: Path, staged_root: Path) -> Path:
        """Copy the schema tree onto the local filesystem before running anything.

        Every JVM rebuilds the XSModel from scratch, and each XSD pulls in CommonTypes and
        xmldsig alongside it — so a few hundred frames means reading the same files a thousand
        times. Read straight off a Windows drive mounted into WSL that is not merely slow, it is
        *unreliable*: it fails intermittently, and Xerces reports the failure as
        "Problem occured while building XML Schema Model", which reads like a bad schema rather
        than a bad read. Nine of the first twenty-six frames failed that way, all nine decoding
        perfectly when run on their own. Staging removes the question entirely.

        The schemas are ISO's. This copies them to a scratch directory on the same machine and
        nothing else; `tools/rig-cleanup.sh` removes it.
        """
        if staged_root.exists():
            shutil.rmtree(staged_root)
        for relative in {**BY_PAYLOAD_TYPE, **BY_VECTOR_FILE}.values():
            source = (source_root / relative).parent
            shutil.copytree(source, staged_root / Path(relative).parent, dirs_exist_ok=True)
        return staged_root

    def _compile(self, classes: Path) -> Path:
        """Build the driver. Needs a JDK — a JRE alone is not enough, and this rig had six of those
        and no compiler until one was installed."""
        classes.mkdir(parents=True, exist_ok=True)
        source = Path(__file__).resolve().parent / "Roundtrip20.java"
        proc = subprocess.run(
            ["javac", "-nowarn", "-cp", str(JAR), "-d", str(classes), str(source)],
            capture_output=True, text=True, timeout=300,
        )
        if proc.returncode != 0:
            raise SystemExit("javac failed — is a JDK installed?\n" + (proc.stderr or "")[:2000])
        return classes

    def run(self, jobs: list[tuple[str, str, str]]) -> dict[str, tuple[str, str, str]]:
        """Runs every (name, schema, hex) in one JVM. Returns name -> (verdict, theirHex, detail)."""
        jobs_file, results_file = self.work / "jobs.tsv", self.work / "results.tsv"
        jobs_file.write_text(
            "".join(f"{name}\t{self.schema_root / schema}\t{hexstr}\n" for name, schema, hexstr in jobs),
            encoding="utf-8")
        results_file.unlink(missing_ok=True)

        proc = subprocess.run(
            ["java", "-cp", f"{JAR}:{self.classes}", "Roundtrip20",
             str(jobs_file), str(results_file)],
            capture_output=True, text=True, timeout=3600,
        )
        for line in (proc.stderr or "").splitlines():
            if "schema models built" in line:
                print("   " + line.strip())
                self.retries = int(line.rsplit("retries: ", 1)[1].rstrip(")"))
        if not results_file.exists():
            raise SystemExit("driver produced nothing:\n" + (proc.stderr or proc.stdout)[:2000])

        out = {}
        for line in results_file.read_text(encoding="utf-8").splitlines():
            if not line.strip():
                continue
            name, verdict, their_hex, detail = line.split("\t", 3)
            out[name] = (verdict, "" if their_hex == "-" else their_hex,
                         "" if detail == "-" else detail)
        return out


def frames_of(path: Path) -> tuple[str, list[tuple[str, str, str]]]:
    """Returns (label, [(name, exi_hex, schema), ...]). Non -20 inputs come back empty."""
    doc = json.loads(path.read_text(encoding="utf-8-sig"))

    if "vectors" in doc:
        # "Iso15118_20.AC_DER_IEC.vectors.json" -> AC_DER_IEC. Longest key first so that AC_DER_*
        # is not swallowed by AC.
        stem = path.name
        key = next((k for k in sorted(BY_VECTOR_FILE, key=len, reverse=True) if f".{k}." in stem), None)
        if key is None:
            return doc.get("generator", ""), []
        schema = BY_VECTOR_FILE[key]
        return (doc.get("generator", ""),
                [(v["name"], v["expectedHex"].replace(" ", ""), schema)
                 for v in doc["vectors"] if v.get("expectedHex")])

    if "exchanges" in doc:
        if doc.get("protocol") != "iso15118-20":
            return doc.get("protocol", ""), []
        out = []
        for exchange in doc["exchanges"]:
            for side in ("request", "response"):
                item = exchange.get(side)
                if not item or not item.get("frame"):
                    continue
                schema = BY_PAYLOAD_TYPE.get(str(item.get("payloadType", "")).lower())
                if schema is None:
                    continue
                out.append((f"{exchange['index']:02d}.{side[:3]} {item.get('message', '?')}",
                            item["frame"][V2GTP_HEADER_BYTES * 2:], schema))
        return doc.get("protocol", ""), out

    raise SystemExit(f"{path.name}: neither a vector file nor a session trace")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("files", nargs="+", type=Path)
    ap.add_argument("--repo", type=Path, default=Path.cwd(),
                    help="conformance repo root; schemas are read from libs/EVSimulatorApp under it")
    ap.add_argument("--json", type=Path)
    args = ap.parse_args()

    schema_root = args.repo / "libs" / "EVSimulatorApp" / "libs" / "WWCP_ISO15118"
    missing = [s for s in {**BY_PAYLOAD_TYPE, **BY_VECTOR_FILE}.values()
               if not (schema_root / s).is_file()]
    if missing:
        raise SystemExit("schemas not present — run download-schemas.sh:\n  " + "\n  ".join(missing))
    if not JAR.exists():
        raise SystemExit(f"no jar at {JAR} — run tools/interop-v2gdecoder/setup.sh")

    exi = Exificient(schema_root, Path.home() / "v2gdec" / "exificient-work")

    # Collect the whole corpus first: one JVM does all of it, and a frame's name has to be unique
    # across files for the results to be matched back.
    per_file, jobs = [], []
    for path in args.files:
        label, frames = frames_of(path)
        if not frames:
            print(f"== {path.name}: SKIPPED — {label} is not ISO 15118-20")
            continue
        per_file.append((path, label, frames))
        jobs += [(f"{path.name}#{name}", schema, our_hex) for name, our_hex, schema in frames]

    print(f"== {len(jobs)} frames across {len(per_file)} files, one JVM")
    verdicts = exi.run(jobs)

    results, totals = [], {"ok": 0, "mismatch": 0, "decode-fail": 0, "encode-fail": 0}
    for path, label, frames in per_file:
        print(f"\n== {path.name}  ({label}, {len(frames)} frames)")
        for name, our_hex, schema in frames:
            verdict, their_hex, detail = verdicts.get(
                f"{path.name}#{name}", ("decode-fail", "", "no result from the driver"))

            totals[verdict] = totals.get(verdict, 0) + 1
            results.append({"file": path.name, "frame": name, "schema": Path(schema).name,
                            "verdict": verdict, "detail": detail,
                            "ourHex": our_hex if verdict != "ok" else None,
                            "theirHex": their_hex or None})

            mark = {"ok": "  ok  ", "mismatch": " DIFF ",
                    "decode-fail": " DEC! ", "encode-fail": " ENC! "}.get(verdict, " ???? ")
            print(f"   [{mark}] {name}" + (f"   {detail}" if detail else ""))

    print("\n== totals: " + ", ".join(f"{k}={v}" for k, v in totals.items())
          + f", schema-load retries={exi.retries}")

    if args.json:
        args.json.write_text(json.dumps({"totals": totals, "retries": exi.retries,
                                         "results": results}, indent=1),
                             encoding="utf-8")
        print(f"   written: {args.json}")

    return 1 if totals["decode-fail"] or totals["encode-fail"] else 0


if __name__ == "__main__":
    sys.exit(main())
