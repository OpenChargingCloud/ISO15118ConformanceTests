#!/usr/bin/env python3
"""Attribute the -20 length mismatches to the EXI value partition, by substitution rather than by eye.

The claim
---------
Every frame in the -20 corpus that EXIficient re-encodes to a *different* length is said to differ for
one reason: EXI keeps a string table (§7.3.3), and a value written a second time can be sent as a
compact identifier instead of a literal. EXIficient does that; our encoder is deliberately miss-only
and writes the literal again. If that is the whole story, the difference must equal exactly what those
repeats cost us.

"Must equal" is testable, and inference is not good enough — the same claim was made for `-2` and only
became a finding once it was measured. This does for -20 what
`ISO15118ConformanceTests.Simulation/Interop/ExiStringTableTests.cs` records for -2.

The experiment
--------------
Our encoder never emits a partition hit, so replacing a repeated value with a *different value of the
same length* cannot change our output length. It changes theirs: the repeat is gone, so the second
occurrence costs a literal again. Therefore

    len(their encoding of the substituted document)  ==  len(our original frame)

if and only if the repeats account for the whole difference. Any residue is something else, and the
size of the residue is the finding.

Three encodings per frame, all EXIficient's, so nothing of ours is in the measurement except the
frame we started from:

    ours            our bytes, from the session trace
    theirs          their encoding of the document they read out of ours   (reproduces the mismatch)
    theirs-subst    their encoding of the same document with every repeat made unique, same lengths

Usage
-----
    python3 valuepartition20.py [--repo <path>]

Reads the -20 session traces, finds the frames whose length EXIficient changes, and reports the
arithmetic for each. Needs the same rig as `roundtrip20.py`.
"""

import argparse
import json
import string
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from roundtrip20 import BY_PAYLOAD_TYPE, JAR, Exificient, V2GTP_HEADER_BYTES   # noqa: E402


def frames_of(path: Path):
    """Every -20 frame in a session trace, as (label, schema, hex)."""
    doc = json.loads(path.read_text(encoding="utf-8-sig"))
    if doc.get("protocol") != "iso15118-20":
        return []
    out = []
    for exchange in doc["exchanges"]:
        for side in ("request", "response"):
            item = exchange.get(side)
            if not item or not item.get("frame"):
                continue
            schema = BY_PAYLOAD_TYPE.get(str(item.get("payloadType", "")).lower())
            if schema is None:
                continue
            out.append((f"{path.name}#{exchange['index']:02d}.{side[:3]} {item.get('message', '?')}",
                        schema, item["frame"][V2GTP_HEADER_BYTES * 2:]))
    return out


def repeated_values(xml: str):
    """Values that appear more than once in the document, longest first.

    Both attribute values and element text: EXI tries the qname-keyed local partition and then the
    global one, so a repeat anywhere in the document is a candidate for a compact identifier. Only
    values of two characters or more — a one-character literal is not obviously cheaper than an
    identifier, and reading a difference into it would be exactly the kind of inference this script
    exists to avoid.
    """
    root = ET.fromstring(xml)
    seen: dict[str, int] = {}
    for element in root.iter():
        for value in list(element.attrib.values()) + [(element.text or "").strip()]:
            if len(value) >= 2:
                seen[value] = seen.get(value, 0) + 1
    return sorted(((v, n) for v, n in seen.items() if n > 1), key=lambda p: -len(p[0]))


ALPHABET = string.ascii_uppercase + string.digits


def vary(value: str, nth: int) -> str:
    """A different string of the same length — and, for base64, of the same *decoded* length.

    Equal length in characters is what holds our side fixed. For an `xs:base64Binary` value that is
    not enough: EXI encodes the decoded octets, whose count depends on the trailing `=` padding. The
    first version of this script replaced the last four characters of a 400-character certificate,
    padding included, which lengthened the binary by two bytes and showed up as a two-byte "residue"
    that was entirely the measurement's own doing. Vary the characters *before* any padding instead,
    and the decoded length cannot move.
    """
    body = value.rstrip("=")
    pad = value[len(body):]
    width = min(4, len(body))
    tail = ALPHABET[nth % len(ALPHABET)] * width
    return body[:len(body) - width] + tail + pad


def substitute(xml: str, value: str) -> str:
    """Replace occurrences of `value` after the first with same-length unique strings.

    The first occurrence stays: it is the literal both encoders write. What is being removed is the
    *repeat*, so that their encoder has no partition hit left to take.
    """
    parts = xml.split(value)
    if len(parts) < 3:
        return xml
    out = [parts[0]]
    for i, part in enumerate(parts[1:]):
        out.append(value if i == 0 else vary(value, i - 1))
        out.append(part)
    return "".join(out)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--repo", type=Path, default=Path.cwd())
    args = ap.parse_args()

    schema_root = args.repo / "libs" / "EVSimulatorApp" / "libs" / "WWCP_ISO15118"
    if not JAR.exists():
        raise SystemExit(f"no jar at {JAR} — run tools/interop-v2gdecoder/setup.sh")

    traces = sorted((args.repo / "ISO15118ConformanceTests.Simulation" / "Vectors")
                    .glob("Session.iso20-*.trace.json"))
    frames = [f for t in traces for f in frames_of(t)]
    print(f"== {len(frames)} -20 frames across {len(traces)} traces")

    exi = Exificient(schema_root, Path.home() / "v2gdec" / "exificient-work")

    # Round 1: which frames does their encoder shorten, and what did they read?
    verdicts = exi.run([(label, schema, hexstr) for label, schema, hexstr in frames])
    xml_jobs, mismatched = [], []
    for label, schema, hexstr in frames:
        verdict, their_hex, _ = verdicts.get(label, ("?", "", ""))
        if verdict != "mismatch":
            continue
        mismatched.append((label, schema, hexstr, their_hex))
        xml_jobs.append((label, schema, "?" + hexstr))

    if not mismatched:
        print("   no length mismatches — nothing to attribute")
        return 0

    print(f"== {len(mismatched)} mismatching frames; asking for the documents they read")
    docs = exi.run(xml_jobs)

    # Round 2: encode each document as read, and again with every repeat made unique.
    probes, plan = [], []
    for label, schema, ours_hex, their_hex in mismatched:
        verdict, xml, detail = docs.get(label, ("?", "", ""))
        if verdict != "decoded":
            print(f"   [SKIP] {label}: could not read the document back ({detail})")
            continue
        repeats = repeated_values(xml)
        substituted = xml
        for value, _ in repeats:
            substituted = substitute(substituted, value)
        plan.append((label, ours_hex, their_hex, repeats))
        probes.append((label + " |asis", schema, xml))
        probes.append((label + " |subst", schema, substituted))
        # And one probe per repeated value on its own. The sum of the individual savings need not
        # equal the total — but where it does not, the difference is worth seeing rather than
        # dividing up by assumption. It is also the only way to find a repeat their encoder does
        # *not* take a hit on, which is what the AuthorizationReq certificate turned out to be.
        if len(repeats) > 1:
            for k, (value, _) in enumerate(repeats):
                probes.append((f"{label} |only{k}", schema, substitute(xml, value)))

    results = exi.run(probes)

    print()
    for label, ours_hex, their_hex, repeats in plan:
        ours = len(ours_hex) // 2
        theirs = len(their_hex) // 2
        asis = results.get(label + " |asis", ("?", "", ""))
        subst = results.get(label + " |subst", ("?", "", ""))

        print(f"-- {label}")
        for k, (value, n) in enumerate(repeats):
            shown = value if len(value) <= 56 else value[:53] + "..."
            only = results.get(f"{label} |only{k}", None)
            saved = ""
            if only and only[0] == "encoded":
                # Removing this repeat alone costs them (len(only) - theirs) bytes, which is what the
                # hit on it was worth.
                saved = f"  -> that repeat is worth {len(only[1]) // 2 - theirs} B to them"
            print(f"     repeated x{n}, {len(value):3d} chars: {shown}{saved}")
        cost = sum(len(v) * (n - 1) for v, n in repeats)

        if asis[0] != "encoded":
            print(f"     [!] re-encoding the document as read failed: {asis[2]}")
            continue
        asis_len = len(asis[1]) // 2
        note = "" if asis_len == theirs else f"  [!] expected {theirs}, the run's own number"
        print(f"     ours {ours} B, theirs {theirs} B, delta {ours - theirs}")
        print(f"     repeats cost us {cost} B if each second occurrence is a literal")
        print(f"     their re-encode of the document as read: {asis_len} B{note}")

        if subst[0] != "encoded":
            print(f"     [!] substituted document does not encode: {subst[2]}")
            continue
        subst_len = len(subst[1]) // 2
        residue = ours - subst_len
        verdict = "ACCOUNTED FOR" if residue == 0 else f"RESIDUE {residue} B — not the partition"
        print(f"     substituted: {subst_len} B against our {ours} B  ->  {verdict}")
        print()

    return 0


if __name__ == "__main__":
    sys.exit(main())
