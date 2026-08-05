#!/usr/bin/env bash
# Live SECC-triggered renegotiation, reverse direction: our SECC (--renegotiate) notifies once mid-loop —
# -2: EVSENotification.ReNegotiation in ChargingStatusRes → a Josev EVCC answers
#     PowerDeliveryReq(Renegotiate) and re-runs ChargeParameterDiscovery ([V2G2-841]);
# -20: EvseNotification.ServiceRenegotiation in the ChargeLoopRes EVSEStatus → Josev answers
#     PowerDelivery(Stop) + SessionStopReq(ServiceRenegotiation) and re-enters ServiceDiscovery
#     ([V2G20-1477]) — the session must NOT end there, and completes on the second round.
#
# Usage: reverse-renegotiate-sdp.sh [2|20]     (default: 2; both run AC)
#
# Both protocols run AC: Josev's -20 DC stop path detours through DCWeldingDetection, whose state builds
# the SessionStopReq with a hardcoded Terminate — only its AC path carries charging_session_stop_v20
# (= SERVICE_RENEGOTIATION) into the SessionStopReq. The -20 DC renegotiation is thus a Josev gap.
set -uo pipefail
PROTO="${1:-2}"
MODE=ac
CFGDIR=/venv/lib/python3.10/site-packages/iso15118/shared/examples/evcc
CFG=$([ "$PROTO" = "2" ] && echo "$CFGDIR/iso15118_2/evcc_config_eim_ac.json" || echo "$CFGDIR/iso15118_20/evcc_config_ac.json")
REPO=/mnt/c/Users/achim/Desktop/Coding/OpenChargingCloud/Vanaheimr.V2G.Exi
DLL="$REPO/EVSimulatorApp/simulation/Vanaheimr.V2G.Simulation.Cli/bin/Release/net10.0/Vanaheimr.V2G.Simulation.Cli.dll"
SECC_LOG=/tmp/secc-reneg-$PROTO.log
EVCC_LOG=/tmp/evcc-reneg-$PROTO.log

cleanup() { [ -n "${PID:-}" ] && kill "$PID" 2>/dev/null; docker rm -f josev-evcc redis-interop 2>/dev/null; pkill -f "Simulation.Cli.*secc" 2>/dev/null; }
trap cleanup EXIT; cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null
echo ">>> our SECC :55000  -$PROTO $MODE, plain TCP, --sdp --renegotiate"
dotnet "$DLL" secc --listen 55000 --protocol "$PROTO" --mode "$MODE" --renegotiate --sdp --interface eth0 >"$SECC_LOG" 2>&1 &
PID=$!; sleep 3
grep -i advertising "$SECC_LOG"

echo ">>> Josev EVCC ($(basename "$CFG"))"
timeout 120 docker run --rm --name josev-evcc --network host -e NETWORK_INTERFACE=eth0 -e SECC_ENFORCE_TLS=False \
    -e EVCC_CONFIG_PATH="$CFG" -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=INFO \
    iso15118-evcc:latest >"$EVCC_LOG" 2>&1
echo ">>> EVCC exited ($?)"
wait "$PID" 2>/dev/null; PID=
sleep 1

echo "== our SECC =="
grep -E "Renegotiation cycles|Session complete|Session aborted" "$SECC_LOG"
echo "== Josev EVCC: the renegotiation =="
grep -icE "RENEGOTIATE|SERVICE_RENEGOTIATION" "$EVCC_LOG" | xargs echo "  renegotiate mentions:"
grep -oE "Sent (PowerDeliveryReq|ChargeParameterDiscoveryReq|ServiceDiscoveryReq|SessionStopReq|DC_ChargeParameterDiscoveryReq)" "$EVCC_LOG" 2>/dev/null | sort | uniq -c
echo "== stop reason =="
grep -iE "session terminated|error|Traceback" "$EVCC_LOG" | head -3
