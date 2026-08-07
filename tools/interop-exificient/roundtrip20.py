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
CLI = "com.siemens.ct.exi.main.cmd.EXIficientCMD"

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
    """EXIficient's command line, one conversion per JVM. It works on files, not stdin."""

    def __init__(self, schema_root: Path, work: Path):
        self.work = work
        self.retries = 0
        work.mkdir(parents=True, exist_ok=True)
        self.schema_root = self._stage(schema_root, work / "schemas")

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

    #: The one failure that is known to be environmental, and the only one worth retrying — see _run.
    SCHEMA_FLAKE = "building XML Schema Model"
    ATTEMPTS = 12

    def _run(self, mode: str, schema: str, src: Path, dst: Path) -> tuple[bool, str]:
        """One conversion, retried past a failure that this rig produces and the codec does not.

        Some invocations die before decoding anything, with Xerces reporting that it could not read
        `xmldsig-core-schema.xsd` — an import sitting next to the schema that imports it, byte-identical
        to its source, well-formed, and readable by the shell at that moment. It is not deterministic
        and it is not the codec: the same command against the same file failed 0 times in 30 in one
        window and 10 times in 10 twenty minutes later.

        What it is not, all measured rather than assumed: memory, file descriptors, JVMs left running,
        a full `/tmp`, a private `java.io.tmpdir`, invocation rate, a corrupted copy off the Windows
        mount (digests match, and the source reads identically 20 times in a row). One measurement did
        suggest that running from the schema's own directory with a bare filename fixed it — 0/30
        against 21/30 for an absolute path — and a later run of that exact shape failed 10 out of 10.
        So the working directory is *not* the explanation, and this is left running that way only
        because it costs nothing and matches how V2Gdecoder is documented to be started.

        The diagnosis stops there. It is a JVM-on-WSL question, not a V2G one, and the verdict does not
        need it answered: a real codec failure is deterministic and reports something else, so retrying
        **only** this error separates the two. `retries` in the totals is how much noise a run hit; a
        frame that exhausts every attempt is reported as a failure like any other.
        """
        schema_dir = (self.schema_root / schema).parent
        last = "no output"
        for attempt in range(self.ATTEMPTS):
            dst.unlink(missing_ok=True)
            proc = subprocess.run(
                ["java", "-cp", str(JAR), CLI, mode,
                 "-schema", Path(schema).name,          # bare name, resolved from cwd
                 "-i", str(src), "-o", str(dst)],       # in/out stay absolute
                cwd=str(schema_dir),
                capture_output=True, text=True, timeout=180,
            )
            if dst.exists() and dst.stat().st_size > 0:
                self.retries += attempt
                return True, ""

            noise = (proc.stdout + "\n" + proc.stderr).strip().splitlines()
            last = (next((l.strip() for l in noise if "ERROR" in l or "Exception" in l),
                         None) or "no output")[:180]
            if self.SCHEMA_FLAKE not in last:
                break   # a real decode/encode failure: do not retry it

        self.retries += attempt
        return False, last

    def decode(self, exi: bytes, schema: str) -> tuple[bool, str]:
        src, dst = self.work / "in.exi", self.work / "out.xml"
        src.write_bytes(exi)
        ok, why = self._run("-decode", schema, src, dst)
        return (True, dst.read_text(encoding="utf-8")) if ok else (False, why)

    def encode(self, xml: str, schema: str) -> tuple[bool, bytes]:
        src, dst = self.work / "in.xml", self.work / "back.exi"
        src.write_text(xml, encoding="utf-8")
        ok, why = self._run("-encode", schema, src, dst)
        return (True, dst.read_bytes()) if ok else (False, why)


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
    results, totals = [], {"ok": 0, "mismatch": 0, "decode-fail": 0, "encode-fail": 0}

    for path in args.files:
        label, frames = frames_of(path)
        if not frames:
            print(f"\n== {path.name}: SKIPPED — {label} is not ISO 15118-20")
            continue
        print(f"\n== {path.name}  ({label}, {len(frames)} frames)")

        for name, our_hex, schema in frames:
            ours = bytes.fromhex(our_hex)

            ok, payload = exi.decode(ours, schema)
            if not ok:
                verdict, detail, theirs = "decode-fail", payload, None
            else:
                ok, back = exi.encode(payload, schema)
                if not ok:
                    verdict, detail, theirs = "encode-fail", back, None
                elif back == ours:
                    verdict, detail, theirs = "ok", "", None
                else:
                    verdict = "mismatch"
                    detail = f"ours {len(ours)} B, theirs {len(back)} B"
                    theirs = back.hex()

            totals[verdict] += 1
            results.append({"file": path.name, "frame": name, "schema": Path(schema).name,
                            "verdict": verdict, "detail": detail,
                            "ourHex": our_hex if verdict != "ok" else None, "theirHex": theirs})

            mark = {"ok": "  ok  ", "mismatch": " DIFF ",
                    "decode-fail": " DEC! ", "encode-fail": " ENC! "}[verdict]
            print(f"   [{mark}] {name}" + (f"   {detail}" if detail else ""))

    print("\n== totals: " + ", ".join(f"{k}={v}" for k, v in totals.items())
          + f", retries={exi.retries}")

    if args.json:
        args.json.write_text(json.dumps({"totals": totals, "retries": exi.retries,
                                         "results": results}, indent=1),
                             encoding="utf-8")
        print(f"   written: {args.json}")

    return 1 if totals["decode-fail"] or totals["encode-fail"] else 0


if __name__ == "__main__":
    sys.exit(main())
