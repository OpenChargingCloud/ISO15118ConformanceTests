#!/usr/bin/env bash
# theirs.sh — the lines of their log that decide each arm
cd /home/ahzf/everest/run/2610-repro || exit 1
s() { sed 's/\x1b\[[0-9;]*m//g' "$1/their-side.log" | grep -v DEBUG; }
echo "=== eim-reject"
s eim-reject | grep -iE "Result for token|REJECTED|Accepted|authoriz|timeout|Timeout|FAILED|EIM|WARNING_|Session Stop|session stop" | grep -v "Requesting" | head -20 | cut -c1-230
echo "=== seqto-dc"
s seqto-dc | grep -iE "timeout|Timeout|ChargeLoop|DcChargeLoop|Session stop|SessionStop|stop|Closing" | grep -viE "Requesting|PWM" | tail -12 | cut -c1-230
echo "=== seqto-ac"
s seqto-ac | grep -iE "timeout|Timeout|AcChargeLoop|Session stop|SessionStop|Closing" | grep -viE "Requesting|PWM" | tail -12 | cut -c1-230
echo "=== reneg"
s reneg | grep -iE "cable check|CableCheck|60V|Inoperative|MREC|error shutdown|Error raised|renegot|dlink|SDP" | tail -20 | cut -c1-230
echo "=== contactor-edge"
s contactor-edge | grep -iE "contactor|PowerOn|PowerDelivery|PWM|Car Paused|state C|CPEvent" | tail -14 | cut -c1-230
echo "=== certupd"
s certupd | grep -iE "Certificate|Payment|Update|FAILED" | grep -v Requesting | tail -8 | cut -c1-230
