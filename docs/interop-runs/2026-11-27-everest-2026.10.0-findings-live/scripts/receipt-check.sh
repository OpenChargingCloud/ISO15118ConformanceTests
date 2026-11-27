#!/usr/bin/env bash
# receipt-fixed / receipt-tamper: was the knob read, what did their side log, and does session 2's
# CurrentDemandRes still ask for a receipt? Plus where EvseManager hands ev_receipt_required to the HLC.
cd /home/ahzf/everest/run/2610-repro || exit 1
for a in receipt-fixed receipt-tamper; do
  for s in 1 2; do
    echo "=== $a s$s ours"
    grep -E "WARNING: set for this run|RECEIPT|Receipt|receipt" $a/our-evcc-$s.log | sort -u | head -5 | cut -c1-220
  done
  echo "=== $a theirs (receipt / signature / verify lines)"
  sed 's/\x1b\[[0-9;]*m//g' $a/their-side.log | grep -v DEBUG | grep -iE "receipt|signature|verif|digest|MeteringReceipt" | grep -viE "Requesting|contract cert chain" | head -12 | cut -c1-220
done
echo "=== session 2 of receipt-fixed: CurrentDemandRes decoded"
F=$(ls receipt-fixed/rec-2/*.frames.log | head -1)
cd "$HOME/v2gdec/V2Gdecoder"
awk '/^\[4[4-6]\] CurrentDemandRes/{h=$0; getline; gsub(/ /,"",$0); print h "|" substr($0,17)}' "$F" | while IFS='|' read -r head hex; do
  echo "## $head"
  java -jar "$HOME/v2gdec/decoder.jar" -e -s "$hex" 2>/dev/null | sed 's/></>\n</g' | grep -iE "MeterID|MeterReading|ReceiptRequired" | head -4
done
echo "=== EvseManager: when is receipt_is_required called"
C=/home/ahzf/everest/core-2610/modules/EVSE
sed -n 1085,1100p $C/EvseManager/EvseManager.cpp
grep -n "receipt_required" $C/EvseV2G/v2g_ctx.cpp $C/EvseV2G/charger/ISO15118_chargerImpl.cpp | head
