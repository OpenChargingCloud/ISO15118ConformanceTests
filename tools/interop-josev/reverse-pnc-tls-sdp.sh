#!/usr/bin/env bash
# Live reverse PnC over TLS with our SECC using its OWN WWCP SDP server (--sdp), NO Python responder shim.
# Confirms whether the WWCP SDP multicast interface binding works end-to-end with a real Josev EVCC.
set -uo pipefail
REPO=/mnt/c/Users/achim/Desktop/Coding/OpenChargingCloud/Vanaheimr.V2G.Exi
DLL="$REPO/EVSimulatorApp/simulation/Vanaheimr.V2G.Simulation.Cli/bin/Release/net10.0/Vanaheimr.V2G.Simulation.Cli.dll"
SECC_LOG=/tmp/secc-sdp.log
EVCC_LOG=/tmp/evcc-sdp.log

cleanup() {
  [ -n "${SECC_PID:-}" ] && kill "$SECC_PID" 2>/dev/null
  docker rm -f josev-evcc redis-interop 2>/dev/null
  pkill -f "Simulation.Cli.*secc" 2>/dev/null
}
trap cleanup EXIT
cleanup

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null

echo ">>> our SECC on :55000 with WWCP --sdp (no python responder), TLS + require client cert"
dotnet "$DLL" secc --listen 55000 --protocol 20 --mode dc --sdp --interface eth0 \
    --tls-backend dotnet --server-cert /tmp/secc.p12 --server-cert-pass 12345 --require-client-cert \
    >"$SECC_LOG" 2>&1 &
SECC_PID=$!
sleep 3
head -5 "$SECC_LOG"

echo ">>> launching Josev EVCC (host mode, TLS 1.3, PnC) — it must SDP-discover our SECC"
timeout 120 docker run --rm --name josev-evcc --network host \
    -e NETWORK_INTERFACE=eth0 -e ENABLE_TLS_1_3=True -e SECC_ENFORCE_TLS=True \
    -e EVCC_CONFIG_PATH=/tmp/evcc_config_dc_tls.json -e REDIS_HOST=localhost -e REDIS_PORT=6379 \
    -e LOG_LEVEL=INFO \
    -v /tmp/evcc_config_dc_tls.json:/tmp/evcc_config_dc_tls.json:ro \
    iso15118-evcc:latest >"$EVCC_LOG" 2>&1
echo ">>> EVCC exited ($?)"
sleep 2

echo "======== OUR SECC (SDP + verdict) ========"
grep -iE "advertising|SDP|listening|Plug & Charge|Session complete|signature" "$SECC_LOG" | head -20
echo "======== EVCC SDP/discovery lines ========"
grep -iE "SDP|discover|SECC found|No SECC|multicast|Sent SDP|Timeout|matching|security" "$EVCC_LOG" | head -20
