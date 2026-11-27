#!/usr/bin/env bash
# Decode the CurrentDemandRes frames of a recorded -2 DC session that carry MeterInfo, with an EXI codec
# that is not ours (V2Gdecoder: RISE-V2G + EXIficient), as the 2026-08-11 metering-receipt run did.
#   meterinfo-decode.sh <trace.json>
set -eu
TRACE=$1
cd "${V2GDEC_DIR:-$HOME/v2gdec/V2Gdecoder}"
JAR="${V2GDEC_JAR:-$HOME/v2gdec/decoder.jar}"
python3 -I - "$TRACE" <<'PY' > /tmp/cdres.txt
import json, sys
t = json.load(open(sys.argv[1]))
seen = {}
for x in t["exchanges"]:
    r = x.get("response") or {}
    m = r.get("message", "")
    if m.startswith(("CurrentDemandRes", "ChargingStatusRes", "MeteringReceiptRes")) or (x.get("request") or {}).get("message", "").startswith("MeteringReceiptReq"):
        for side in ("request", "response"):
            f = (x.get(side) or {}).get("frame", "")
            msg = (x.get(side) or {}).get("message", "")
            if not f: continue
            n = len(f) // 2
            key = (msg, n)
            if key not in seen:
                seen[key] = (x["index"], f)
for (msg, n), (i, f) in sorted(seen.items(), key=lambda kv: kv[1][0]):
    print(i, msg, n, f[16:])
PY
cat /tmp/cdres.txt | awk '{print "frame", $1, $2, $3, "B"}'
while read -r idx msg n hex; do
  case "$msg" in CurrentDemandReq*|ChargingStatusReq*) continue;; esac
  echo "########## [$idx] $msg, $n B on the wire ##########"
  java -jar "$JAR" -e -s "$hex" 2>/dev/null | sed 's/></>\n</g' | grep -iE "MeterInfo|MeterID|MeterReading|SigMeterReading|MeterStatus|TMeter|ReceiptRequired|ResponseCode|Signature|DigestValue" | head -20
done < /tmp/cdres.txt
