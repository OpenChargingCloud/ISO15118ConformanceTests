#!/usr/bin/env bash
# Live ISO 15118-20 **bidirectional (BPT)** interop, plain TCP + --sdp: a Josev BPT EVCC discovers our SECC
# and runs a DC_BPT or AC_BPT session (charge + discharge). Our SECC advertises both the unidirectional and
# BPT energy-transfer services and replies with BPT energy-transfer-modes/control-modes when the EV sends them.
#
# Usage: reverse-bpt-sdp.sh [dc|ac]   (default: dc)
set -uo pipefail
MODE="${1:-dc}"
REPO=/mnt/c/Users/achim/Desktop/Coding/OpenChargingCloud/Vanaheimr.V2G.Exi
DLL="$REPO/EVSimulatorApp/simulation/Vanaheimr.V2G.Simulation.Cli/bin/Release/net10.0/Vanaheimr.V2G.Simulation.Cli.dll"
CFG="/venv/lib/python3.10/site-packages/iso15118/shared/examples/evcc/iso15118_20/evcc_config_${MODE}_bpt.json"
SECC_LOG=/tmp/secc-${MODE}-bpt.log
EVCC_LOG=/tmp/evcc-${MODE}-bpt.log

cleanup() { [ -n "${PID:-}" ] && kill "$PID" 2>/dev/null; docker rm -f josev-evcc redis-interop 2>/dev/null; pkill -f "Simulation.Cli.*secc" 2>/dev/null; }
trap cleanup EXIT; cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null
echo ">>> our SECC :55000  -20 ${MODE^^} (bidirectional), plain TCP, --sdp"
dotnet "$DLL" secc --listen 55000 --protocol 20 --mode "$MODE" --sdp --interface eth0 >"$SECC_LOG" 2>&1 &
PID=$!; sleep 3
grep -i advertising "$SECC_LOG"

echo ">>> Josev ${MODE^^}_BPT EVCC (${CFG##*/}) — SDP-discovers our SECC"
timeout 120 docker run --rm --name josev-evcc --network host -e NETWORK_INTERFACE=eth0 -e SECC_ENFORCE_TLS=False \
    -e EVCC_CONFIG_PATH="$CFG" -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=INFO \
    iso15118-evcc:latest >"$EVCC_LOG" 2>&1
echo ">>> EVCC exited ($?)"; sleep 1

echo "== SECC =="; grep -iE "Session complete|error|exception|abort" "$SECC_LOG" | head
echo "== services offered =="; grep -oE "\"Service\":\[[^]]*\]" "$EVCC_LOG" | head -1
echo "== service selected =="; grep -oE "SelectedEnergyTransferService[^}]*ServiceID[^,}]*" "$EVCC_LOG" | head -1
echo "== states =="; grep -oE "Entered state [A-Za-z]+" "$EVCC_LOG" | sort -u
echo "== stop reason =="; grep -iE "WrongServiceID|session terminated|Requesting SessionStop" "$EVCC_LOG" | head -2
