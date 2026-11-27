#!/usr/bin/env bash
# Decode a receipt arm's exchanges [40]..[49] (status responses, receipts, PowerDelivery) with V2Gdecoder,
# straight from frames.log: a failed session writes no trace.json.   ARM=<arm> receipt-decode.sh
set -eu
F=$(ls /home/ahzf/everest/run/2610-repro/${ARM:-receipt}/rec-1/*.frames.log | head -1)
cd "$HOME/v2gdec/V2Gdecoder"
awk '/^\[(4[0-9])\](CurrentDemandRes|MeteringReceiptRes|PowerDeliveryRes|MeteringReceiptReq)/{h=$0; getline; gsub(/ /,"",$0); print h "|" substr($0,17)}' "$F" > /tmp/receipt.txt
while IFS='|' read -r head hex; do
  echo "########## $head"
  java -jar "$HOME/v2gdec/decoder.jar" -e -s "$hex" 2>/dev/null | sed 's/></>\n</g' \
    | grep -iE "ResponseCode|MeterInfo>|MeterID|MeterReading|SigMeterReading|ReceiptRequired|EVSENotification|DigestValue|SignatureValue|ChargeProgress" | head -10
done < /tmp/receipt.txt
