#!/usr/bin/env bash
# Live ISO 15118-20 **AC** interop, plain TCP + EIM, with our SECC using --sdp (no responder shim):
# Josev EVCC (AC, useTls=false) SDP-discovers our SECC and runs a -20 AC session.
set -uo pipefail
REPO=/mnt/c/Users/achim/Desktop/Coding/OpenChargingCloud/Vanaheimr.V2G.Exi
DLL="$REPO/libs/EVSimulatorApp/simulation/Vanaheimr.V2G.Simulation.Cli/bin/Release/net10.0/Vanaheimr.V2G.Simulation.Cli.dll"
SECC_LOG=/tmp/secc-ac.log
EVCC_LOG=/tmp/evcc-ac.log
AC_CFG=/venv/lib/python3.10/site-packages/iso15118/shared/examples/evcc/iso15118_20/evcc_config_ac.json

cleanup() {
  [ -n "${SECC_PID:-}" ] && kill "$SECC_PID" 2>/dev/null
  docker rm -f josev-evcc redis-interop 2>/dev/null
  pkill -f "Simulation.Cli.*secc" 2>/dev/null
}
trap cleanup EXIT
cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1
ss -ulnp 2>/dev/null | grep 15118 && echo "WARN stale 15118" || echo "15118 clean"

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null

echo ">>> our SECC :55000  -20 AC, plain TCP, --sdp (NoTLS)"
dotnet "$DLL" secc --listen 55000 --protocol 20 --mode ac --sdp --interface eth0 >"$SECC_LOG" 2>&1 &
SECC_PID=$!
sleep 3
head -4 "$SECC_LOG"

echo ">>> Josev EVCC (host mode, -20 AC, useTls=false) — SDP-discovers our SECC"
timeout 120 docker run --rm --name josev-evcc --network host \
    -e NETWORK_INTERFACE=eth0 -e SECC_ENFORCE_TLS=False \
    -e EVCC_CONFIG_PATH="$AC_CFG" -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=INFO \
    iso15118-evcc:latest >"$EVCC_LOG" 2>&1
echo ">>> EVCC exited ($?)"
sleep 2

echo "======== OUR SECC (AC) ========"
grep -iE "advertising|listening|Session complete|Plug & Charge|error|exception|abort" "$SECC_LOG" | head -20
echo "======== EVCC (AC flow) ========"
grep -iE "SDPResponse received|Sending SDPRequest|ServiceDiscovery|ACChargeParameter|ACChargeLoop|PowerDelivery|SessionStop|Authorization|error|exception|Traceback|Timeout|Selected" "$EVCC_LOG" | head -30
