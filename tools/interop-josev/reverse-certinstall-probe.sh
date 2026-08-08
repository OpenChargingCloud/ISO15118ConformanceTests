#!/usr/bin/env bash
# PROBE: does a Josev EVCC with isCertInstallNeeded=true actually build + send a signed -20
# CertificateInstallationReq? Our SECC announces CertificateInstallationService=true; whatever arrives is
# captured in both logs (our SECC may abort with a sequence guard if the message is not yet handled — the
# probe's value is Josev's EXI log of the req it encodes, esp. WHAT its signature covers).
set -uo pipefail
# The repository root, two levels up from this script -- not a path on one particular machine.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$here/../.." && pwd)"
DLL="$REPO/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_CLI/bin/Release/net10.0/WWCP_ISO15118_CLI.dll"
SECC_LOG=/tmp/secc-certinstall-probe.log
EVCC_LOG=/tmp/evcc-certinstall-probe.log

cleanup() { [ -n "${PID:-}" ] && kill "$PID" 2>/dev/null; docker rm -f josev-evcc redis-interop 2>/dev/null; pkill -f "Simulation.Cli.*secc" 2>/dev/null; }
trap cleanup EXIT; cleanup
pkill -9 -f sdp-responder 2>/dev/null; sleep 1

docker run -d --rm --name redis-interop --network host redis:6.2.6-alpine >/dev/null
echo ">>> our SECC :55000  -20 DC, plain TCP, --sdp (CertificateInstallationService=true)"
dotnet "$DLL" secc --listen 55000 --protocol 20 --mode dc --sdp --interface eth0 >"$SECC_LOG" 2>&1 &
PID=$!; sleep 3
grep -i advertising "$SECC_LOG"

echo ">>> Josev EVCC (isCertInstallNeeded=true) — will attempt CertificateInstallationReq"
timeout 90 docker run --rm --name josev-evcc --network host -e NETWORK_INTERFACE=eth0 -e SECC_ENFORCE_TLS=False \
    -v /tmp/josev-cfg/evcc_config_dc_certinstall.json:/cfg/evcc_config.json:ro \
    -e EVCC_CONFIG_PATH=/cfg/evcc_config.json -e REDIS_HOST=localhost -e REDIS_PORT=6379 -e LOG_LEVEL=DEBUG \
    iso15118-evcc:latest >"$EVCC_LOG" 2>&1
echo ">>> EVCC exited ($?)"; sleep 1

echo "== EVCC: cert-install attempt =="
grep -iE "CertificateInstallation|PrivateKeyReadError|Falling back" "$EVCC_LOG" | head -6
echo "== EVCC: the encoded req (first 400 chars) =="
grep -oE '\{"CertificateInstallationReq".{0,400}' "$EVCC_LOG" | head -1
echo "== EVCC: errors =="
grep -iE "error|exception|Traceback" "$EVCC_LOG" | head -6
echo "== our SECC =="
grep -iE "Session complete|aborted|sequence guard" "$SECC_LOG" | head -3
