#!/bin/bash
# [V2G20-460] against Evse15118D20: correct id (control) vs eight zero bytes.
set -u
EV=$HOME/everest
OUT=$EV/sid20run
REPO=/mnt/d/Coding/OpenChargingCloud/ISO15118ConformanceTests
mkdir -p "$OUT"; rm -f "$OUT"/*.log

pkill -x manager 2>/dev/null; sleep 2
if ! pgrep -x mosquitto >/dev/null; then setsid mosquitto -d -p 1883 >/dev/null 2>&1; sleep 2; fi

arm() {
  NAME=$1; SID=$2
  echo "############ arm $NAME (V2G_INTEROP_SESSIONID=${SID:-unset}) ############"
  cd "$EV" || exit 1
  setsid "$EV/dist/bin/manager" --config "$EV/configs-ours/config-d20-ours.yaml" > "$OUT/station.$NAME.log" 2>&1 &
  MPID=$!
  sleep 12
  CP_AT_PLUGIN=1 setsid bash "$EV/sil-car.sh" > "$OUT/silcar.$NAME.log" 2>&1 &
  sleep 6
  EP=$(bash "$EV/sdp-probe.sh" eth0 2>"$OUT/sdp.$NAME.err" | awk '{print $1}')
  echo "endpoint: ${EP:-NONE}"
  [ -z "$EP" ] && { cat "$OUT/sdp.$NAME.err"; kill -TERM -"$MPID" 2>/dev/null; return; }

  cd "$REPO" || exit 1
  env V2G_INTEROP_SECC="$EP" V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc \
      ${SID:+V2G_INTEROP_SESSIONID=$SID} \
      dotnet test -c Release ISO15118ConformanceTests.Simulation --artifacts-path ~/wsl-artifacts \
        --logger "console;verbosity=detailed" \
        --filter "FullyQualifiedName~EverestInteropTests.OurEvcc" > "$OUT/ours.$NAME.log" 2>&1
  echo "our side, exit=$?:"
  grep -aE "Authorization:|Energy transfer|SessionAborted|FAILED|Passed!|Failed!|answered" "$OUT/ours.$NAME.log" | sed 's/^ *//' | sort -u | head -8

  echo "--- their station ---"
  sed 's/\x1b\[[0-9;]*m//g' "$OUT/station.$NAME.log" | grep -aiE "session|unknown|V2G " | tail -8
  kill -TERM -"$MPID" 2>/dev/null; sleep 1; kill -KILL -"$MPID" 2>/dev/null; sleep 3
  echo
}

arm control ""
arm zero zero
echo "=== logs in $OUT"
