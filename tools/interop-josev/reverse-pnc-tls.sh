#!/usr/bin/env bash
# Live reverse Plug & Charge over TLS: Josev EVCC -> our SECC. Confirms our SECC now VERIFIES Josev's
# PnC SignedInfo signature via the standalone-xmldsig grammar fallback (expect: signature OK, grammar=xmldsig-standalone).
set -uo pipefail

# The repository root, two levels up from this script -- not a path on one particular machine.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$here/../.." && pwd)"
CLI="$REPO/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_CLI"
SECC_LOG=/tmp/secc-pnc2.log
SDP_LOG=/tmp/sdp-pnc2.log
EVCC_LOG=/tmp/evcc-pnc2.log
PORT=55000

cleanup() {
  echo ">>> cleanup"
  [ -n "${SECC_PID:-}" ] && kill "$SECC_PID" 2>/dev/null
  [ -n "${SDP_PID:-}" ] && kill "$SDP_PID" 2>/dev/null
  docker rm -f josev-evcc redis-interop 2>/dev/null
  pkill -f "WWCP_ISO15118_CLI.*secc" 2>/dev/null
}
trap cleanup EXIT
cleanup

echo ">>> building CLI under WSL"
dotnet build "$CLI" -c Release --nologo >/tmp/cli-build.log 2>&1 || { echo "BUILD FAILED"; tail -20 /tmp/cli-build.log; exit 1; }
DLL="$CLI/bin/Release/net10.0/WWCP_ISO15118_CLI.dll"
ls -la "$DLL" || exit 1

echo ">>> starting redis (host net)"
docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null

echo ">>> starting our SECC on :$PORT (TLS, require client cert)"
dotnet "$DLL" secc --listen "$PORT" --protocol 20 --mode dc \
    --tls-backend dotnet --server-cert /tmp/secc.p12 --server-cert-pass 12345 --require-client-cert \
    >"$SECC_LOG" 2>&1 &
SECC_PID=$!
sleep 3
head -3 "$SECC_LOG"

echo ">>> starting SDP responder (eth0, TLS)"
python3 "$REPO/tools/interop-josev/sdp-responder.py" eth0 "$PORT" tls >"$SDP_LOG" 2>&1 &
SDP_PID=$!
sleep 2
head -2 "$SDP_LOG"

echo ">>> launching Josev EVCC (host mode, TLS 1.3, PnC)"
timeout 120 docker run --rm --name josev-evcc --network host \
    -e NETWORK_INTERFACE=eth0 -e ENABLE_TLS_1_3=True -e SECC_ENFORCE_TLS=True \
    -e EVCC_CONFIG_PATH=/tmp/evcc_config_dc_tls.json -e REDIS_HOST=localhost -e REDIS_PORT=6379 \
    -e LOG_LEVEL=INFO \
    -v /tmp/evcc_config_dc_tls.json:/tmp/evcc_config_dc_tls.json:ro \
    iso15118-evcc:latest >"$EVCC_LOG" 2>&1
echo ">>> EVCC exited ($?)"

sleep 2
echo "======== OUR SECC PnC VERDICT ========"
grep -iE "listening|Plug & Charge|Session complete|signature|grammar" "$SECC_LOG" || tail -20 "$SECC_LOG"
echo "======== EVCC tail ========"
grep -iE "SDP|SECC|TLS|Plug|PnC|Authoriz|selected|complete|error|Exception" "$EVCC_LOG" | tail -25
