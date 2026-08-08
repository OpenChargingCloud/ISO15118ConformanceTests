#!/usr/bin/env bash
# Live EV-initiated renegotiation, FORWARD direction: our EVCC (-2 AC, --renegotiate) opens
# PowerDeliveryReq(Renegotiate) after the first charging-status cycle against a real Josev SECC, re-runs
# ChargeParameterDiscovery, and completes the session ([V2G2-841]).
#
# Usage: live-evcc-renegotiate.sh [interface]     (default: eth0)
set -uo pipefail
IFACE="${1:-eth0}"
# The repository root, two levels up from this script -- not a path on one particular machine.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$here/../.." && pwd)"
DLL="$REPO/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EVCC/bin/Release/net10.0/WWCP_ISO15118_EVCC.dll"
SECC_LOG=/tmp/josev-secc-reneg.log
EVCC_LOG=/tmp/our-evcc-reneg.log

cleanup() { docker rm -f josev-secc redis-interop 2>/dev/null; }
trap cleanup EXIT; cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null
echo ">>> Josev SECC (host mode, plain TCP)"
docker run -d --rm --name josev-secc --network host -e NETWORK_INTERFACE="$IFACE" \
    -e SECC_ENFORCE_TLS=False -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=INFO \
    iso15118-secc:latest >/dev/null
sleep 8

echo ">>> our EVCC: --sdp discovery, renegotiates after the first cycle"
dotnet "$DLL" --sdp --interface "$IFACE" --protocol 2 --mode ac --renegotiate >"$EVCC_LOG" 2>&1
rc=$?
sleep 1
docker logs josev-secc >"$SECC_LOG" 2>&1

echo ">>> our EVCC exited ($rc)"
echo "== our EVCC =="; grep -E "renegotiations:|Session complete|aborted" "$EVCC_LOG"
echo "== Josev SECC =="
grep -icE "RENEGOTIATE" "$SECC_LOG" | xargs echo "  renegotiate mentions:"
grep -oE "(ChargeParameterDiscoveryReq|PowerDeliveryReq|SessionStopReq) received" "$SECC_LOG" | sort | uniq -c
