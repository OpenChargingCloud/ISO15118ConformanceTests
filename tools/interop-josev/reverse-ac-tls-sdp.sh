#!/usr/bin/env bash
# Live ISO 15118-20 **AC over mutual TLS 1.3** with our SECC using --sdp (no shim):
# Josev EVCC (AC, useTls=true) SDP-discovers our SECC and runs a -20 AC session over TLS.
set -uo pipefail
# The repository root, two levels up from this script -- not a path on one particular machine.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$here/../.." && pwd)"
DLL="$REPO/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_SECC/bin/Release/net10.0/WWCP_ISO15118_SECC.dll"
SECC_LOG=/tmp/secc-ac-tls.log
EVCC_LOG=/tmp/evcc-ac-tls.log
AC_TLS_CFG=/tmp/evcc_config_ac_tls.json

cleanup() {
  [ -n "${SECC_PID:-}" ] && kill "$SECC_PID" 2>/dev/null
  docker rm -f josev-evcc redis-interop 2>/dev/null
  pkill -f "Simulation.Cli.*secc" 2>/dev/null
}
trap cleanup EXIT
cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1
ss -ulnp 2>/dev/null | grep 15118 && echo "WARN stale 15118" || echo "15118 clean"

# AC + TLS EVCC config (baked example is useTls=false; flip it on).
cat > "$AC_TLS_CFG" <<'JSON'
{
	"supportedProtocols": ["ISO_15118_20_AC"],
	"supportedEnergyServices": ["AC"],
	"isCertInstallNeeded": false,
	"useTls": true
}
JSON

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null

echo ">>> our SECC :55000  -20 AC over TLS (--sdp, require client cert)"
dotnet "$DLL" --listen 55000 --protocol 20 --mode ac --sdp --interface eth0 \
   --tls-backend dotnet --server-cert /tmp/secc.p12 --server-cert-pass 12345 --require-client-cert \
    >"$SECC_LOG" 2>&1 &
SECC_PID=$!
sleep 3
head -4 "$SECC_LOG"

echo ">>> Josev EVCC (host mode, -20 AC, TLS 1.3) — SDP-discovers our SECC"
timeout 120 docker run --rm --name josev-evcc --network host \
    -e NETWORK_INTERFACE=eth0 -e ENABLE_TLS_1_3=True -e SECC_ENFORCE_TLS=True \
    -e EVCC_CONFIG_PATH="$AC_TLS_CFG" -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=INFO \
    -v "$AC_TLS_CFG:$AC_TLS_CFG:ro" \
    iso15118-evcc:latest >"$EVCC_LOG" 2>&1
echo ">>> EVCC exited ($?)"
sleep 2

echo "======== OUR SECC (AC/TLS) ========"
grep -iE "Presenting|advertising|listening|Session complete|Plug & Charge|error|exception|abort" "$SECC_LOG" | head -20
echo "======== EVCC (AC/TLS flow) ========"
grep -iE "SDPResponse received|Sending SDPRequest|TLS|ServiceDiscoveryRes|ACChargeParameter|ACChargeLoop|PowerDeliveryRes|SessionStop|error|exception|Traceback|Timeout" "$EVCC_LOG" | head -30
