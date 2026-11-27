#!/usr/bin/env bash
# The -20 AC arms against 2026.10.0 with the car leaving state C on PowerDeliveryReq(Stop), as
# [V2G20-848] requires within 250 ms. Without it every AC session ended FAILED_ContactorError at the stop.
set -u
E=/home/ahzf/everest
C=$E/configs-2610
F="bash $E/fwd-2610.sh"
T=$E/run/2610-fwd/tls
MTLS="V2G_INTEROP_TLS=1 V2G_INTEROP_TLS_TRUST=$T/v2g-root.pem V2G_INTEROP_TLS_CLIENT=$T/vehicle.p12:123456"

SESSIONS=2 STOP_TRIGGER=1 TRIGGER=1 PROTO=20 MODE=ac $F stop-ac20          $C/config-ac20-fwd2610.yaml
           STOP_TRIGGER=1 TRIGGER=1 PROTO=20 MODE=ac $F stop-ac20-bpt      $C/config-ac20-fwd2610.yaml V2G_INTEROP_BPT_FIRST=1
           STOP_TRIGGER=1 TRIGGER=1 PROTO=20 MODE=ac $F stop-ac20-dyn      $C/config-ac20-fwd2610.yaml V2G_INTEROP_DYNAMIC=1
STOP_TRIGGER=1 TRIGGER=1 SDPSEC=00 PROTO=20 MODE=ac  $F stop-ac20-tls13    $C/config-ac20-tls-fwd2610.yaml $MTLS
echo "=== STOP ARMS DONE $(date -Is)"
