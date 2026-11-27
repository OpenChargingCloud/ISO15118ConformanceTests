#!/usr/bin/env bash
# -20 Plug & Charge, forward, against 2026.10.0: our car with the release's own MO contract credential
# over mutual TLS 1.3, their Evse15118D20 offering PnC for the first time in a release.
set -u
E=/home/ahzf/everest
C=$E/configs-2610
P=$E/dist-2610/etc/everest/certs
T=$E/run/2610-fwd/tls
F="bash $E/fwd-2610.sh"
MTLS="V2G_INTEROP_TLS=1 V2G_INTEROP_TLS_TRUST=$T/v2g-root.pem V2G_INTEROP_TLS_CLIENT=$T/vehicle.p12:123456"
PNC="V2G_INTEROP_CONTRACT_CERT=$P/client/mo/MO_CERT_CHAIN.p12 V2G_INTEROP_CONTRACT_PASS=123456"

SESSIONS=2 SDPSEC=00 PROTO=20 MODE=dc $F pnc20-tls13     $C/config-d20-pnc-fwd2610.yaml $MTLS $PNC
           SDPSEC=00 PROTO=20 MODE=dc $F pnc20-tls13-dyn $C/config-d20-pnc-fwd2610.yaml $MTLS $PNC V2G_INTEROP_DYNAMIC=1
echo "=== PNC20 ARMS DONE $(date -Is)"
