#!/usr/bin/env bash
# Live ISO 15118-20 **Dynamic control mode** interop, plain TCP + --sdp: our SECC (run with --dynamic)
# advertises the Dynamic (ControlMode=2) parameter set first; a Josev EVCC adopts the first offered set's
# ControlMode, so the session runs ScheduleExchange + charge loop in Dynamic mode — exercising the
# Dynamic_SEResControlMode and (BPT_)Dynamic_*_CLResControlMode answer-in-kind paths.
#
# Usage: reverse-dynamic-sdp.sh [dc|ac|dc-bpt|ac-bpt]   (default: dc)
set -uo pipefail
VARIANT="${1:-dc}"
case "$VARIANT" in
  dc)     MODE=dc; CFGNAME=evcc_config_dc.json;;
  ac)     MODE=ac; CFGNAME=evcc_config_ac.json;;
  dc-bpt) MODE=dc; CFGNAME=evcc_config_dc_bpt.json;;
  ac-bpt) MODE=ac; CFGNAME=evcc_config_ac_bpt.json;;
  *) echo "usage: $0 [dc|ac|dc-bpt|ac-bpt]" >&2; exit 2;;
esac
REPO=/mnt/c/Users/achim/Desktop/Coding/OpenChargingCloud/Vanaheimr.V2G.Exi
DLL="$REPO/Vanaheimr.V2G.Simulation.Cli/bin/Release/net10.0/Vanaheimr.V2G.Simulation.Cli.dll"
CFG="/venv/lib/python3.10/site-packages/iso15118/shared/examples/evcc/iso15118_20/$CFGNAME"
SECC_LOG=/tmp/secc-${VARIANT}-dynamic.log
EVCC_LOG=/tmp/evcc-${VARIANT}-dynamic.log

cleanup() { [ -n "${PID:-}" ] && kill "$PID" 2>/dev/null; docker rm -f josev-evcc redis-interop 2>/dev/null; pkill -f "Simulation.Cli.*secc" 2>/dev/null; }
trap cleanup EXIT; cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null
echo ">>> our SECC :55000  -20 ${MODE^^} (--dynamic: Dynamic control mode offered first), plain TCP, --sdp"
dotnet "$DLL" secc --listen 55000 --protocol 20 --mode "$MODE" --dynamic --sdp --interface eth0 >"$SECC_LOG" 2>&1 &
PID=$!; sleep 3
grep -i advertising "$SECC_LOG"

echo ">>> Josev EVCC ($CFGNAME) — SDP-discovers our SECC, adopts Dynamic from parameter set"
timeout 120 docker run --rm --name josev-evcc --network host -e NETWORK_INTERFACE=eth0 -e SECC_ENFORCE_TLS=False \
    -e EVCC_CONFIG_PATH="$CFG" -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=INFO \
    iso15118-evcc:latest >"$EVCC_LOG" 2>&1
echo ">>> EVCC exited ($?)"; sleep 1

echo "== control mode =="; grep -iE "Selected Control Mode" "$EVCC_LOG" | head -1
echo "== SECC =="; grep -iE "Session complete|error|exception|abort" "$SECC_LOG" | head
echo "== states =="; grep -oE "Entered state [A-Za-z]+" "$EVCC_LOG" | sort -u
echo "== errors =="; grep -iE "error|exception|Traceback" "$EVCC_LOG" | head -5
echo "== stop reason =="; grep -iE "WrongServiceID|session terminated|Requesting SessionStop" "$EVCC_LOG" | head -2
