#!/usr/bin/env bash
# Forward discharge arms, 2026-11-28: our EVCC (stack master f9dd169, negative discharge limits) against
# their Evse15118D20 + EvseManager (2026.10.0), -20 BPT in Dynamic mode, with their station told to
# export through the API module (LIMIT_W < 0). A control arm without the limit after each.
set -u
E=/home/ahzf/everest
C=$E/configs-2610
F="bash $E/fwd-discharge.sh"
D=V2G_INTEROP_DYNAMIC=1
B=V2G_INTEROP_BPT_FIRST=1

LIMIT_W=-5000                PROTO=20 MODE=dc $F d20-bpt-dyn-export      $C/config-d20-fwd2610.yaml  $D $B
                             PROTO=20 MODE=dc $F d20-bpt-dyn-control     $C/config-d20-fwd2610.yaml  $D $B
LIMIT_W=-5000 TRIGGER=1 STOP_TRIGGER=1 PROTO=20 MODE=ac $F ac20-bpt-dyn-export  $C/config-ac20-fwd2610.yaml $D $B
              TRIGGER=1 STOP_TRIGGER=1 PROTO=20 MODE=ac $F ac20-bpt-dyn-control $C/config-ac20-fwd2610.yaml $D $B
echo "=== FORWARD DISCHARGE ARMS DONE $(date -Is)"
