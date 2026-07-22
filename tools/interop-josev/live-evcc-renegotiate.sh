#!/usr/bin/env bash
# Live EV-initiated renegotiation, FORWARD direction: our EVCC (-2 AC, --renegotiate) opens
# PowerDeliveryReq(Renegotiate) after the first charging-status cycle against a real Josev SECC, re-runs
# ChargeParameterDiscovery, and completes the session ([V2G2-841]).
#
# Usage: live-evcc-renegotiate.sh [interface]     (default: eth0)
set -uo pipefail
IFACE="${1:-eth0}"
REPO=/mnt/c/Users/achim/Desktop/Coding/OpenChargingCloud/Vanaheimr.V2G.Exi
DLL="$REPO/Vanaheimr.V2G.Simulation.Cli/bin/Release/net10.0/Vanaheimr.V2G.Simulation.Cli.dll"
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

ep=$(IFACE="$IFACE" python3 - <<'PY'
import os, socket, struct
req = bytes([0x01, 0xFE, 0x90, 0x00, 0x00, 0x00, 0x00, 0x02, 0x10, 0x00])  # SDP: NoTLS
ifidx = socket.if_nametoindex(os.environ["IFACE"])
s = socket.socket(socket.AF_INET6, socket.SOCK_DGRAM)
s.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_MULTICAST_IF, struct.pack("I", ifidx))
s.settimeout(5)
s.sendto(req, ("ff02::1", 15118, 0, ifidx))
data, _ = s.recvfrom(1024)
p = data[8:]
print(socket.inet_ntop(socket.AF_INET6, p[0:16]), int.from_bytes(p[16:18], "big"))
PY
) || { echo "!! SDP discovery failed"; exit 1; }
addr=$(echo "$ep" | awk '{print $1}'); port=$(echo "$ep" | awk '{print $2}')
echo ">>> SECC at [$addr%$IFACE]:$port — our EVCC renegotiates after the first cycle"
dotnet "$DLL" evcc --connect "[$addr%$IFACE]:$port" --protocol 2 --mode ac --renegotiate >"$EVCC_LOG" 2>&1
rc=$?
sleep 1
docker logs josev-secc >"$SECC_LOG" 2>&1

echo ">>> our EVCC exited ($rc)"
echo "== our EVCC =="; grep -E "renegotiations:|Session complete|aborted" "$EVCC_LOG"
echo "== Josev SECC =="
grep -icE "RENEGOTIATE" "$SECC_LOG" | xargs echo "  renegotiate mentions:"
grep -oE "(ChargeParameterDiscoveryReq|PowerDeliveryReq|SessionStopReq) received" "$SECC_LOG" | sort | uniq -c
