#!/usr/bin/env bash
# Copy the 2026-11-28 discharge run into the ct-dis worktree: per arm the recorder's artifacts, both
# logs, the derived config and the decoded loop; plus every script the run used.
set -u
R=/home/ahzf/everest/run/2610-discharge
E=/home/ahzf/everest
SP=/mnt/c/Users/achim/AppData/Local/Temp/claude/443da193b49d45a8/b340fbb5-0a48-4a7c-bb63-fd8aec8ece2e/scratchpad
OUT=/mnt/d/Coding/OpenChargingCloud/ct-dis/docs/interop-runs/2026-11-28-everest-2026.10.0-discharge
mkdir -p "$OUT/reverse" "$OUT/forward" "$OUT/scripts/decode-loop"
X="--exclude session-logs/ --exclude build.log --exclude mosquitto.log"
for a in dc20-bpt-dyn-discharge dc20-bpt-dyn-control ac20-bpt-dyn-discharge; do
  rsync -a $X "$R/$a/" "$OUT/reverse/$a/"
done
for a in d20-bpt-dyn-export d20-bpt-dyn-control d20-bpt-dyn-export-long d20-bpt-dyn-export-late \
         ac20-bpt-dyn-export ac20-bpt-dyn-control ac20-bpt-dyn-export-late; do
  rsync -a $X "$R/fwd-$a/" "$OUT/forward/$a/"
done
cp "$E/rev-discharge.sh" "$E/fwd-discharge.sh" "$E/fwd-discharge-batch.sh" "$E/fwd-discharge-batch2.sh" \
   "$E/sync-build-dis.sh" "$E/dec-run.sh" "$E/dec-all.sh" "$OUT/scripts/"
cp "$SP/decode-loop/Program.cs" "$SP/decode-loop/dec.csproj" "$OUT/scripts/decode-loop/"
cp "$R/../2610-discharge-fwd.log" "$OUT/forward/first-pass.log"
cp "$R/../2610-discharge-fwd2.log" "$OUT/forward/second-pass.log"
cat "$R/../2610-discharge-dc.log" "$R/../2610-discharge-ac.log" "$R/../2610-discharge-dcctl.log" > "$OUT/reverse/arms.log"
du -sh "$OUT"; find "$OUT" -type f | wc -l
