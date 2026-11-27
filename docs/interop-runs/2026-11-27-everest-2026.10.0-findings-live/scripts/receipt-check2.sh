#!/usr/bin/env bash
# Session 2 of receipt-fixed: does CurrentDemandRes still ask for a receipt? And who resets receipt_required.
F=$(ls /home/ahzf/everest/run/2610-repro/receipt-fixed/rec-2/*.frames.log | head -1)
cd "$HOME/v2gdec/V2Gdecoder"
awk '/^\[4[4-6]\] CurrentDemandRes/{h=$0; getline; gsub(/ /,"",$0); print h "|" substr($0,17)}' "$F" | while IFS='|' read -r head hex; do
  echo "## $head"
  java -jar "$HOME/v2gdec/decoder.jar" -e -s "$hex" 2>/dev/null | sed 's/></>\n</g' | grep -iE "MeterID|MeterReading|ReceiptRequired" | head -4
done
C=/home/ahzf/everest/core-2610/modules/EVSE/EvseV2G
echo "=== v2g_ctx.cpp around :329"
awk 'NR>=290 && NR<=332 && /^[a-z].*\(|receipt_required/ {print NR": "$0}' $C/v2g_ctx.cpp
echo "=== callers"
grep -rn "v2g_ctx_init_charging_values\|init_charging_values" $C/*.cpp $C/*/*.cpp | head
