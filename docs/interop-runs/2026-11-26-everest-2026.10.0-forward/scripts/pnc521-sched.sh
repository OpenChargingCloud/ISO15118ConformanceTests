#!/usr/bin/env bash
# The Scheduled half of pnc521-2610.sh again, without the rebuild: its first attempt never booted the manager.
set -u
E=/home/ahzf/everest; C=$E/configs-2610; T=$E/run/2610-fwd/tls; M=$E/run/2610-fwd/mo521
pgrep -a manager; sleep 3
SESSIONS=2 SDPSEC=00 PROTO=20 MODE=dc bash $E/fwd-2610.sh pnc521-tls13 $C/config-d20-pnc521-fwd2610.yaml \
  V2G_INTEROP_TLS=1 V2G_INTEROP_TLS_TRUST=$T/v2g-root.pem V2G_INTEROP_TLS_CLIENT=$T/vehicle.p12:123456 \
  V2G_INTEROP_CONTRACT_CERT=$M/contract521.p12 V2G_INTEROP_CONTRACT_PASS=123456
