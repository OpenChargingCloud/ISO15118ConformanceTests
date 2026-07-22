#!/usr/bin/env bash
# Live pause/resume, FORWARD direction: our EVCC runs a session against a real Josev SECC, ends it with
# ChargingSession.Pause, then re-discovers via SDP (Josev tears down its TCP server on pause and resumes
# the UDP SDP server — the resumed session lands on a NEW dynamic port) and rejoins with the old session
# id via `evcc --resume <hex>`. Josev's SECC preserves the EV session context across connections on PAUSE
# ("Preserved session state") and answers the resumed -2 SessionSetup with OK_OldSessionJoined; its -20
# SessionSetup compares against the fresh comm session's empty id instead of the preserved context — run
# it anyway and document what actually happens.
#
# Uses the proven in-script python SDP probe per session (our CLI's own EVCC-side SDP client is a known
# CI-only gap — see docs/roadmap.md "SDP over the wire in CI").
#
# Usage: live-evcc-pause-resume.sh [2|20] [interface]     (default: 2, eth0)
set -uo pipefail
PROTO="${1:-2}"
IFACE="${2:-eth0}"
MODE=$([ "$PROTO" = "2" ] && echo ac || echo dc)
REPO=/mnt/c/Users/achim/Desktop/Coding/OpenChargingCloud/Vanaheimr.V2G.Exi
DLL="$REPO/Vanaheimr.V2G.Simulation.Cli/bin/Release/net10.0/Vanaheimr.V2G.Simulation.Cli.dll"
SECC_LOG=/tmp/josev-secc-pause.log
EVCC_LOG=/tmp/our-evcc-pause.log

sdp_discover() {  # NoTLS SDP probe; prints "<addr> <port>"
    IFACE="$IFACE" python3 - <<'PY'
import os, socket, struct, sys, time
req = bytes([0x01, 0xFE, 0x90, 0x00, 0x00, 0x00, 0x00, 0x02, 0x10, 0x00])  # security=NoTLS, transport=TCP
ifidx = socket.if_nametoindex(os.environ["IFACE"])
s = socket.socket(socket.AF_INET6, socket.SOCK_DGRAM)
s.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_MULTICAST_IF, struct.pack("I", ifidx))
s.settimeout(2)
for _ in range(10):
    s.sendto(req, ("ff02::1", 15118, 0, ifidx))
    try:
        data, _ = s.recvfrom(1024)
    except socket.timeout:
        continue
    p = data[8:]
    print(socket.inet_ntop(socket.AF_INET6, p[0:16]), int.from_bytes(p[16:18], "big"))
    sys.exit(0)
sys.exit(1)
PY
}

cleanup() { docker rm -f josev-secc redis-interop 2>/dev/null; }
trap cleanup EXIT; cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null
echo ">>> Josev SECC (host mode, plain TCP)"
docker run -d --rm --name josev-secc --network host -e NETWORK_INTERFACE="$IFACE" \
    -e SECC_ENFORCE_TLS=False -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=INFO \
    iso15118-secc:latest >/dev/null
sleep 8

ep=$(sdp_discover) || { echo "!! SDP discovery (session 1) failed"; exit 1; }
addr=$(echo "$ep" | awk '{print $1}'); port=$(echo "$ep" | awk '{print $2}')
echo ">>> session 1: SECC at [$addr%$IFACE]:$port — run to PAUSE"
dotnet "$DLL" evcc --connect "[$addr%$IFACE]:$port" --protocol "$PROTO" --mode "$MODE" --pause >"$EVCC_LOG" 2>&1
echo ">>> session 1 exited ($?)"
sid=$(grep -oE 'Paused session id: [0-9A-F]+' "$EVCC_LOG" | awk '{print $4}')
[ -n "$sid" ] || { echo "!! no paused session id"; tail -5 "$EVCC_LOG"; exit 1; }
echo ">>> paused with session id $sid; re-discovering..."
sleep 3

ep=$(sdp_discover) || { echo "!! SDP discovery (session 2) failed"; docker logs josev-secc | tail -5; exit 1; }
addr=$(echo "$ep" | awk '{print $1}'); port2=$(echo "$ep" | awk '{print $2}')
echo ">>> session 2: SECC at [$addr%$IFACE]:$port2 (was :$port) — RESUME with $sid"
dotnet "$DLL" evcc --connect "[$addr%$IFACE]:$port2" --protocol "$PROTO" --mode "$MODE" --resume "$sid" >>"$EVCC_LOG" 2>&1
rc=$?
sleep 1
docker logs josev-secc >"$SECC_LOG" 2>&1

echo ">>> session 2 exited ($rc)"
echo "== our EVCC =="
grep -E "session setup:|Paused session id|Session complete|aborted" "$EVCC_LOG"
echo "== Josev SECC: pause/resume handling =="
grep -iE "Preserved session state|OLD_SESSION_JOINED|old session|does not match|new session ID" "$SECC_LOG" | head -6
