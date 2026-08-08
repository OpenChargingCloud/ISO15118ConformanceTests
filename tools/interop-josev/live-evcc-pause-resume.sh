#!/usr/bin/env bash
# Live pause/resume, FORWARD direction: our EVCC runs a session against a real Josev SECC, ends it with
# ChargingSession.Pause, then re-discovers via SDP (Josev tears down its TCP server on pause and resumes
# the UDP SDP server — the resumed session lands on a NEW dynamic port) and rejoins with the old session
# id via `evcc --resume <hex>`. Josev's SECC preserves the EV session context across connections on PAUSE
# ("Preserved session state") and answers the resumed -2 SessionSetup with OK_OldSessionJoined; its -20
# SessionSetup compares against the fresh comm session's empty id instead of the preserved context — run
# it anyway and document what actually happens.
#
# Each session runs its own `evcc --sdp` discovery (the CLI's EVCC-side SDP client works live since the
# MulticastLoopback fix, 2026-07-23) — which is exactly what the port move on pause requires.
#
# Usage: live-evcc-pause-resume.sh [2|20] [interface]     (default: 2, eth0)
set -uo pipefail
PROTO="${1:-2}"
IFACE="${2:-eth0}"
MODE=$([ "$PROTO" = "2" ] && echo ac || echo dc)
REPO=/mnt/c/Users/achim/Desktop/Coding/OpenChargingCloud/Vanaheimr.V2G.Exi
DLL="$REPO/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_CLI/bin/Release/net10.0/WWCP_ISO15118_CLI.dll"
SECC_LOG=/tmp/josev-secc-pause.log
EVCC_LOG=/tmp/our-evcc-pause.log

cleanup() { docker rm -f josev-secc redis-interop 2>/dev/null; }
trap cleanup EXIT; cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null
echo ">>> Josev SECC (host mode, plain TCP)"
docker run -d --rm --name josev-secc --network host -e NETWORK_INTERFACE="$IFACE" \
    -e SECC_ENFORCE_TLS=False -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=INFO \
    iso15118-secc:latest >/dev/null
sleep 8

echo ">>> session 1: --sdp discovery — run to PAUSE"
dotnet "$DLL" evcc --sdp --interface "$IFACE" --protocol "$PROTO" --mode "$MODE" --pause >"$EVCC_LOG" 2>&1
echo ">>> session 1 exited ($?)"
sid=$(grep -oE 'Paused session id: [0-9A-F]+' "$EVCC_LOG" | awk '{print $4}')
[ -n "$sid" ] || { echo "!! no paused session id"; tail -5 "$EVCC_LOG"; exit 1; }
echo ">>> paused with session id $sid; session 2 re-discovers via --sdp (Josev moved ports) — RESUME"
sleep 3

dotnet "$DLL" evcc --sdp --interface "$IFACE" --protocol "$PROTO" --mode "$MODE" --resume "$sid" >>"$EVCC_LOG" 2>&1
rc=$?
sleep 1
docker logs josev-secc >"$SECC_LOG" 2>&1

echo ">>> session 2 exited ($rc)"
echo "== our EVCC =="
grep -E "SDP:|session setup:|Paused session id|Session complete|aborted" "$EVCC_LOG"
echo "== Josev SECC: pause/resume handling =="
grep -iE "Preserved session state|OLD_SESSION_JOINED|old session|does not match|new session ID" "$SECC_LOG" | head -6
