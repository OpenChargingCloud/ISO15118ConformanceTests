#!/usr/bin/env bash
# Live over-the-wire ISO 15118-20 DC **Plug & Charge** over mutual TLS 1.3, FORWARD direction:
# OUR EVCC signs the PnC AuthorizationReq (Josev interop form: SHA-256 digest over the
# PnC_AReqAuthorizationMode EXI fragment, SignedInfo EXI-encoded over the standalone xmldsig grammar,
# ecdsa-sha256 raw r||s) and a real Josev SECC must VERIFY it — digest match + signature check both
# happen in Josev's shared/security.py with its own EXIficient codec, so this is the strongest
# independent confirmation of our EVCC-side signing bytes.
#
# Prereqs:
#   /tmp/oem.p12       client cert for mutual TLS (Josev OEM leaf+key+Sub-CAs, pw 12345)
#   /tmp/contract.p12  contract credentials for PnC   (Josev contract leaf+key+MO Sub-CAs, pw 12345)
#   dotnet build -c Release (the CLI)
#
# Usage: live-evcc-pnc-tls.sh [interface]     (default: eth0)
set -uo pipefail
IFACE="${1:-eth0}"
REPO=/mnt/c/Users/achim/Desktop/Coding/OpenChargingCloud/Vanaheimr.V2G.Exi
DLL="$REPO/Vanaheimr.V2G.Simulation.Cli/bin/Release/net10.0/Vanaheimr.V2G.Simulation.Cli.dll"
SECC_LOG=/tmp/josev-secc-pnc.log
EVCC_LOG=/tmp/our-evcc-pnc.log

cleanup() { docker rm -f josev-secc redis-interop 2>/dev/null; }
trap cleanup EXIT; cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null
echo ">>> Josev SECC (host mode, SECC_ENFORCE_TLS=True, TLS 1.3 mutual, AUTH_MODES default = EIM+PNC)"
docker run -d --rm --name josev-secc --network host -e NETWORK_INTERFACE="$IFACE" \
    -e SECC_ENFORCE_TLS=True -e ENABLE_TLS_1_3=True \
    -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=DEBUG \
    iso15118-secc:latest >/dev/null
sleep 8
docker logs josev-secc >"$SECC_LOG" 2>&1
grep -iE "SECC.*ready|SDP server" "$SECC_LOG" | head -2

ep=$(IFACE="$IFACE" python3 - <<'PY'
import os, socket, struct
req = bytes([0x01, 0xFE, 0x90, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00])  # SDP req: security=TLS, transport=TCP
ifidx = socket.if_nametoindex(os.environ["IFACE"])
s = socket.socket(socket.AF_INET6, socket.SOCK_DGRAM)
s.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_MULTICAST_IF, struct.pack("I", ifidx))
s.settimeout(5)
s.sendto(req, ("ff02::1", 15118, 0, ifidx))
data, _ = s.recvfrom(1024)
p = data[8:]
print(socket.inet_ntop(socket.AF_INET6, p[0:16]), int.from_bytes(p[16:18], "big"))
PY
) || { echo "!! SDP discovery failed"; docker logs josev-secc | tail -20; exit 1; }
addr=$(echo "$ep" | awk '{print $1}'); port=$(echo "$ep" | awk '{print $2}')
echo ">>> SDP discovered TLS SECC at [$addr%$IFACE]:$port"

echo ">>> our EVCC: mutual TLS 1.3 + signed PnC AuthorizationReq (contract.p12)"
dotnet "$DLL" evcc --connect "[$addr%$IFACE]:$port" --protocol 20 --mode dc --tls-backend dotnet \
    --client-cert /tmp/oem.p12 --client-cert-pass 12345 \
    --contract-cert /tmp/contract.p12 --contract-cert-pass 12345 >"$EVCC_LOG" 2>&1
rc=$?
sleep 1
docker logs josev-secc >"$SECC_LOG" 2>&1

echo ">>> our EVCC exited ($rc)"
echo "== our EVCC =="; grep -E "PnC:|auth:|Session complete|aborted" "$EVCC_LOG"
echo "== Josev SECC: signature verification =="
grep -iE "Verifying digest|Match:|Verifying signature value|signature.*(fail|error|verif)" "$SECC_LOG" | head -8
echo "== Josev SECC: auth mode + session =="
grep -iE "selected_auth|PNC|AuthorizationReq|Session ended|SessionStopReq" "$SECC_LOG" | grep -ivE "offered" | head -8
