#!/usr/bin/env bash
#
# Their EV's TLS client chain, judged by our station against their roots. eVDriveFlow's PKI is
# secp521r1 throughout, and their VEHICLE branch hangs off an **OEM root of its own** — not off the V2G
# root their station chain uses, which is what the CharIN V2G PKI describes and what makes this the
# shape no other counterparty here has:
#
#   V2GRootCA (DC=V2G) -> cpoSubCA1     -> cpoSubCA2     -> SECCCert      (their station)
#   OEMRootCA (DC=OEM) -> VEHICLESubCA1 -> VEHICLESubCA2 -> VEHICLECert   (their car)
#
# Their car sends all three of its certificates, so their OEM root ALONE is enough. That is also the
# run that found our own defect: until 2026-08-09 both .NET call sites dropped the intermediates a peer
# sends and judged the bare leaf, which looks exactly like a peer that sent nothing.
#
# Usage: chain-validation.sh <trust-roots-path|""> <label>
#   certs/oemRootCACert.pem                        valid, anchored at OEMRootCA
#   vehicleSubCA1 + vehicleSubCA2 concatenated     refused: a Sub-CA is never an anchor
#   certs/v2gRootCACert.pem                        refused: real root, wrong branch
set -uo pipefail

TRUST="${1:-}"
LABEL="${2:-run}"

E="${EDF_CHECKOUT:-$HOME/edf/eVDriveFlow}"
C="$E/shared/certificates"
RUN="${EDF_RUN:-$HOME/edf/run}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="${CONFORMANCE_REPO:-$(cd "$here/../.." && pwd)}"
SECC="${SECC_DLL:-$REPO/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_SECC/bin/Release/net10.0/WWCP_ISO15118_SECC.dll}"
mkdir -p "$RUN"

# Their EV verifies our station against their own v2gRootCACert, so we serve THEIR SECC chain — the
# passphrase is theirs, out of their generator.
if [ ! -f "$HOME/edf/secc.pfx" ]; then
    openssl pkcs12 -export -inkey "$C/privateKeys/secc.key" -passin pass:123456789abcdefgh \
        -in "$C/certs/seccCertChain.pem" -name edf-secc -passout pass:interop -out "$HOME/edf/secc.pfx" \
        && echo "  wrote secc.pfx from their chain"
fi

# The network must have IPv6 or their EV's SDP multicast to ff02::1 dies with ENETUNREACH. It does not
# survive a WSL restart.
docker network inspect edfnet >/dev/null 2>&1 || \
    docker network create --ipv6 --subnet fd00:edf::/64 --subnet 172.30.0.0/16 edfnet >/dev/null

docker rm -f edf-ev >/dev/null 2>&1
pkill -f WWCP_ISO15118_SECC 2>/dev/null; sleep 2

echo "=== our SECC :55000, -20 DC Dynamic, mutual TLS, trust-roots='${TRUST:-<none>}' ==="
if [ -n "$TRUST" ]; then
    setsid nohup dotnet "$SECC" --listen 55000 --protocol 20 --mode dc --dynamic --no-pnc \
        --tls --server-cert "$HOME/edf/secc.pfx" --server-cert-pass interop \
        --require-client-cert --trust-roots "$TRUST" > "$RUN/secc-$LABEL.log" 2>&1 < /dev/null &
else
    setsid nohup dotnet "$SECC" --listen 55000 --protocol 20 --mode dc --dynamic --no-pnc \
        --tls --server-cert "$HOME/edf/secc.pfx" --server-cert-pass interop \
        --require-client-cert > "$RUN/secc-$LABEL.log" 2>&1 < /dev/null &
fi
sleep 8
grep -E 'Trust roots|Presenting|listening' "$RUN/secc-$LABEL.log" | sed 's/^/  /'

echo
echo "=== their EV (SECURITY_PROTOCOL=0x00, their regenerated PKI) ==="
docker run -d --name edf-ev --network edfnet edf-ev-unpatched sleep infinity >/dev/null
docker exec edf-ev sed -i 's/^SECURITY_PROTOCOL = 0x10/SECURITY_PROTOCOL = 0x00/' /app/shared/global_values.py
docker cp "$C" edf-ev:/app/shared/ >/dev/null
docker exec -d edf-ev socat TCP6-LISTEN:15118,fork,reuseaddr TCP:172.30.0.1:55000
docker exec -d edf-ev python3 /usr/local/bin/sdp-responder.py eth0 15118 tls
sleep 2
# stdin held open: at EOF their EV reads "Enter pressed" and stops at exchange 4 (2026-08-06 finding).
docker exec -d edf-ev sh -c "mkfifo -m 600 /tmp/kb 2>/dev/null; (sleep 300 > /tmp/kb &) ; cd /app/evcc && python3 start_ev.py < /tmp/kb > /tmp/ev.log 2>&1"
sleep 35
docker exec edf-ev sh -c "cat /tmp/ev.log" 2>&1 | sed 's/\x1b\[[0-9;]*m//g' > "$RUN/ev-$LABEL.log"

echo
echo "=== our station's verdict ==="
grep -E 'TLS client|Session' "$RUN/secc-$LABEL.log" | sed 's/^/  /' | head -6

echo
echo "=== their EV: what it negotiated ==="
grep -iE 'TLS Session established|Cipher suite|SSLError|CERTIFICATE' "$RUN/ev-$LABEL.log" | head -4 | cut -c1-160 | sed 's/^/  /'

docker rm -f edf-ev >/dev/null 2>&1
sleep 1; pkill -f WWCP_ISO15118_SECC 2>/dev/null
echo
echo "=== logs: $RUN/secc-$LABEL.log, $RUN/ev-$LABEL.log ==="
