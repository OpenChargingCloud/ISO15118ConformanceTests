#!/usr/bin/env python3
"""Extract the Ed448 test vectors from RFC 8032 section 7.4 into JSON.

Parsed from the RFC text rather than transcribed, because a 114-byte signature
retyped by hand (or by a language model) is a test that passes against the wrong
answer.
"""
import json
import re
import sys

src = open(sys.argv[1]).read().split("\n")

# Section 7.4 only: Ed448 pure. 7.5 is Ed448ph, a different algorithm.
start = next(i for i, l in enumerate(src) if l.startswith("7.4."))
end   = next(i for i, l in enumerate(src) if l.startswith("7.5."))
lines = src[start + 1:end]

# Page furniture: form feeds, running headers, footers, blank runs.
lines = [l for l in lines
         if not l.startswith("\f")
         and "Josefsson & Liusvaara" not in l
         and not l.startswith("RFC 8032")]

vectors, cur, field = [], None, None

for line in lines:
    s = line.strip()
    if s.startswith("-----"):
        if cur and cur.get("signature"):
            vectors.append(cur)
        label = s[5:].strip()
        cur   = {"label": label or "(final)", "context": ""}
        field = None
        continue
    if cur is None:
        continue

    m = re.match(r"^(ALGORITHM|SECRET KEY|PUBLIC KEY|MESSAGE|CONTEXT|SIGNATURE)"
                 r"(?: \(length (\d+) bytes?\))?:$", s)
    if m:
        field = {"ALGORITHM": "algorithm", "SECRET KEY": "secretKey",
                 "PUBLIC KEY": "publicKey", "MESSAGE": "message",
                 "CONTEXT": "context", "SIGNATURE": "signature"}[m.group(1)]
        cur.setdefault(field, "")
        if m.group(2) is not None:
            cur["messageLength"] = int(m.group(2))
        continue

    if field and s:
        cur[field] = cur.get(field, "") + s

if cur and cur.get("signature"):
    vectors.append(cur)

# Every field except the label is hex; the algorithm is a name.
for v in vectors:
    assert v["algorithm"] == "Ed448", v["algorithm"]
    for k in ("secretKey", "publicKey", "message", "context", "signature"):
        v.setdefault(k, "")
        assert re.fullmatch(r"[0-9a-f]*", v[k]), (v["label"], k, v[k][:40])
    assert len(v["secretKey"]) == 57 * 2, (v["label"], len(v["secretKey"]))
    assert len(v["publicKey"]) == 57 * 2, (v["label"], len(v["publicKey"]))
    assert len(v["signature"]) == 114 * 2, (v["label"], len(v["signature"]))
    if "messageLength" in v:
        assert len(v["message"]) == v["messageLength"] * 2, \
            (v["label"], v["messageLength"], len(v["message"]) // 2)
    del v["algorithm"]

print(json.dumps({
    "schemaVersion": 1,
    "generator": "RFC 8032 section 7.4, extracted from https://www.rfc-editor.org/rfc/rfc8032.txt",
    "generatorNote":
        "Published test vectors for **pure Ed448** (RFC 8032 section 7.4). Section 7.5 is Ed448ph, "
        "a different algorithm with different vectors, and is deliberately not included: "
        "ISO 15118-20's signature method URI is http://www.w3.org/2021/04/xmldsig-more#eddsa-ed448, "
        "which RFC 9231 section 2.3.12 lists separately from #eddsa-ed448ph. "
        "Extracted mechanically from the RFC text by tools/rfc8032-ref/extract.py and never retyped: "
        "a 114-byte signature transcribed by hand is a test that passes against the wrong answer. "
        "Unlike the cbV2G corpus these are not one implementation's output but the standard's own "
        "numbers, so they are the strongest oracle in this repository.",
    "vectors": vectors,
}, indent=2))
