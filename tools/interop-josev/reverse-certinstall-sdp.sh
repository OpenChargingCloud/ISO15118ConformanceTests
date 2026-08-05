#!/usr/bin/env bash
# Live ISO 15118-20 **contract provisioning** (CertificateInstallation), reverse direction: a Josev EVCC with
# isCertInstallNeeded=true SDP-discovers our SECC and sends its SIGNED CertificateInstallationReq (OEM
# provisioning chain, ecdsa-sha256 over the chain's EXI fragment). Our SECC must (a) tolerate Josev's
# mis-framed V2GTP payload type (0x8001 instead of 0x8002 — a Josev default-arg bug), (b) VERIFY the OEM
# signature (standalone-xmldsig grammar), and (c) answer with a signed, schema-valid
# CertificateInstallationRes (fresh P-521 contract, key ECDH/AES-GCM-wrapped — undecryptable for Josev's
# P-256 OEM key, which cannot join the -20 secp521r1 ECDH).
#
# KNOWN ENDPOINT: Josev's own EVCC-side CertificateInstallation state is `raise NotImplementedError`, so the
# session ends right after our res reaches it — that crash is Josev's gap, not a failure of this run. The
# run's evidence is our SECC's "CertificateInstallation: ... signature OK (grammar=xmldsig-standalone)" line.
#
# Prereq: /tmp/josev-cfg/evcc_config_dc_certinstall.json (evcc_config_dc.json with isCertInstallNeeded=true).
set -uo pipefail
REPO=/mnt/c/Users/achim/Desktop/Coding/OpenChargingCloud/Vanaheimr.V2G.Exi
DLL="$REPO/libs/EVSimulatorApp/simulation/Vanaheimr.V2G.Simulation.Cli/bin/Release/net10.0/Vanaheimr.V2G.Simulation.Cli.dll"
SECC_LOG=/tmp/secc-certinstall.log
EVCC_LOG=/tmp/evcc-certinstall.log

cleanup() { [ -n "${PID:-}" ] && kill "$PID" 2>/dev/null; docker rm -f josev-evcc redis-interop 2>/dev/null; pkill -f "Simulation.Cli.*secc" 2>/dev/null; }
trap cleanup EXIT; cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null
echo ">>> our SECC :55000  -20 DC, plain TCP, --sdp (CertificateInstallationService=true)"
dotnet "$DLL" secc --listen 55000 --protocol 20 --mode dc --sdp --interface eth0 >"$SECC_LOG" 2>&1 &
PID=$!; sleep 3
grep -i advertising "$SECC_LOG"

echo ">>> Josev EVCC (isCertInstallNeeded=true) — sends its signed CertificateInstallationReq"
timeout 90 docker run --rm --name josev-evcc --network host -e NETWORK_INTERFACE=eth0 -e SECC_ENFORCE_TLS=False \
    -v /tmp/josev-cfg/evcc_config_dc_certinstall.json:/cfg/evcc_config.json:ro \
    -e EVCC_CONFIG_PATH=/cfg/evcc_config.json -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=INFO \
    iso15118-evcc:latest >"$EVCC_LOG" 2>&1
echo ">>> EVCC exited ($?)"
wait "$PID" 2>/dev/null; PID=
sleep 1

echo "== our SECC: the verdict =="
grep -E "CertificateInstallation:|Session complete|Session aborted" "$SECC_LOG"
echo "== Josev EVCC: what it did =="
grep -iE "Sent CertificateInstallationReq|Entered state CertificateInstallation" "$EVCC_LOG" | head -3
echo "== Josev EVCC: its own gap (expected) =="
grep -iE "NotImplementedError|not yet implemented" "$EVCC_LOG" | head -2
