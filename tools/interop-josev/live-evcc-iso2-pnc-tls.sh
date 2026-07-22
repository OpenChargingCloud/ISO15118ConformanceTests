#!/usr/bin/env bash
# Live ISO 15118-2 **Plug & Charge**, FORWARD direction over TLS: OUR EVCC pays via Contract against a real
# Josev SECC — PaymentDetails (Josev VERIFIES our contract chain against its MO root!) → our SIGNED
# AuthorizationReq (Josev's verify_signature re-encodes the body fragment + SignedInfo with its own
# EXIficient codec, hardcoded SHA-256) → full AC charge loop to SessionStop. (Josev's SECC hardcodes
# receipt_required=False, so MeteringReceipt is exercised by the reverse run instead.)
#
# Prereq: /tmp/contract.p12 (Josev's MO PKI contract leaf+key+Sub-CAs, pw 12345); Josev SECC image.
#
# Usage: live-evcc-iso2-pnc-tls.sh [interface]     (default: eth0)
set -uo pipefail
IFACE="${1:-eth0}"
REPO=/mnt/c/Users/achim/Desktop/Coding/OpenChargingCloud/Vanaheimr.V2G.Exi
DLL="$REPO/Vanaheimr.V2G.Simulation.Cli/bin/Release/net10.0/Vanaheimr.V2G.Simulation.Cli.dll"
SECC_LOG=/tmp/josev-secc-iso2-pnc.log
EVCC_LOG=/tmp/our-evcc-iso2-pnc.log

cleanup() { docker rm -f josev-secc redis-interop 2>/dev/null; }
trap cleanup EXIT; cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null
echo ">>> Josev SECC (host mode, SECC_ENFORCE_TLS=True — -2 TLS 1.2 unilateral, AUTH_MODES default EIM+PNC)"
docker run -d --rm --name josev-secc --network host -e NETWORK_INTERFACE="$IFACE" \
    -e SECC_ENFORCE_TLS=True \
    -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=DEBUG \
    iso15118-secc:latest >/dev/null
sleep 8

ep=$(IFACE="$IFACE" python3 - <<'PY'
import os, socket, struct
req = bytes([0x01, 0xFE, 0x90, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00])  # SDP req: security=TLS
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

echo ">>> our EVCC: -2 AC, TLS, Contract payment (contract.p12) — signed AuthorizationReq"
dotnet "$DLL" evcc --connect "[$addr%$IFACE]:$port" --protocol 2 --mode ac --tls-backend dotnet \
    --contract-cert /tmp/contract.p12 --contract-cert-pass 12345 >"$EVCC_LOG" 2>&1
rc=$?
sleep 1
docker logs josev-secc >"$SECC_LOG" 2>&1

echo ">>> our EVCC exited ($rc)"
echo "== our EVCC =="; grep -E "PnC:|auth:|Session complete|aborted" "$EVCC_LOG"
echo "== Josev SECC: chain + signature verification =="
grep -iE "Verifying signature|Verifying digest|Match:|verified successfully|certificate.*(valid|verif)|CertChainError|CertSignatureError" "$SECC_LOG" | head -8
echo "== Josev SECC: session =="
grep -iE "PaymentDetailsReq received|AuthorizationReq received|Session ended|SessionStopReq" "$SECC_LOG" | head -5
