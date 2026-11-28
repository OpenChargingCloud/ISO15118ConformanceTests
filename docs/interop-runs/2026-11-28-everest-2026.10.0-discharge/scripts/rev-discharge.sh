#!/bin/bash
# Reverse discharge arm: their Ev15118 (everest-core 2026.10.0, dist-2610) against OUR SECC, -20 BPT in
# Dynamic mode, with our station discharging the car (V2G_INTEROP_DISCHARGE).
#   rev-discharge.sh <arm> <dc|ac> <magnitude: A for dc, W for ac | 0 = charge-only control>
# Builds from its own mirror (~/i15118-dis) so the main tree's mirror (~/i15118) is left alone.
set -u
ARM=$1; MODE=$2; AMOUNT=$3
E=/home/ahzf/everest
D=$E/dist-2610
WT=/mnt/d/Coding/OpenChargingCloud/ct-dis
SRC=$WT/docs/interop-runs/2026-11-06-everest-ev15118
TREE=/home/ahzf/i15118-dis
ART=/home/ahzf/wsl-artifacts-dis
RUN=$E/run/2610-discharge/$ARM
rm -rf "$RUN"; mkdir -p "$RUN/rec"
if [ "$MODE" = dc ]; then BASE=config-dc20-reverse-ev15118.yaml; CMD=dc_bpt; else BASE=config-ac20-reverse-ev15118.yaml; CMD=ac_bpt; fi
CONF=$RUN/config-$MODE-bpt-dynamic-reverse-2610.yaml
sed -E -e "s/iso_start_v2g_session (DC|AC);/iso_start_v2g_session $CMD;/" -e "s/^(\s*)control_mode: Scheduled/\1control_mode: Dynamic/" "$SRC/$BASE" > "$CONF"
sed -i "1i # 2026-11-28: $BASE with the session started as $CMD and control_mode Dynamic (Ev15118's default), run on dist-2610 (2026.10.0)." "$CONF"
grep -n -E "iso_start_v2g_session|control_mode" "$CONF"

if [ "${SKIP_BUILD:-0}" != 1 ]; then
  rsync -a --delete --exclude bin/ --exclude obj/ --exclude .git --exclude .vs/ --exclude TestResults/ "$WT/" "$TREE/"
  echo "rsync exit $?"
fi

bash $E/stop-rig2.sh > /dev/null
pkill -x dotnet 2>/dev/null; sleep 1
if ! pgrep -x mosquitto > /dev/null; then setsid /usr/sbin/mosquitto -p 1883 > "$RUN/mosquitto.log" 2>&1 < /dev/null & sleep 1; fi
ss -tln | grep -q ':1883 ' || { echo "FAIL: no broker"; exit 1; }
git -C $E/core-2610 log -1 --format='core-2610 at %h %ci'
git -C "$WT" log -1 --format='ours: worktree at %h %s' 2>/dev/null
date -Is

cd "$TREE"
if [ "${SKIP_BUILD:-0}" != 1 ]; then
  /usr/bin/dotnet build ISO15118ConformanceTests.Simulation/ISO15118ConformanceTests.Simulation.csproj -c Release --artifacts-path "$ART" -v q -nologo > "$RUN/build.log" 2>&1
  B=$?; echo "build exit $B"; [ $B -eq 0 ] || { grep -E "error" "$RUN/build.log" | head -20; exit 1; }
fi

DIS=""; [ "$AMOUNT" != 0 ] && DIS="$AMOUNT"
V2G_INTEROP_LISTEN=55000 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=$MODE V2G_INTEROP_BPT_FIRST=1 \
V2G_INTEROP_DYNAMIC=1 V2G_INTEROP_DISCHARGE=$DIS V2G_INTEROP_RECORD=$RUN/rec \
  setsid timeout 300 /usr/bin/dotnet test ISO15118ConformanceTests.Simulation/ISO15118ConformanceTests.Simulation.csproj -c Release \
    --no-build --artifacts-path "$ART" -l "console;verbosity=detailed" \
    --filter FullyQualifiedName~TheirPyEvJosev_AgainstOurSecc > "$RUN/our-secc.log" 2>&1 < /dev/null &
TP=$!
i=0; until ss -lnt | grep -q ':55000 '; do sleep 1; i=$((i+1)); [ $i -gt 120 ] && { echo "FAIL: our SECC never listened"; tail -20 "$RUN/our-secc.log"; exit 1; }; done
echo "our SECC listening after ${i}s"
cd "$D"
setsid ./bin/manager --config "$CONF" > "$RUN/their-side.log" 2>&1 < /dev/null &
wait $TP; echo "dotnet test exit $?"
sleep 2
bash $E/stop-rig2.sh > /dev/null
grep -E "Passed|Failed|Bestanden|Fehler|Total|Gesamt|Control mode|unconsulted|ignored" "$RUN/our-secc.log" | tail -8
ls "$RUN/rec"
