#!/usr/bin/env bash
# Live smart-charging EVCC, FORWARD direction (-2 AC, EIM): our EVCC reads whatever SASchedule offer a
# real Josev SECC makes, reports the tariff verdict (Josev never signs its tariffs — expect "signature
# absent"), picks the cheapest tuple, shapes its ChargingProfile to that tuple's PMaxSchedule, and sends
# it in PowerDeliveryReq(Start) — which Josev's SECC validates against its own offer. That external
# profile validation is the real value of this run; the signature path has no external checker (see
# reverse-tariff-sdp.sh for the honest validation limit).
#
# Usage: live-evcc-tariff.sh [interface]     (default: eth0)
set -uo pipefail
IFACE="${1:-eth0}"
# The repository root, two levels up from this script -- not a path on one particular machine.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$here/../.." && pwd)"
DLL="$REPO/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_CLI/bin/Release/net10.0/WWCP_ISO15118_CLI.dll"
SECC_LOG=/tmp/josev-secc-tariff.log
EVCC_LOG=/tmp/our-evcc-tariff.log

cleanup() { docker rm -f josev-secc redis-interop 2>/dev/null; }
trap cleanup EXIT; cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null
echo ">>> Josev SECC (host mode, plain TCP)"
docker run -d --rm --name josev-secc --network host -e NETWORK_INTERFACE="$IFACE" \
    -e SECC_ENFORCE_TLS=False -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=INFO \
    iso15118-secc:latest >/dev/null
sleep 8

echo ">>> our EVCC: --sdp discovery, evaluates the offer and sends a shaped ChargingProfile"
dotnet "$DLL" evcc --sdp --interface "$IFACE" --protocol 2 --mode ac >"$EVCC_LOG" 2>&1
rc=$?
sleep 1
docker logs josev-secc >"$SECC_LOG" 2>&1

echo ">>> our EVCC exited ($rc)"
echo "== our EVCC =="; grep -E "SDP:|Tariff:|Session complete|aborted" "$EVCC_LOG"
echo "== Josev SECC =="
grep -icE "charg(e|ing).?profile" "$SECC_LOG" | xargs echo "  charging-profile mentions:"
grep -iE "sales.?tariff" "$SECC_LOG" | head -2
grep -oE "(ChargeParameterDiscoveryReq|PowerDeliveryReq|SessionStopReq) received" "$SECC_LOG" | sort | uniq -c
grep -iE "invalid|error|Traceback" "$SECC_LOG" | head -3
