#!/usr/bin/env bash
# One FORWARD -2 Plug & Charge arm against everest-core 2026.10.0, optionally with our contract
# validator standing in for the CSMS. fwd-2610.sh (2026-11-26) plus one knob:
#
#   VALIDATOR="--status Accepted [--certificate-status X]"   start the manager with
#       --standalone token_validator and answer on its topics with contract-validator.py; every
#       validate_token call is logged whole to $RUN/tokens.jsonl
#   (unset)                                                  stock manager, DummyTokenValidator answers
#
#   fwd-2610v.sh <arm> <config> [VAR=value ...]      VAR=value pairs are exported for our EVCC
set -u
ARM=$1; CONF=$2; shift 2
EXTRA=("$@")
E=/home/ahzf/everest
D=$E/dist-2610
REPO=/mnt/d/Coding/OpenChargingCloud/ISO15118ConformanceTests
RUN=$E/run/2610-pnc2/$ARM
PROTO=${PROTO:-2}; MODE=${MODE:-dc}; SESSIONS=${SESSIONS:-1}; SDPSEC=${SDPSEC:-10}
VALIDATOR=${VALIDATOR:-}
rm -rf "$RUN"; mkdir -p "$RUN"
LOG=$RUN/their-side.log

bash $E/stop-rig2.sh > /dev/null
for p in $(pgrep -f 'interop-everest/contract-validator[.]py'); do kill "$p"; done
if ! pgrep -x mosquitto > /dev/null; then
  setsid /usr/sbin/mosquitto -p 1883 > "$RUN/mosquitto.log" 2>&1 < /dev/null &
  sleep 1
fi
ss -tln | grep -q ':1883 ' || { echo "FAIL: no broker on 1883"; exit 1; }
rm -rf /tmp/everest-logs; mkdir -p /tmp/everest-logs

echo "### arm $ARM  config $(basename "$CONF")  -$PROTO $MODE  sessions $SESSIONS  validator '${VALIDATOR:-dummy}'"
git -C $E/core-2610 log -1 --format='core-2610 at %h %ci'
date -Is

cd "$D" || exit 1
if [ -n "$VALIDATOR" ]; then
  setsid ./bin/manager --config "$CONF" --standalone token_validator > "$LOG" 2>&1 < /dev/null &
  i=0
  while [ $i -lt 60 ]; do
    grep -q "waiting for standalone modules" "$LOG" 2>/dev/null && break
    sleep 1; i=$((i+1))
  done
  grep -q "waiting for standalone modules" "$LOG" || { echo "FAIL: manager never waited for the standalone module"; tail -15 "$LOG"; exit 1; }
  # shellcheck disable=SC2086
  DIST=$D setsid python3 $REPO/tools/interop-everest/contract-validator.py --config "$CONF" \
      --log "$RUN/tokens.jsonl" $VALIDATOR > "$RUN/validator.log" 2>&1 < /dev/null &
  echo "manager holding after ${i}s, validator started"
else
  setsid ./bin/manager --config "$CONF" > "$LOG" 2>&1 < /dev/null &
fi
i=0
while [ $i -lt 90 ]; do
  grep -qE "(evse_manager|connector_1)[^ ]* initialized" "$LOG" 2>/dev/null && break
  pgrep -x manager > /dev/null || { echo "FAIL: manager died at boot"; grep -iE "error|fail|abort" "$LOG" | head -15; exit 1; }
  sleep 1; i=$((i+1))
done
grep -qE "(evse_manager|connector_1)[^ ]* initialized" "$LOG" || { echo "FAIL: station never initialized"; tail -15 "$LOG"; [ -n "$VALIDATOR" ] && tail -15 "$RUN/validator.log"; exit 1; }
sleep 4
echo "station up after ${i}s"
[ -n "$VALIDATOR" ] && grep -E "registered|answering|ready" "$RUN/validator.log"

LL=$(ip -6 addr show dev eth0 scope link | awk '/inet6/{print $2}' | cut -d/ -f1 | head -1)

for s in $(seq 1 "$SESSIONS"); do
  echo "---------------- session $s"
  pwm_before=$(grep -c "Set PWM On" "$LOG")
  dlink_before=$(grep -c "SDP dlink_ready set to true" "$LOG")
  pkill -f sil-car.sh 2>/dev/null; sleep 1
  CP_AT_PLUGIN=1 setsid bash $E/sil-car.sh > "$RUN/car-$s.log" 2>&1 < /dev/null &
  i=0
  while [ $i -lt 45 ]; do
    [ "$(grep -c "Set PWM On" "$LOG")" -gt "$pwm_before" ] && break
    sleep 1; i=$((i+1))
  done
  if [ "$(grep -c "Set PWM On" "$LOG")" -le "$pwm_before" ]; then
    echo "FAIL: no 'Set PWM On' after plug-in (car log $(stat -c %s "$RUN/car-$s.log") bytes)"; continue
  fi
  echo "PWM on after ${i}s"
  # A refused session leaves one more "Set PWM On" during its own unplug, before the next plug-in, so
  # PWM alone can be the old session's. Their SDP server answers only once the data link is ready.
  i=0
  while [ $i -lt 30 ]; do
    [ "$(grep -c "SDP dlink_ready set to true" "$LOG")" -gt "$dlink_before" ] && break
    sleep 1; i=$((i+1))
  done
  echo "dlink ready after ${i}s more"

  EP=""
  for t in 1 2 3; do
    R=$(SECURITY=$SDPSEC bash $E/sdp-probe.sh eth0 2>&1) && { EP=${R%% *}; break; }
    echo "probe $t: $R"; sleep 2
  done
  [ -z "$EP" ] && { echo "FAIL: no endpoint"; continue; }
  echo "endpoint $EP"

  (
    cd /home/ahzf/i15118 || exit 1
    export V2G_INTEROP_SECC="$EP" V2G_INTEROP_PROTOCOL=$PROTO V2G_INTEROP_MODE=$MODE V2G_INTEROP_RECORD=$RUN/rec-$s
    for kv in "${EXTRA[@]}"; do export "$kv"; done
    timeout 330 dotnet test ISO15118ConformanceTests.Simulation/ISO15118ConformanceTests.Simulation.csproj -c Release \
      --no-build --artifacts-path /home/ahzf/wsl-artifacts -l "console;verbosity=detailed" \
      --filter "FullyQualifiedName~EverestInteropTests.OurEvcc" > "$RUN/our-evcc-$s.log" 2>&1 < /dev/null
    echo "dotnet test exit $?"
  )
  echo "car log $(stat -c %s "$RUN/car-$s.log") bytes"
  grep -E "^\s*(Passed|Failed) |Error Message|Authorization|Battery|FAILED_|SessionStop|exchanges" "$RUN/our-evcc-$s.log" | head -12
  sleep 3
done

sleep 2
echo "manager alive at end: $(pgrep -x manager | wc -l)"
bash $E/stop-rig2.sh > /dev/null
for p in $(pgrep -f 'interop-everest/contract-validator[.]py'); do kill "$p"; done
cp -r /tmp/everest-logs "$RUN/session-logs" 2>/dev/null
if [ -n "$VALIDATOR" ]; then
  echo "### validator"
  grep -E " #[0-9]+ " "$RUN/validator.log"
fi
echo "### their side"
grep -iE "FAILED|error|segfault|signal|Shutdown|Authoriz|token|Handshake|Verify cert|ocsp" "$LOG" | grep -viE "debug|error_history|ErrorHistory" | tail -14
