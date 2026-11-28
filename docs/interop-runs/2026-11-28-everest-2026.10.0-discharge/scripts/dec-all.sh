#!/bin/bash
# Decode every recorded session under ~/everest/run/2610-discharge and print the folded lines.
#   dec-all.sh [arm-glob]
R=/home/ahzf/everest/run/2610-discharge
for t in $R/${1:-*}/rec*/*.trace.json; do
  [ -f "$t" ] || continue
  d=$(dirname "$(dirname "$t")")
  echo "===== $(basename "$d")"
  bash /home/ahzf/everest/dec-run.sh "$t" "$d/decoded-loop.txt" > /dev/null
  grep -v "^       frame" "$d/decoded-loop.txt" | cut -c1-360
done
