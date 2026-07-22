#!/usr/bin/env bash
# Live signed-tariff offer, reverse direction: our SECC (--tariff-cert) offers
# -2:  a two-tuple SAScheduleList whose SalesTariffs are digitally signed into the
#      ChargeParameterDiscoveryRes header (§7.9.2.5) — a Josev EVCC receives it, picks a tuple and
#      answers with a ChargingProfile our SECC validates against the offered PMax;
# -20: a Scheduled-mode ScheduleExchangeRes carrying the rich AbsolutePriceSchedule (power-banded
#      EUR/kWh price rule stacks), signed ECDSA-P521/SHA-512 into the response header.
#
# HONEST VALIDATION LIMIT (the reason this stayed a non-goal so long): Josev does NOT verify tariff
# signatures — its -2 EVCC carries the check as a literal "# TODO ... verify each sales tariff with
# the mobility operator sub 2 certificate", and nothing in its -20 EVCC looks at price-schedule
# signatures. These runs therefore prove (a) our signed offers are schema-valid enough for a real EVCC
# to consume and keep charging, and (b) Josev's tuple choice/ChargingProfile against OUR validation —
# but the signature verification itself only has our own EVCC (loopback/CI) as a checker.
#
# Usage: reverse-tariff-sdp.sh [2|20]     (default: 2; both run AC, plain TCP, EIM)
set -uo pipefail
PROTO="${1:-2}"
MODE=ac
CFGDIR=/venv/lib/python3.10/site-packages/iso15118/shared/examples/evcc
CFG=$([ "$PROTO" = "2" ] && echo "$CFGDIR/iso15118_2/evcc_config_eim_ac.json" || echo "$CFGDIR/iso15118_20/evcc_config_ac.json")
TARIFF=$([ "$PROTO" = "2" ] && echo /tmp/tariff2.p12 || echo /tmp/tariff20.p12)
REPO=/mnt/c/Users/achim/Desktop/Coding/OpenChargingCloud/Vanaheimr.V2G.Exi
DLL="$REPO/Vanaheimr.V2G.Simulation.Cli/bin/Release/net10.0/Vanaheimr.V2G.Simulation.Cli.dll"
SECC_LOG=/tmp/secc-tariff-$PROTO.log
EVCC_LOG=/tmp/evcc-tariff-$PROTO.log

cleanup() { [ -n "${PID:-}" ] && kill "$PID" 2>/dev/null; docker rm -f josev-evcc redis-interop 2>/dev/null; pkill -f "Simulation.Cli.*secc" 2>/dev/null; }
trap cleanup EXIT; cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null
echo ">>> our SECC :55000  -$PROTO $MODE, plain TCP, --sdp --tariff-cert $TARIFF"
dotnet "$DLL" secc --listen 55000 --protocol "$PROTO" --mode "$MODE" --sdp --interface eth0 \
    --tariff-cert "$TARIFF" --tariff-cert-pass 12345 >"$SECC_LOG" 2>&1 &
PID=$!; sleep 3
grep -iE "advertising|Tariff:" "$SECC_LOG"

echo ">>> Josev EVCC ($(basename "$CFG"))"
timeout 120 docker run --rm --name josev-evcc --network host -e NETWORK_INTERFACE=eth0 -e SECC_ENFORCE_TLS=False \
    -e EVCC_CONFIG_PATH="$CFG" -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=INFO \
    iso15118-evcc:latest >"$EVCC_LOG" 2>&1
echo ">>> EVCC exited ($?)"
wait "$PID" 2>/dev/null; PID=
sleep 1

echo "== our SECC =="
grep -E "Tariff:|SmartCharging:|Session complete|Session aborted" "$SECC_LOG"
echo "== Josev EVCC: schedule handling =="
grep -icE "sales.?tariff|price.?schedule" "$EVCC_LOG" | xargs echo "  tariff mentions:"
grep -oE "Sent (PowerDeliveryReq|ChargeParameterDiscoveryReq|ScheduleExchangeReq|ChargingStatusReq|SessionStopReq)" "$EVCC_LOG" 2>/dev/null | sort | uniq -c
echo "== stop reason =="
grep -iE "session terminated|error|Traceback" "$EVCC_LOG" | head -3
