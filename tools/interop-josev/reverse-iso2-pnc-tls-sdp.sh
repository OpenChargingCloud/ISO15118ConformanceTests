#!/usr/bin/env bash
# Live ISO 15118-2 **Plug & Charge**, reverse direction over TLS + SDP: a Josev EVCC
# (evcc_config_pnc_ac.json: useTls=true) discovers our TLS SECC, picks Contract payment (it does so
# whenever Contract is offered AND TLS is on), and runs the full -2 PnC AC session:
# PaymentDetails (contract chain → our GenChallenge) → SIGNED AuthorizationReq → charge loop where OUR
# ChargingStatusRes demands ReceiptRequired + MeterInfo → Josev answers each cycle with a SIGNED
# MeteringReceiptReq. Our SECC verifies every signature (Josev form: standalone-xmldsig SignedInfo,
# ecdsa-sha256) and prints one verdict line per signature.
#
# Prereq: /tmp/secc.p12 (Josev SECC leaf+key+CPO Sub-CAs, pw 12345) — the EVCC validates our TLS server
# cert against its V2G root, so we present the chain it trusts. -2 TLS is unilateral (no client cert).
# `pnc-chain-setup.sh` builds it, along with the trust-root directories below.
#
# TRUST_ROOTS=<file|dir> anchors the CONTRACT chain as well as verifying the signature; unset, the
# station prints "chain not checked" and the run proves only that the signature matched the leaf the car
# sent. /tmp/josev-roots-mo is the arm, /tmp/josev-roots-v2g the control that must REJECT it. -2 TLS is
# unilateral, so here the roots decide nothing but the contract.
set -uo pipefail
# The repository root, two levels up from this script -- not a path on one particular machine.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$here/../.." && pwd)"
DLL="$REPO/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_SECC/bin/Release/net10.0/WWCP_ISO15118_SECC.dll"
CFG=/venv/lib/python3.10/site-packages/iso15118/shared/examples/evcc/iso15118_2/evcc_config_pnc_ac.json
SECC_LOG=/tmp/secc-iso2-pnc.log
EVCC_LOG=/tmp/evcc-iso2-pnc.log

cleanup() { [ -n "${PID:-}" ] && kill "$PID" 2>/dev/null; docker rm -f josev-evcc redis-interop 2>/dev/null; pkill -f "Simulation.Cli.*secc" 2>/dev/null; }
trap cleanup EXIT; cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null
echo ">>> our SECC :55000  -2 AC, TLS (secc.p12), --sdp — offers Contract + ExternalPayment"
dotnet "$DLL" --listen 55000 --protocol 2 --mode ac --tls \
   --server-cert /tmp/secc.p12 --server-cert-pass 12345 \
   ${TRUST_ROOTS:+--trust-roots "$TRUST_ROOTS"} \
   --sdp --interface eth0 >"$SECC_LOG" 2>&1 &
PID=$!; sleep 3
grep -i advertising "$SECC_LOG"

echo ">>> Josev EVCC (evcc_config_pnc_ac.json, useTls) — Contract payment, signed messages"
timeout 120 docker run --rm --name josev-evcc --network host -e NETWORK_INTERFACE=eth0 \
    -e EVCC_CONFIG_PATH="$CFG" -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=INFO \
    iso15118-evcc:latest >"$EVCC_LOG" 2>&1
echo ">>> EVCC exited ($?)"
wait "$PID" 2>/dev/null; PID=
sleep 1

echo "== our SECC: verdicts =="
grep -E "Trust roots|Plug & Charge|MeteringReceipt:|Session complete|Session aborted" "$SECC_LOG"
echo "== Josev EVCC: what it ran =="
grep -oE "Sent (PaymentDetailsReq|AuthorizationReq|MeteringReceiptReq|PowerDeliveryReq|SessionStopReq)" "$EVCC_LOG" | sort | uniq -c
echo "== stop reason =="
grep -iE "session terminated|SessionStopRes received|error|Traceback" "$EVCC_LOG" | head -3
