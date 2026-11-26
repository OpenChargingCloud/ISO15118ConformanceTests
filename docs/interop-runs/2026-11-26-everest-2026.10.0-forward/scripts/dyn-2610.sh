#!/usr/bin/env bash
# Second pass: -2 AC (missed by a readiness check), and every -20 arm again in Dynamic mode, because
# every Scheduled -20 session stops at PowerDeliveryRes=FAILED_EVPowerProfileInvalid against 2026.10.0
# (our EVPowerProfile is anchored at TimeAnchor 0). Dynamic carries no schedule check, so these arms
# measure what lies past PowerDelivery.
set -u
E=/home/ahzf/everest
C=$E/configs-2610
F="bash $E/fwd-2610.sh"
D=V2G_INTEROP_DYNAMIC=1

SESSIONS=2 PROTO=2  MODE=ac $F ac2                     $C/config-ac2-fwd2610.yaml
SESSIONS=2 PROTO=20 MODE=dc $F d20-dyn2                $C/config-d20-fwd2610.yaml $D
           PROTO=20 MODE=dc $F d20-bpt-dyn             $C/config-d20-fwd2610.yaml $D V2G_INTEROP_BPT_FIRST=1
TRIGGER=1  PROTO=20 MODE=ac $F ac20-dyn-trigger        $C/config-ac20-fwd2610.yaml $D
TRIGGER=1  PROTO=20 MODE=ac $F ac20-bpt-dyn-trigger    $C/config-ac20-fwd2610.yaml $D V2G_INTEROP_BPT_FIRST=1
PROBE=mux  PROTO=20 MODE=dc $F mux-iso20-dyn           $C/config-mux-fwd2610.yaml $D
PROBE=mux  PROTO=both MODE=dc $F mux-both-2first-dyn   $C/config-mux-fwd2610.yaml $D V2G_INTEROP_SAP_FIRST=2
           PROTO=20 MODE=mcs $F mcs-dyn                $C/config-mcs-fwd2610.yaml $D
           PROTO=20 MODE=mcs $F mcs-bpt-dyn            $C/config-mcs-fwd2610.yaml $D V2G_INTEROP_BPT_FIRST=1
echo "=== DYN ARMS DONE $(date -Is)"
