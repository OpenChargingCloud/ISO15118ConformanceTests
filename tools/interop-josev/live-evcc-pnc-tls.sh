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
# The repository root, two levels up from this script -- not a path on one particular machine.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$here/../.." && pwd)"
DLL="$REPO/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_CLI/bin/Release/net10.0/WWCP_ISO15118_CLI.dll"
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

echo ">>> our EVCC: --sdp discovery (TLS requested), mutual TLS 1.3 + signed PnC AuthorizationReq (contract.p12)"
dotnet "$DLL" evcc --sdp --interface "$IFACE" --protocol 20 --mode dc --tls-backend dotnet \
    --client-cert /tmp/oem.p12 --client-cert-pass 12345 \
    --contract-cert /tmp/contract.p12 --contract-cert-pass 12345 >"$EVCC_LOG" 2>&1
rc=$?
sleep 1
docker logs josev-secc >"$SECC_LOG" 2>&1

echo ">>> our EVCC exited ($rc)"
echo "== our EVCC =="; grep -E "SDP:|PnC:|auth:|Session complete|aborted" "$EVCC_LOG"
echo "== Josev SECC: signature verification =="
grep -iE "Verifying digest|Match:|Verifying signature value|signature.*(fail|error|verif)" "$SECC_LOG" | head -8
echo "== Josev SECC: auth mode + session =="
grep -iE "selected_auth|PNC|AuthorizationReq|Session ended|SessionStopReq" "$SECC_LOG" | grep -ivE "offered" | head -8
