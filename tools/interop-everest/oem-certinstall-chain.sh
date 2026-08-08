#!/usr/bin/env bash
# The ISO 15118-20 **OEM provisioning** chain, judged against a foreign car's real material.
#
# EVerest's PyEvJosev with `is_cert_install_needed: true` SDP-discovers our SECC and sends a signed
# CertificateInstallationReq carrying EVerest's OEM branch — OEMRootCA -> OEMSubCA1 -> OEMSubCA2 ->
# OEMProvCert, a self-signed root of its own, separate from their V2G and MO roots. Our station
# verifies the signature and builds the chain against whatever --trust-roots it was given, and prints
# both verdicts on one line.
#
# KNOWN ENDPOINT: their EVCC's own CertificateInstallation handler is `raise NotImplementedError` (it is
# Josev-derived, and Josev implements the request but not the response). The session therefore ends the
# moment our response arrives, and our station reports the closed connection. That is their gap, not this
# run's: the evidence is printed before the response is sent.
#
# PREREQ, and it bites first: their -20 cert-install path loads `ca/oem/OEM_SUB_CA{1,2}.der`, which this
# dist's certificate store did not have (every other DER their enum names was there). Their own
# `pki/create_certs.sh` does emit them — copy them from `pki/iso15118_20/certs/ca/oem/`, or convert:
#     openssl x509 -in ca/oem/OEM_SUB_CA1.pem -outform DER -out ca/oem/OEM_SUB_CA1.der   # and _CA2
#
# Also needed: a config with the knob set. Take the reverse -20 config that puts their station on `lo`
# and their car on `eth0` — so the car finds *our* SDP — and add to the PyEvJosev config_module:
#     is_cert_install_needed: true
#
# Usage: oem-certinstall-chain.sh <trust-roots-path|""> <label>
#   ""                          no chain check at all — the honest "not validated" state
#   ca/oem/OEM_ROOT_CA.pem      their OEM root alone: enough, because their EV ships its Sub-CAs
#   the two OEM Sub-CAs         no root: refused — a non-self-signed cert is never an anchor
#   ca/v2g/V2G_ROOT_CA.pem      a real root, wrong branch: refused, while the signature still verifies
set -uo pipefail

TRUST="${1:-}"
LABEL="${2:-run}"

DIST="${EVEREST_DIST:-$HOME/everest/dist}"
CFG="${EVEREST_CONFIG:-$HOME/everest/configs-ours/config-mcs-oem-certinstall.yaml}"
RUN="${EVEREST_RUN:-$HOME/everest/run}"
# The repository root, two levels up from this script -- not a path on one particular machine.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="${CONFORMANCE_REPO:-$(cd "$here/../.." && pwd)}"
SECC="${SECC_DLL:-$REPO/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_SECC/bin/Release/net10.0/WWCP_ISO15118_SECC.dll}"
mkdir -p "$RUN"

# Our station has to sit on the same link as their car for SDP to reach it, so both run inside WSL.
pkill -x manager 2>/dev/null; pkill -f WWCP_ISO15118_SECC 2>/dev/null; sleep 2

echo "=== our SECC :55000, -20 DC, SDP on eth0, trust-roots='${TRUST:-<none>}' ==="
if [ -n "$TRUST" ]; then
    setsid nohup dotnet "$SECC" --listen 55000 --protocol 20 --mode dc \
        --sdp --interface eth0 --trust-roots "$TRUST" > "$RUN/secc-$LABEL.log" 2>&1 < /dev/null &
else
    setsid nohup dotnet "$SECC" --listen 55000 --protocol 20 --mode dc \
        --sdp --interface eth0 > "$RUN/secc-$LABEL.log" 2>&1 < /dev/null &
fi
sleep 8
grep -E 'Trust roots|listening|advertis' "$RUN/secc-$LABEL.log" | sed 's/^/  /'

echo
echo "=== their PyEvJosev, is_cert_install_needed=true ==="
cd "$DIST" || exit 1
setsid nohup ./bin/manager --config "$CFG" > "$RUN/ev-$LABEL.log" 2>&1 < /dev/null &
sleep 50

echo
echo "=== our station's verdict ==="
grep -E 'CertificateInstallation:|Plug & Charge|Session' "$RUN/secc-$LABEL.log" | sed 's/^/  /' | head -8

# `pkill` and the `pgrep` that would verify it must not share a one-liner: the check runs before the
# signal has taken effect and reports the process still up.
pkill -x manager 2>/dev/null; sleep 1; pkill -f WWCP_ISO15118_SECC 2>/dev/null
echo
echo "=== logs: $RUN/secc-$LABEL.log, $RUN/ev-$LABEL.log ==="
