#!/usr/bin/env bash
# Bring up one 2026.10.0 station and plug the SIL car in (CP at state C from the plug-in), then return.
# The station keeps running; the caller drives the arm and calls stop-rig2.sh afterwards.
#   station-2610.sh <run-dir> <config>
set -u
RUN=$1; CONF=$2
E=/home/ahzf/everest
D=$E/dist-2610
LOG=$RUN/their-side.log
mkdir -p "$RUN"
bash $E/stop-rig2.sh > /dev/null
pkill -f sil-car.sh 2>/dev/null
if ! pgrep -x mosquitto > /dev/null; then
  setsid /usr/sbin/mosquitto -p 1883 > "$RUN/mosquitto.log" 2>&1 < /dev/null &
  sleep 1
fi
rm -rf /tmp/everest-logs; mkdir -p /tmp/everest-logs
cd "$D" || exit 1
setsid ./bin/manager --config "$CONF" > "$LOG" 2>&1 < /dev/null &
i=0
while [ $i -lt 90 ]; do
  grep -qE "(evse_manager|connector_1)[^ ]* initialized" "$LOG" 2>/dev/null && break
  pgrep -x manager > /dev/null || { echo "FAIL: manager died at boot"; grep -iE "error|fail|abort" "$LOG" | head -15; exit 1; }
  sleep 1; i=$((i+1))
done
grep -qE "(evse_manager|connector_1)[^ ]* initialized" "$LOG" || { echo "FAIL: station never initialized"; exit 1; }
sleep 4
CP_AT_PLUGIN=1 setsid bash $E/sil-car.sh > "$RUN/car.log" 2>&1 < /dev/null &
i=0
while [ $i -lt 45 ]; do
  grep -q "Set PWM On" "$LOG" && break
  sleep 1; i=$((i+1))
done
grep -q "Set PWM On" "$LOG" || { echo "FAIL: no PWM (car log $(stat -c %s "$RUN/car.log") bytes)"; exit 1; }
for j in $(seq 1 20); do
  grep -q "dlink_ready set to true\|D-LINK_READY (true)" "$LOG" && break
  sleep 1
done
sleep 1
echo "station up, PWM on"
