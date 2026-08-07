#!/usr/bin/env python3
"""Build a DIN 70121 corpus out of a captured session, and judge it with V2Gdecoder.

Why a DIN corpus at all
-----------------------
This project speaks no DIN 70121 — no schemas, no codec. What it does have is a real capture
(a Tesla Model 3 at a German charge point, from tux-evse's trace-logs) and, since 2026-08-07,
an oracle that *does* speak DIN: V2Gdecoder ships `schemas_din/`. So the frames can be read
even though we cannot read them, which makes them worth keeping for three reasons:

  * **V2GTP framing is protocol-independent.** Our own framing layer can be run over thousands
    of frames from real equipment rather than only over frames we produced ourselves — the same
    structural argument that made the SupportedAppProtocol handshake reachable in a DIN capture.
  * **Ground truth, if DIN is ever implemented here.** Field bytes beat anything we could write.
  * **An EXI question we can ask today.** Do real devices use the EXI value partition on encode?
    Our own encoder does not (see `Interop/ExiStringTableTests.cs`); a 2,214-transaction session
    from two independent vendors is the best evidence available on what the field actually does.

Beware the fuzzy decoder
------------------------
V2Gdecoder tries grammars until one parses, and with the ISO-2 schema set a DIN frame does not
fail -- it *succeeds*, wrongly. The DIN `SessionSetupReq` below reads as an ISO-2
`WeldingDetectionReq` full of nonsense. So the schema set is pinned explicitly here, never left
to detection, and `--schemas` must point at a directory holding the DIN XSDs as `./schemas`.

Usage
-----
    python3 din-corpus.py <capture.pcap> --out <corpus.json> [--sample N] [--roundtrip]

Without `--roundtrip` it only extracts and groups; the decode pass costs one JVM start per
unique frame and is the slow part.
"""

import argparse
import json
import re
import subprocess
import sys
from collections import Counter, OrderedDict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "interop-tux-evse"))
from importlib import import_module

v2gtp = import_module("v2gtp-from-pcap".replace("-", "_")) if False else None

V2GTP_HEADER_BYTES = 8
JAR = Path.home() / "v2gdec" / "decoder.jar"
DIN_CWD = Path.home() / "v2gdec" / "din"


def frames_from_pcap(path: Path):
    """Yields (direction, index, v2gtp_hex) using the extractor already in this repo."""
    tool = Path(__file__).resolve().parent.parent / "interop-tux-evse" / "v2gtp-from-pcap.py"
    proc = subprocess.run([sys.executable, str(tool), str(path), "1000000"],
                          capture_output=True, text=True, timeout=900)
    if proc.returncode != 0:
        raise SystemExit(f"extractor failed:\n{proc.stderr[:2000]}")

    direction, index = None, 0
    for line in proc.stdout.splitlines():
        header = re.match(r"=== (\d+) -> (\d+):", line)
        if header:
            direction, index = f"{header.group(1)}->{header.group(2)}", 0
            continue
        hexline = line.strip()
        if direction and re.fullmatch(r"[0-9a-f]+", hexline) and len(hexline) >= V2GTP_HEADER_BYTES * 2:
            yield direction, index, hexline
            index += 1


def decode(exi_hex: str, cwd: Path) -> str | None:
    proc = subprocess.run(["java", "-jar", str(JAR), "-e", "-s", exi_hex],
                          cwd=str(cwd), capture_output=True, text=True, timeout=120)
    i = proc.stdout.find("<")
    return proc.stdout[i:].strip() if i >= 0 else None


def encode(xml: str, cwd: Path) -> str | None:
    proc = subprocess.run(["java", "-jar", str(JAR), "-x", "-s", xml],
                          cwd=str(cwd), capture_output=True, text=True, timeout=120)
    for line in reversed(proc.stdout.splitlines()):
        line = line.strip()
        if line and len(line) % 2 == 0 and all(c in "0123456789abcdefABCDEF" for c in line):
            return line.lower()
    return None


def message_name(xml: str) -> str:
    """The message this decode claims to be — or a marker that the grammar missed.

    Their fuzzy decoder tries MsgDef, then AppProtocol, then xmldsig, and returns the first that
    does not throw *without checking that the result makes sense*. So a frame whose real grammar
    fails does not come back as an error: it comes back as whatever the xmldsig grammar made of
    the bits. A root element that is neither a V2G_Message nor a supportedAppProtocol* is that
    case, and naming it as such is the whole point.
    """
    m = re.search(r"<ns\d+:Body><ns\d+:([A-Za-z_]+)", xml)
    if m:
        return m.group(1)
    m = re.search(r"<ns\d+:(supportedAppProtocol(?:Req|Res))", xml)
    if m:
        return m.group(1)
    root = re.search(r"<ns\d+:([A-Za-z_]+)", xml)
    return f"grammar-miss({root.group(1) if root else '?'})"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("pcap", type=Path)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--schemas", type=Path, default=DIN_CWD,
                    help="directory containing the DIN XSDs as ./schemas")
    ap.add_argument("--roundtrip", action="store_true",
                    help="also re-encode each unique frame and compare bytes")
    args = ap.parse_args()

    if args.roundtrip and not (args.schemas / "schemas" / "V2G_CI_MsgDef.xsd").is_file():
        raise SystemExit(f"{args.schemas}/schemas is not a schema set — see din-corpus docs")

    # --- extract -------------------------------------------------------------------------
    all_frames = list(frames_from_pcap(args.pcap))
    per_direction = Counter(d for d, _, _ in all_frames)
    print(f"== {args.pcap.name}: {len(all_frames)} V2GTP frames")
    for d, n in per_direction.items():
        print(f"   {d}: {n}")

    # --- group by identical octets -------------------------------------------------------
    # A charge loop repeats the same request thousands of times; the corpus wants one of each
    # shape, and the repetition count is itself worth recording.
    unique = OrderedDict()
    for direction, index, frame in all_frames:
        entry = unique.setdefault(frame, {"frame": frame, "direction": direction,
                                          "firstIndex": index, "count": 0})
        entry["count"] += 1
    print(f"== {len(unique)} distinct frames "
          f"({100 - 100 * len(unique) // len(all_frames)}% of the capture is repetition)")

    # --- decode, and optionally re-encode ------------------------------------------------
    corpus, totals = [], Counter()
    for n, entry in enumerate(unique.values(), 1):
        exi = entry["frame"][V2GTP_HEADER_BYTES * 2:]
        xml = decode(exi, args.schemas)
        if xml is None:
            totals["decode-fail"] += 1
            entry["message"], entry["verdict"] = "?", "decode-fail"
        else:
            entry["message"] = message_name(xml)
            if args.roundtrip:
                back = encode(xml, args.schemas)
                if back is None:
                    entry["verdict"] = "encode-fail"
                elif back == exi:
                    entry["verdict"] = "ok"
                else:
                    entry["verdict"] = "mismatch"
                    entry["theirHex"] = back
                totals[entry["verdict"]] += 1
            else:
                entry["verdict"] = "decoded"
                totals["decoded"] += 1
        corpus.append(entry)
        if n % 25 == 0:
            print(f"   {n}/{len(unique)} …")

    by_message = Counter(e["message"] for e in corpus)
    print("\n== message types (distinct frames / total occurrences)")
    for name, distinct in by_message.most_common():
        occurrences = sum(e["count"] for e in corpus if e["message"] == name)
        print(f"   {name:34s} {distinct:4d} / {occurrences}")
    print("\n== totals: " + ", ".join(f"{k}={v}" for k, v in totals.items()))

    args.out.write_text(json.dumps({
        "schemaVersion": 1,
        "source": args.pcap.name,
        "protocol": "din-70121",
        "note": "Captured session, not our output. We speak no DIN; decoded by V2Gdecoder "
                "(RISE-V2G + EXIficient) against its schemas_din set.",
        "totalFrames": len(all_frames),
        "distinctFrames": len(unique),
        "verdicts": dict(totals),
        "frames": corpus,
    }, indent=1), encoding="utf-8")
    print(f"   written: {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
