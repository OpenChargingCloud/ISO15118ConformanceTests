#!/usr/bin/env python3
"""Regenerate expectedHex for the SupportedAppProtocol test vectors from cbV2G.

Runs each vector's `input` through the cbv2g-ref harness (a thin CLI over
libcbv2g, pinned in tools/cbv2g-ref/CMakeLists.txt) and writes the resulting
wire-conformant EXI hex back into AppProtocol.vectors.json. Also verifies each
vector round-trips (our input -> cbV2G encode -> cbV2G decode -> our input).

Run under WSL/Linux after building the harness:

    python3 tools/regenerate-appprotocol-vectors.py

Environment:
    CBV2G_REF_BIN   path to the cbv2g_ref binary (default: ~/cbv2g-ref-build/cbv2g_ref)
    VECTORS_JSON    path to the vectors file (default: repo copy next to this script)
"""
import json
import os
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
DEFAULT_VECTORS = HERE.parent / "libs" / "EVSimulatorApp" / "libs" / "WWCP_ISO15118" / "WWCP_ISO15118_EXI_Tests" / "Vectors" / "AppProtocol.vectors.json"
DEFAULT_BIN = Path(os.environ.get("CBV2G_REF_BIN", Path.home() / "cbv2g-ref-build" / "cbv2g_ref"))

# cbV2G responseCodeType enum: declaration order in the XSD (== the EXI n-bit index).
CODE_TO_INDEX = {
    "OK_SuccessfulNegotiation": 0,
    "OK_SuccessfulNegotiationWithMinorDeviation": 1,
    "Failed_NoNegotiation": 2,
}
INDEX_TO_CODE = {v: k for k, v in CODE_TO_INDEX.items()}


def run(binary: Path, mode: str, stdin_text: str) -> str:
    proc = subprocess.run(
        [str(binary), mode],
        input=stdin_text.encode("utf-8"),
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if proc.returncode != 0:
        raise RuntimeError(
            f"cbv2g_ref {mode} failed ({proc.returncode}): {proc.stderr.decode('utf-8', 'replace').strip()}"
        )
    return proc.stdout.decode("utf-8").strip()


def encode_input_lines(vector: dict) -> str:
    inp = vector["input"]
    if vector["messageType"] == "SupportedAppProtocolReq":
        lines = ["req"]
        for ap in inp["appProtocols"]:
            lines.append(
                f'{ap["versionNumberMajor"]} {ap["versionNumberMinor"]} '
                f'{ap["schemaId"]} {ap["priority"]} {ap["protocolNamespace"]}'
            )
        return "\n".join(lines) + "\n"
    elif vector["messageType"] == "SupportedAppProtocolRes":
        idx = CODE_TO_INDEX[inp["code"]]
        schema = inp.get("schemaId")
        schema_field = "-" if schema is None else str(schema)
        return f"res\n{idx} {schema_field}\n"
    raise ValueError(f"unknown messageType {vector['messageType']}")


def verify_roundtrip(binary: Path, vector: dict, hex_str: str) -> None:
    decoded = run(binary, "decode", hex_str)
    lines = decoded.splitlines()
    inp = vector["input"]
    if vector["messageType"] == "SupportedAppProtocolReq":
        assert lines[0] == "req", decoded
        aps = inp["appProtocols"]
        assert len(lines) - 1 == len(aps), f"entry count mismatch: {decoded}"
        for ap, line in zip(aps, lines[1:]):
            major, minor, schema, prio, ns = line.split(" ", 4)
            assert int(major) == ap["versionNumberMajor"], line
            assert int(minor) == ap["versionNumberMinor"], line
            assert int(schema) == ap["schemaId"], line
            assert int(prio) == ap["priority"], line
            assert ns == ap["protocolNamespace"], line
    else:
        assert lines[0] == "res", decoded
        idx, schema = lines[1].split(" ", 1)
        assert INDEX_TO_CODE[int(idx)] == inp["code"], decoded
        want = inp.get("schemaId")
        if want is None:
            assert schema == "-", decoded
        else:
            assert int(schema) == want, decoded


def main() -> int:
    binary = DEFAULT_BIN
    vectors_path = Path(os.environ.get("VECTORS_JSON", DEFAULT_VECTORS))
    if not binary.exists():
        print(f"error: harness binary not found at {binary}; build it first "
              f"(tools/cbv2g-ref/build.sh)", file=sys.stderr)
        return 1

    doc = json.loads(vectors_path.read_text(encoding="utf-8"))
    sha = doc["referenceEncoder"]["commit"]

    for v in doc["vectors"]:
        hex_str = run(binary, "encode", encode_input_lines(v))
        verify_roundtrip(binary, v, hex_str)
        v["expectedHex"] = hex_str
        v["expectedBytes"] = len(hex_str.split())
        print(f"  {v['name']:32s} {v['expectedBytes']:3d} B  {hex_str}")

    doc["generator"] = f"cbV2G@{sha[:12]}"
    doc["generatedAtUtc"] = datetime.now(timezone.utc).isoformat()

    vectors_path.write_text(json.dumps(doc, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"\nWrote {len(doc['vectors'])} vectors to {vectors_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
