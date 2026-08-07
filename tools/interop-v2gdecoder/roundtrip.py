#!/usr/bin/env python3
"""Round-trip our recorded frames through V2Gdecoder — a second, cbV2G-independent oracle.

Why this exists
---------------
Our byte oracle for ISO 15118-2 is libcbv2g (`tools/cbv2g-ref`), and every cbexigen-derived
counterparty — EVerest, tux-evse — shares that lineage, so agreement with them proves little.
V2Gdecoder (FlUxIuS) is built on RISE-V2G + **EXIficient**: a different codec, a different
language, a different author. It is the same family as Josev's encoder, but where the Josev
cross-check is limited to the frames Josev happened to send us, this one encodes and decodes
*anything* on demand.

What it checks
--------------
For each frame, the full circle through the other implementation:

    our bytes  --(their decode)-->  XML  --(their encode)-->  their bytes

and then `their bytes == our bytes`, octet for octet. No XML-to-model mapping is needed, which
is what makes this cheap; the same shape as `tools/regenerate-appprotocol-vectors.py` uses
against cbV2G. Three distinct outcomes, and all three are informative:

  * **decode-fail** — they cannot read what we wrote. Either our bytes are wrong or their
    grammar is; a disagreement worth chasing either way.
  * **mismatch**    — they read it, but re-encode it differently. EXI is not canonical, so this
    is not automatically a defect: it may be a legitimate encoder choice (string tables, value
    partitions). It is, however, always a concrete byte diff to look at.
  * **ok**          — their codec and ours agree on the octets, through a decode and an encode.

Scope: V2Gdecoder ships schemas for SupportedAppProtocol, ISO 15118-2:2013 and DIN 70121.
**Not ISO 15118-20** — RISE-V2G predates it. Feed it -20 traces and every frame will decode-fail
for a reason that is not about us; the script skips them by protocol and says so.

Usage
-----
    python3 roundtrip.py [--jar ~/v2gdec/decoder.jar] [--cwd ~/v2gdec/V2Gdecoder] FILE...

FILE is either a codec vector file (`vectors[].expectedHex`, a bare EXI document) or a session
trace (`exchanges[].{request,response}.frame`, V2GTP-framed — the 8-byte header is stripped).
Set up the oracle with `setup.sh`; see README.md.
"""

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

# V2GTP: 0x01 0xFE, 2-byte payload type, 4-byte length. 0x8001 is an EXI-encoded V2G message.
V2GTP_HEADER_BYTES = 8
V2GTP_EXI_PAYLOAD  = 0x8001

# Every JVM start costs ~1 s, and each frame needs two. Worth knowing before a long corpus.
JVM_CALLS_PER_FRAME = 2


class Oracle:
    """A thin wrapper over the V2Gdecoder CLI. `cwd` must hold the ./schemas directory."""

    def __init__(self, jar: Path, cwd: Path):
        self.jar = jar
        self.cwd = cwd

    def _run(self, mode: str, payload: str, want: str) -> tuple[bool, str]:
        """Runs one conversion. `want` is "xml" or "hex" — see _extract for why that matters."""
        proc = subprocess.run(
            ["java", "-jar", str(self.jar), mode, "-s", payload],
            cwd=str(self.cwd), capture_output=True, text=True, timeout=120,
        )
        return self._extract(proc.stdout, proc.stderr, proc.returncode, want)

    @staticmethod
    def _extract(stdout: str, stderr: str, code: int, want: str) -> tuple[bool, str]:
        """Pick the payload out of stdout.

        V2Gdecoder writes its *result* to stdout — and so does everything else. Under a modern
        JVM log4j opens with `WARNING: sun.reflect.Reflection.getCallerClass is not supported`,
        and a parse failure prints `[Fatal Error] …` there too, with a zero exit code. So the
        return code says nothing and the payload has to be recognised by shape.
        """
        if want == "xml":
            start = stdout.find("<")
            if start >= 0:
                return True, stdout[start:].strip()
        else:
            for line in reversed(stdout.splitlines()):
                line = line.strip()
                if line and len(line) % 2 == 0 and all(c in "0123456789abcdefABCDEF" for c in line):
                    return True, line

        noise = [l.strip() for l in (stdout + "\n" + stderr).splitlines() if l.strip()]
        reason = next((l for l in noise
                       if "Error" in l or "Exception" in l or "error" in l), None)
        return False, reason or f"exit {code}, no {want} in output"

    def decode(self, hex_frame: str) -> tuple[bool, str]:
        return self._run("-e", hex_frame, want="xml")

    def encode(self, xml: str) -> tuple[bool, str]:
        return self._run("-x", xml, want="hex")


def load_frames(path: Path) -> tuple[str, list[tuple[str, str]]]:
    """Returns (protocol, [(label, bare-EXI-hex), ...])."""
    doc = json.loads(path.read_text(encoding="utf-8-sig"))

    if "vectors" in doc:
        protocol = doc.get("generator", "")
        frames = [(v["name"], v["expectedHex"].replace(" ", ""))
                  for v in doc["vectors"] if v.get("expectedHex")]
        return protocol, frames

    if "exchanges" in doc:
        protocol = doc.get("protocol", "")
        frames = []
        for exchange in doc["exchanges"]:
            for side in ("request", "response"):
                item = exchange.get(side)
                if not item or not item.get("frame"):
                    continue
                if int(str(item.get("payloadType", "0")), 16) != V2GTP_EXI_PAYLOAD:
                    continue
                label = f"{exchange['index']:02d}.{side[:3]} {item.get('message', '?')}"
                frames.append((label, item["frame"][V2GTP_HEADER_BYTES * 2:]))
        return protocol, frames

    raise SystemExit(f"{path.name}: neither a vector file nor a session trace")


def normalise(hex_text: str) -> str:
    return re.sub(r"[^0-9a-f]", "", hex_text.lower())


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("files", nargs="+", type=Path)
    ap.add_argument("--jar", type=Path, default=Path.home() / "v2gdec" / "decoder.jar")
    ap.add_argument("--cwd", type=Path, default=Path.home() / "v2gdec" / "V2Gdecoder")
    ap.add_argument("--json", type=Path, help="write the full result set here")
    args = ap.parse_args()

    if not args.jar.exists():
        raise SystemExit(f"no jar at {args.jar} — run setup.sh first")
    if not (args.cwd / "schemas").is_dir():
        raise SystemExit(f"no ./schemas under {args.cwd} — V2Gdecoder resolves grammars from cwd")

    oracle = Oracle(args.jar, args.cwd)
    results, totals = [], {"ok": 0, "mismatch": 0, "decode-fail": 0, "encode-fail": 0}

    for path in args.files:
        protocol, frames = load_frames(path)

        if "15118_20" in protocol or "iso15118-20" in protocol:
            print(f"\n== {path.name}: SKIPPED — {protocol}; "
                  f"RISE-V2G has no ISO 15118-20 schemas")
            continue

        print(f"\n== {path.name}  ({protocol}, {len(frames)} frames, "
              f"~{len(frames) * JVM_CALLS_PER_FRAME} JVM starts)")

        for label, our_hex in frames:
            our_hex = normalise(our_hex)

            ok, payload = oracle.decode(our_hex)
            if not ok:
                verdict, detail = "decode-fail", payload
            else:
                ok, re_encoded = oracle.encode(payload)
                if not ok:
                    verdict, detail = "encode-fail", re_encoded
                elif normalise(re_encoded) == our_hex:
                    verdict, detail = "ok", ""
                else:
                    verdict = "mismatch"
                    detail = f"ours {len(our_hex)//2} B, theirs {len(normalise(re_encoded))//2} B"

            totals[verdict] += 1
            results.append({"file": path.name, "frame": label, "verdict": verdict,
                            "detail": detail, "ourHex": our_hex,
                            "theirHex": normalise(re_encoded) if verdict == "mismatch" else None})

            mark = {"ok": "  ok  ", "mismatch": " DIFF ",
                    "decode-fail": " DEC! ", "encode-fail": " ENC! "}[verdict]
            print(f"   [{mark}] {label}" + (f"   {detail}" if detail else ""))

    print("\n== totals: " + ", ".join(f"{k}={v}" for k, v in totals.items()))

    if args.json:
        args.json.write_text(json.dumps({"totals": totals, "results": results}, indent=1),
                             encoding="utf-8")
        print(f"   written: {args.json}")

    # Only a hard failure of their codec is a non-zero exit; a mismatch is a finding to read,
    # not a broken run, and the caller should look at the diff rather than at $?.
    return 1 if totals["decode-fail"] or totals["encode-fail"] else 0


if __name__ == "__main__":
    sys.exit(main())
