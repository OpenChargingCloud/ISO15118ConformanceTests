#!/bin/bash
# V2G_SECC_Sequence_Timeout in the -20 charge loop: how long does Evse15118D20 hold a silent session?
# Arm 1 (control): a normal charge, to prove the rig and the path.  Arm 2: silence after one loop.
set -u
EV=$HOME/everest
OUT=$EV/silrun
REPO=/mnt/d/Coding/OpenChargingCloud/ISO15118ConformanceTests
mkdir -p "$OUT"; rm -f "$OUT"/*.log

pkill -x manager 2>/dev/null; sleep 2
if ! pgrep -x mosquitto >/dev/null; then setsid mosquitto -d -p 1883 >/dev/null 2>&1; sleep 2; fi

arm() {
  NAME=$1; SILENT=$2
  echo "############ arm $NAME (V2G_INTEROP_SILENT=${SILENT:-unset}) ############"
  cd "$EV" || exit 1
  setsid "$EV/dist/bin/manager" --config "$EV/configs-ours/config-d20-ours.yaml" > "$OUT/station.$NAME.log" 2>&1 &
  MPID=$!
  sleep 12
  CP_AT_PLUGIN=1 setsid bash "$EV/sil-car.sh" > "$OUT/silcar.$NAME.log" 2>&1 &
  sleep 6

  # Evse15118D20 creates its TCP server only when an SDP request arrives, and picks the port then.
  # sdp-probe.sh takes an INTERFACE name, and writes the endpoint to stdout and errors to stderr —
  # so take stdout alone, or the socat error line's ff02::1 is mistaken for the answer.
  IFACE=${IFACE:-eth0}
  EP=$(bash "$EV/sdp-probe.sh" "$IFACE" 2>"$OUT/sdp.$NAME.err" | awk '{print $1}')
  echo "endpoint: ${EP:-NONE}   (iface $IFACE)"
  [ -z "$EP" ] && { cat "$OUT/sdp.$NAME.err"; ip -br -6 addr | sed 's/^/     /'; kill -TERM -"$MPID" 2>/dev/null; return; }

  START=$(date +%s)
  cd "$REPO" || exit 1
  env V2G_INTEROP_SECC="$EP" V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=dc \
      ${SILENT:+V2G_INTEROP_SILENT=$SILENT} \
      dotnet test -c Release ISO15118ConformanceTests.Simulation --artifacts-path ~/wsl-artifacts \
        --logger "console;verbosity=detailed" \
        --filter "FullyQualifiedName~EverestInteropTests.OurEvcc" > "$OUT/ours.$NAME.log" 2>&1
  echo "our side, exit=$? after $(( $(date +%s) - START ))s:"
  grep -aE "Authorization:|Energy transfer|Sequence_Timeout|allowed|Passed!|Failed!" "$OUT/ours.$NAME.log" | head -8

  echo "--- their station, the last words ---"
  sed 's/\x1b\[[0-9;]*m//g' "$OUT/station.$NAME.log" | grep -iE "sequence|timeout|session|charge loop|stopping" | tail -10
  kill -TERM -"$MPID" 2>/dev/null; sleep 1; kill -KILL -"$MPID" 2>/dev/null; sleep 3
  echo
}

arm control ""
arm silent 90
echo "=== logs in $OUT"
