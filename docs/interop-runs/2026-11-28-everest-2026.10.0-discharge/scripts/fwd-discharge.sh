#!/usr/bin/env bash
# One FORWARD discharge arm against everest-core 2026.10.0: their station (dist-2610), our EVCC
# (EverestInteropTests.OurEvcc_AgainstTheirEvseV2G_RunsToCompletion) from ~/i15118, in WSL, straight to
# their link-local endpoint. 2026-11-28 variant of fwd-2610.sh: own tree (~/i15118-dis), own run dir, and
# LIMIT_W=<watts> published to their API module (everest_api/evse_manager/cmd/set_limit_watts) once the
# station is up; a negative value moves the import limit to 0 and the magnitude into the export schedule.
#
#   fwd-2610.sh <arm> <config> [VAR=value ...]       VAR=value pairs are exported for our EVCC
#
# Knobs (environment of this script, not of the EVCC):
#   PROTO=2|20  MODE=ac|dc|mcs   SESSIONS=n (sessions in the same manager process, default 1)
#   PROBE=sdp|mux|muxtls         sdp = fresh multicast SDP probe per session (required for Evse15118D20)
#   SDPSEC=10|00                 SDP security byte for the probe (10 = plaintext, 00 = TLS)
#   TRIGGER=1                    -20 AC: hold the car at state B and raise CP inside PowerDelivery's
#                                3 s contactor window (carsim-on-trigger.sh), instead of CP at plug-in
set -u
ARM=$1; CONF=$2; shift 2
EXTRA=("$@")
E=/home/ahzf/everest
D=$E/dist-2610
REPO=/mnt/d/Coding/OpenChargingCloud/ct-dis
RUN=$E/run/2610-discharge/fwd-$ARM
PROTO=${PROTO:-2}; MODE=${MODE:-dc}; SESSIONS=${SESSIONS:-1}; PROBE=${PROBE:-sdp}; SDPSEC=${SDPSEC:-10}
TRIGGER=${TRIGGER:-0}
rm -rf "$RUN"; mkdir -p "$RUN"
LOG=$RUN/their-side.log

bash $E/stop-rig2.sh > /dev/null
if ! pgrep -x mosquitto > /dev/null; then
  setsid /usr/sbin/mosquitto -p 1883 > "$RUN/mosquitto.log" 2>&1 < /dev/null &
  sleep 1
fi
ss -tln | grep -q ':1883 ' || { echo "FAIL: no broker on 1883"; exit 1; }
rm -rf /tmp/everest-logs; mkdir -p /tmp/everest-logs

echo "### arm $ARM  config $(basename "$CONF")  -$PROTO $MODE  sessions $SESSIONS  probe $PROBE  trigger $TRIGGER"
git -C $E/core-2610 log -1 --format='core-2610 at %h %ci'
date -Is

cd "$D" || exit 1
setsid ./bin/manager --config "$CONF" > "$LOG" 2>&1 < /dev/null &
i=0
while [ $i -lt 90 ]; do
  grep -qE "(evse_manager|connector_1)[^ ]* initialized" "$LOG" 2>/dev/null && break
  pgrep -x manager > /dev/null || { echo "FAIL: manager died at boot"; grep -iE "error|fail|abort" "$LOG" | head -15; exit 1; }
  sleep 1; i=$((i+1))
done
grep -qE "(evse_manager|connector_1)[^ ]* initialized" "$LOG" || { echo "FAIL: station never initialized"; tail -15 "$LOG"; exit 1; }
sleep 4
echo "station up after ${i}s"
if [ -n "${LIMIT_W:-}" ] && [ -z "${LIMIT_AFTER:-}" ]; then
  /usr/bin/mosquitto_pub -t everest_api/evse_manager/cmd/set_limit_watts -m "$LIMIT_W" && echo "published set_limit_watts $LIMIT_W"
  sleep 2
fi

LL=$(ip -6 addr show dev eth0 scope link | awk '/inet6/{print $2}' | cut -d/ -f1 | head -1)

for s in $(seq 1 "$SESSIONS"); do
  echo "---------------- session $s"
  pwm_before=$(grep -c "Set PWM On" "$LOG")
  pkill -f sil-car.sh 2>/dev/null; pkill -f carsim-on-trigger 2>/dev/null; sleep 1
  if [ "$TRIGGER" = 1 ]; then
    CP_AT_PLUGIN=0 setsid bash $E/sil-car.sh > "$RUN/car-$s.log" 2>&1 < /dev/null &
  else
    CP_AT_PLUGIN=1 setsid bash $E/sil-car.sh > "$RUN/car-$s.log" 2>&1 < /dev/null &
  fi
  i=0
  while [ $i -lt 45 ]; do
    [ "$(grep -c "Set PWM On" "$LOG")" -gt "$pwm_before" ] && break
    sleep 1; i=$((i+1))
  done
  if [ "$(grep -c "Set PWM On" "$LOG")" -le "$pwm_before" ]; then
    echo "FAIL: no 'Set PWM On' after plug-in (car log $(stat -c %s "$RUN/car-$s.log") bytes)"; continue
  fi
  echo "PWM on after ${i}s"
  if [ "$TRIGGER" = 1 ]; then
    setsid bash $REPO/tools/interop-everest/carsim-on-trigger.sh --watch "$LOG" > "$RUN/trigger-$s.log" 2>&1 < /dev/null &
    # [V2G20-848]: CP state B no later than 250 ms after PowerDeliveryReq(Stop). 2026.10.0 waits for the
    # contactor to open before it answers, so the SIL car has to leave state C on the same cue.
    if [ "${STOP_TRIGGER:-0}" = 1 ]; then
      TRIGGER_LINE='Waiting for contactor is opened' setsid bash $REPO/tools/interop-everest/carsim-on-trigger.sh \
        --watch "$LOG" --commands 'pause;sleep 600' > "$RUN/stop-trigger-$s.log" 2>&1 < /dev/null &
    fi
  fi

  EP=""
  case "$PROBE" in
    mux)    EP="[$LL%eth0]:61342" ;;
    muxtls) EP="[$LL%eth0]:64110" ;;
    *)
      for t in 1 2 3; do
        R=$(SECURITY=$SDPSEC bash $E/sdp-probe.sh eth0 2>&1) && { EP=${R%% *}; break; }
        echo "probe $t: $R"; sleep 2
      done ;;
  esac
  [ -z "$EP" ] && { echo "FAIL: no endpoint"; continue; }
  echo "endpoint $EP"
  # LIMIT_AFTER=<s>: publish the limit that many seconds into the session instead of at boot. An AC
  # station whose import limit is 0 before any BPT service is selected never switches PWM on, so for AC
  # the export has to be asked for once the charge loop runs.
  if [ -n "${LIMIT_W:-}" ] && [ -n "${LIMIT_AFTER:-}" ]; then
    ( sleep "$LIMIT_AFTER"; /usr/bin/mosquitto_pub -t everest_api/evse_manager/cmd/set_limit_watts -m "$LIMIT_W" \
        && echo "$(date +%T.%3N) published set_limit_watts $LIMIT_W" >> "$RUN/limit-$s.log" ) &
  fi

  (
    cd /home/ahzf/i15118-dis || exit 1
    export V2G_INTEROP_SECC="$EP" V2G_INTEROP_PROTOCOL=$PROTO V2G_INTEROP_MODE=$MODE V2G_INTEROP_RECORD=$RUN/rec-$s
    for kv in "${EXTRA[@]}"; do export "$kv"; done
    timeout 330 /usr/bin/dotnet test ISO15118ConformanceTests.Simulation/ISO15118ConformanceTests.Simulation.csproj -c Release \
      --no-build --artifacts-path /home/ahzf/wsl-artifacts-dis -l "console;verbosity=detailed" \
      --filter "FullyQualifiedName~EverestInteropTests.OurEvcc" > "$RUN/our-evcc-$s.log" 2>&1 < /dev/null
    echo "dotnet test exit $?"
  )
  echo "car log $(stat -c %s "$RUN/car-$s.log") bytes"
  grep -E "^\s*(Passed|Failed) |Error Message|Assert|negotiated|ResponseCode|FAILED_|SessionStop|exchanges" "$RUN/our-evcc-$s.log" | head -12
  sleep 3
done

sleep 2
echo "manager alive at end: $(pgrep -x manager | wc -l)"
bash $E/stop-rig2.sh > /dev/null
cp -r /tmp/everest-logs "$RUN/session-logs" 2>/dev/null
echo "### their side"
grep -iE "FAILED|error|segfault|signal|Shutdown|Selected|selected service|Handshake|Verify cert" "$LOG" | grep -viE "debug|error_history|ErrorHistory" | tail -12
