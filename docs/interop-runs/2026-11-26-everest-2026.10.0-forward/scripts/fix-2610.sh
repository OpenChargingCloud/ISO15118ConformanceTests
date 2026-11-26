#!/usr/bin/env bash
# The Scheduled -20 arms again, with the EVCC whose EVPowerProfile is anchored in SECC time and follows
# the offered schedule. Before the fix every one of them ended at PowerDeliveryRes=FAILED_EVPowerProfileInvalid.
set -u
E=/home/ahzf/everest
C=$E/configs-2610
F="bash $E/fwd-2610.sh"
T=$E/run/2610-fwd/tls
MTLS="V2G_INTEROP_TLS=1 V2G_INTEROP_TLS_TRUST=$T/v2g-root.pem V2G_INTEROP_TLS_CLIENT=$T/vehicle.p12:123456"

SESSIONS=2 PROTO=20 MODE=dc  $F fix-d20              $C/config-d20-fwd2610.yaml
           PROTO=20 MODE=dc  $F fix-d20-bpt          $C/config-d20-fwd2610.yaml V2G_INTEROP_BPT_FIRST=1
SESSIONS=2 TRIGGER=1 PROTO=20 MODE=ac $F fix-ac20-trigger $C/config-ac20-fwd2610.yaml
TRIGGER=1  PROTO=20 MODE=ac  $F fix-ac20-bpt-trigger $C/config-ac20-fwd2610.yaml V2G_INTEROP_BPT_FIRST=1
           PROTO=20 MODE=mcs $F fix-mcs              $C/config-mcs-fwd2610.yaml
           PROTO=20 MODE=mcs $F fix-mcs-bpt          $C/config-mcs-fwd2610.yaml V2G_INTEROP_BPT_FIRST=1
PROBE=mux  PROTO=20 MODE=dc  $F fix-mux-iso20        $C/config-mux-fwd2610.yaml
PROBE=mux  PROTO=both MODE=dc $F fix-mux-both-2first $C/config-mux-fwd2610.yaml V2G_INTEROP_SAP_FIRST=2
SESSIONS=2 SDPSEC=00 PROTO=20 MODE=dc $F fix-d20-tls13 $C/config-d20-tls-fwd2610.yaml $MTLS
TRIGGER=1 SDPSEC=00 PROTO=20 MODE=ac $F fix-ac20-tls13 $C/config-ac20-tls-fwd2610.yaml $MTLS
PROBE=muxtls PROTO=both MODE=dc $F fix-mux-tls-both $C/config-mux-tls-fwd2610.yaml $MTLS V2G_INTEROP_SAP_FIRST=20
echo "=== FIX ARMS DONE $(date -Is)"
