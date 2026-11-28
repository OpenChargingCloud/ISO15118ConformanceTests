#!/usr/bin/env bash
# Forward discharge arms, second pass (2026-11-28): the first pass ran our car's fixed three charge loops,
# too short to see their station current settle. Here a battery-driven session of up to 300 iterations
# (simulated minutes, at the -20 car own 50 ms pacing), and for AC the limit published 6 s in.
set -u
E=/home/ahzf/everest
C=$E/configs-2610
F="bash $E/fwd-discharge.sh"
D=V2G_INTEROP_DYNAMIC=1
B=V2G_INTEROP_BPT_FIRST=1
L=V2G_INTEROP_BATTERY=kwh=60,soc=50,target=90,power=0,minutes=300

LIMIT_W=-5000                PROTO=20 MODE=dc $F d20-bpt-dyn-export-long   $C/config-d20-fwd2610.yaml  $D $B $L
LIMIT_W=-5000 LIMIT_AFTER=6  PROTO=20 MODE=dc $F d20-bpt-dyn-export-late   $C/config-d20-fwd2610.yaml  $D $B $L
LIMIT_W=-5000 LIMIT_AFTER=6 TRIGGER=1 STOP_TRIGGER=1 PROTO=20 MODE=ac $F ac20-bpt-dyn-export-late $C/config-ac20-fwd2610.yaml $D $B $L
echo "=== SECOND PASS DONE $(date -Is)"
