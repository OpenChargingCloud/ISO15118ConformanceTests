#!/usr/bin/env bash
# Copy the reproduction run into the repository worktree: configs, scripts, per-arm logs and flows.
set -u
E=/home/ahzf/everest
R=$E/run/2610-repro
SP=/mnt/c/Users/achim/AppData/Local/Temp/claude/443da193b49d45a8/b340fbb5-0a48-4a7c-bb63-fd8aec8ece2e/scratchpad/repro
OUT=/mnt/d/Coding/OpenChargingCloud/ct-repro/docs/interop-runs/2026-11-27-everest-2026.10.0-findings-live
strip() { sed 's/\x1b\[[0-9;]*m//g'; }
mkdir -p "$OUT/configs" "$OUT/scripts" "$OUT/tls" "$OUT/sessions" "$OUT/isomux"
cp $E/configs-2610/config-d20-tls-default-2610.yaml $E/configs-2610/config-d20-eim-reject-2610.yaml \
   $E/configs-2610/config-dc2-pnc-receipt-2610.yaml "$OUT/configs/"
cp $SP/station-2610.sh $SP/tls-arms-2610.sh $SP/evcc-arms-2610.sh $SP/meterinfo-decode.sh $SP/isomux-extract.sh \
   $SP/collect.sh $SP/inspect.sh $SP/theirs.sh $SP/theirs2.sh $SP/receipt-decode.sh $SP/meter-compare.sh $E/fwd-2610.sh $E/fwd-2610v.sh "$OUT/scripts/"
cp $R/isomux/* "$OUT/isomux/"
for a in ca12 ca12-replay ca13 ta-oem ta-mo ta-vehicle; do
  d=$R/tls/$a; [ -d "$d" ] || continue
  cp "$d/openssl.txt" "$OUT/tls/$a.openssl.txt"
  strip < "$d/their-side.log" | grep -E "iso15118_charge|evse_manager:Ev" | grep -v DEBUG > "$OUT/tls/$a.their-side.log"
  [ -f "$d/replay.out" ] && xxd "$d/replay.out" > "$OUT/tls/$a.station-answered.hex"
  [ -f "$d/req-sap.bin" ] && { xxd "$d/req-sap.bin"; xxd "$d/req-sessionsetup.bin"; } > "$OUT/tls/$a.sent.hex"
done
for a in meter-ctl meter seqto-dc seqto-ac eim-reject contactor-edge sid-zero sid-wrong certupd receipt reneg reneg-fwd; do
  d=$R/$a; [ -d "$d" ] || continue
  for s in 1 2; do
    [ -f "$d/our-evcc-$s.log" ] || continue
    f=$(ls $d/rec-$s/*.flow.md 2>/dev/null | head -1); [ -n "$f" ] && cp "$f" "$OUT/sessions/$a.s$s.flow.md"
    t=$(ls $d/rec-$s/*.frames.log 2>/dev/null | head -1); [ -n "$t" ] && cp "$t" "$OUT/sessions/$a.s$s.frames.log"
    grep -vE "^\s*$" "$d/our-evcc-$s.log" | grep -vE "^\s+at |Microsoft|Build started|NUnit Adapter|Test run for|VSTest" > "$OUT/sessions/$a.s$s.ours.txt"
  done
  strip < "$d/their-side.log" | grep -v DEBUG > "$OUT/sessions/$a.their-side.log"
  [ -f "$d/tokens.jsonl" ] && cp "$d/tokens.jsonl" "$OUT/sessions/$a.tokens.jsonl"
done
du -sh "$OUT"; ls -la "$OUT/sessions" | awk '{s+=$5} END {print s" bytes in sessions"}'
