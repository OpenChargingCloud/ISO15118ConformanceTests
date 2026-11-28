#!/bin/bash
# Reverse arm: their Ev15118 (everest-core 2026.10.0, dist-2610) against OUR SECC, -20 BPT.
#   rev-dsign.sh <arm> <dc|ac>
set -u
ARM=$1; MODE=$2
E=/home/ahzf/everest
D=$E/dist-2610
SRC=/mnt/d/Coding/OpenChargingCloud/ISO15118ConformanceTests/docs/interop-runs/2026-11-06-everest-ev15118
RUN=$E/run/2610-dsign/$ARM
rm -rf "$RUN"; mkdir -p "$RUN/rec"
if [ "$MODE" = dc ]; then BASE=config-dc20-reverse-ev15118.yaml; CMD=dc_bpt; else BASE=config-ac20-reverse-ev15118.yaml; CMD=ac_bpt; fi
CONF=$RUN/config-$MODE-bpt-reverse-2610.yaml
sed -E "s/iso_start_v2g_session (DC|AC);/iso_start_v2g_session $CMD;/" "$SRC/$BASE" > "$CONF"
sed -i "1i # 2026-11-28: $BASE with the session started as $CMD, run on dist-2610 (2026.10.0)." "$CONF"
grep -n "iso_start_v2g_session" "$CONF"

bash $E/stop-rig2.sh > /dev/null
pkill -x dotnet 2>/dev/null; sleep 1
if ! pgrep -x mosquitto > /dev/null; then setsid /usr/sbin/mosquitto -p 1883 > "$RUN/mosquitto.log" 2>&1 < /dev/null & sleep 1; fi
ss -tln | grep -q ':1883 ' || { echo "FAIL: no broker"; exit 1; }
git -C $E/core-2610 log -1 --format='core-2610 at %h %ci'
date -Is

cd ~/i15118
dotnet build ISO15118ConformanceTests.Simulation/ISO15118ConformanceTests.Simulation.csproj -c Release --artifacts-path ~/wsl-artifacts -v q -nologo > "$RUN/build.log" 2>&1
B=$?; echo "build exit $B"; [ $B -eq 0 ] || { tail -20 "$RUN/build.log"; exit 1; }

V2G_INTEROP_LISTEN=55000 V2G_INTEROP_SDP=eth0 V2G_INTEROP_PROTOCOL=20 V2G_INTEROP_MODE=$MODE V2G_INTEROP_BPT_FIRST=1 \
V2G_INTEROP_RECORD=$RUN/rec \
  setsid timeout 300 dotnet test ISO15118ConformanceTests.Simulation/ISO15118ConformanceTests.Simulation.csproj -c Release \
    --no-build --artifacts-path ~/wsl-artifacts -l "console;verbosity=detailed" \
    --filter FullyQualifiedName~TheirPyEvJosev_AgainstOurSecc > "$RUN/our-secc.log" 2>&1 < /dev/null &
TP=$!
i=0; until ss -lnt | grep -q ':55000 '; do sleep 1; i=$((i+1)); [ $i -gt 120 ] && { echo "FAIL: our SECC never listened"; tail -20 "$RUN/our-secc.log"; exit 1; }; done
echo "our SECC listening after ${i}s"
cd "$D"
setsid ./bin/manager --config "$CONF" > "$RUN/their-side.log" 2>&1 < /dev/null &
wait $TP; echo "dotnet test exit $?"
sleep 2
bash $E/stop-rig2.sh > /dev/null
grep -E "Passed|Failed|Bestanden|Fehler|Total|Gesamt" "$RUN/our-secc.log" | tail -5
ls "$RUN/rec"
