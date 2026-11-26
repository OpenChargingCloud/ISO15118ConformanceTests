#!/usr/bin/env bash
# The plaintext forward arms against 2026.10.0, one after another. Each arm restarts the manager.
#   plain-2610.sh [arm ...]     (no argument = all)
set -u
E=/home/ahzf/everest
C=$E/configs-2610
F="bash $E/fwd-2610.sh"
WANTED=" $* "
want() { [ "$WANTED" = "  " ] || [[ "$WANTED" == *" $1 "* ]]; }

want ac2              && SESSIONS=2 PROTO=2  MODE=ac $F ac2              $C/config-ac2-fwd2610.yaml
want d20              && SESSIONS=2 PROTO=20 MODE=dc $F d20              $C/config-d20-fwd2610.yaml
want d20-dynamic      &&            PROTO=20 MODE=dc $F d20-dynamic      $C/config-d20-fwd2610.yaml V2G_INTEROP_DYNAMIC=1
want d20-bpt          &&            PROTO=20 MODE=dc $F d20-bpt          $C/config-d20-fwd2610.yaml V2G_INTEROP_BPT_FIRST=1
want ac20-cp          &&            PROTO=20 MODE=ac $F ac20-cp          $C/config-ac20-fwd2610.yaml
want ac20-trigger     && SESSIONS=2 TRIGGER=1 PROTO=20 MODE=ac $F ac20-trigger $C/config-ac20-fwd2610.yaml
want ac20-bpt-trigger && TRIGGER=1  PROTO=20 MODE=ac $F ac20-bpt-trigger $C/config-ac20-fwd2610.yaml V2G_INTEROP_BPT_FIRST=1
want mux-iso2         && PROBE=mux  PROTO=2  MODE=dc $F mux-iso2         $C/config-mux-fwd2610.yaml
want mux-iso20        && PROBE=mux  PROTO=20 MODE=dc $F mux-iso20        $C/config-mux-fwd2610.yaml
want mux-both-20first && PROBE=mux  PROTO=both MODE=dc $F mux-both-20first $C/config-mux-fwd2610.yaml V2G_INTEROP_SAP_FIRST=20
want mux-both-2first  && PROBE=mux  PROTO=both MODE=dc $F mux-both-2first  $C/config-mux-fwd2610.yaml V2G_INTEROP_SAP_FIRST=2
want mcs              &&            PROTO=20 MODE=mcs $F mcs             $C/config-mcs-fwd2610.yaml
want mcs-bpt          &&            PROTO=20 MODE=mcs $F mcs-bpt         $C/config-mcs-fwd2610.yaml V2G_INTEROP_BPT_FIRST=1
echo "=== PLAIN ARMS DONE $(date -Is)"
