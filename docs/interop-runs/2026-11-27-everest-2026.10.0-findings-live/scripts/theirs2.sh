#!/usr/bin/env bash
cd /home/ahzf/everest/run/2610-repro || exit 1
s() { sed 's/\x1b\[[0-9;]*m//g' "$1/their-side.log" | grep -v DEBUG; }
echo "=== eim-reject: around the FAILED"
s eim-reject | grep -vE "CAR ISO V2G AuthorizationReq|EVSE ISO V2G AuthorizationRes" | sed -n '/V2G session established/,$p' | grep -vE "Requesting|PWM" | head -30 | cut -c1-260
echo "=== eim-reject: first and last AuthorizationRes"
s eim-reject | grep "EVSE ISO V2G AuthorizationRes" | sed -n '1p;$p' | cut -c1-80
echo "=== reneg: from the renegotiation"
s reneg | sed -n '/PowerDeliveryReq/,$p' | grep -iE "renegot|cable|voltage|measure|power supply|powersupply|60 ?V|Inoperative|MREC|Cleared|shutdown|dlink" | head -30 | cut -c1-300
