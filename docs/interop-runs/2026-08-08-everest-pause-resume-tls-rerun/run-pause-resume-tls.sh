#!/usr/bin/env bash
# The 2026-08-08 pause/resume run, repeated against the fixed EVCC. Same rig, same config, same
# credential; the only variable is our state machine.
#
# Three rig facts drive the shape, all of them paid for on the first attempt:
#   * Evse15118D20 answers SDP only while no session is running, and a probe that is NOT followed by a
#     TCP connect leaves a session in exactly that state with nothing to time it out. Probe immediately
#     before connecting, never to "check the port".
#   * Both halves must share one station process -- pause_ctx lives in it.
#   * The manager writes a session log into $PWD, so run from $OUT and not from the repository.
export HOME=/home/ahzf
export PATH=$PATH:/usr/sbin
set -uo pipefail

REPO=/mnt/d/Coding/OpenChargingCloud/ISO15118ConformanceTests
CLI=$REPO/libs/EVSimulatorApp/simulation/Vanaheimr.V2G.Simulation.Cli
OUT=$HOME/everest/tlsrun
RUN=$HOME/everest/run
CARSIM="everest_external/nodered/1/carsim/cmd"
PLUG='sleep 2;iso_wait_slac_matched;iso_wait_pwm_is_running;draw_power_fixed 0,0;sleep 600'
mkdir -p "$OUT" "$RUN"
cd "$OUT" || exit 1

pkill -f 'dist/bin/manager' 2>/dev/null; sleep 2
setsid "$HOME/everest/dist/bin/manager" --conf "$HOME/everest/configs-ours/config-d20-tls-ours.yaml" \
    > "$RUN/rerun.charger.log" 2>&1 &
sleep 18
pgrep -f 'dist/bin/manager' >/dev/null || { echo "manager did not come up"; tail -15 "$RUN/rerun.charger.log"; exit 1; }
echo "station up (config-d20-tls-ours.yaml)"

replug() {
    mosquitto_pub -h localhost -t "$CARSIM/modify_charging_session" -m 'unplug' 2>/dev/null
    sleep 4
    mosquitto_pub -h localhost -t "$CARSIM/execute_charging_session" -m "$PLUG" 2>/dev/null
    sleep 10
}
probe() { timeout 20 bash "$HOME/everest/sdp-probe.sh" 2>/dev/null | grep -oE '\[[0-9a-f:]+%[a-z0-9]+\]:[0-9]+' | tail -1; }

common=(--protocol 20 --mode dc --tls --client-cert "$OUT/vehicle.p12" --client-cert-pass 123456)

half() {
    local label="$1"; shift
    echo
    echo "=== $label ==="
    replug
    local ep; ep=$(probe); echo "  SDP -> ${ep:-<none>}"
    [ -n "$ep" ] || { echo "  no SDP answer"; return 1; }
    dotnet run --project "$CLI" -c Release --no-build -- evcc --connect "$ep" "${common[@]}" "$@" \
        > "$OUT/rerun.$label.log" 2>&1
    echo "  exit $?"
    grep -E "TLS |session setup|Paused session id|Session complete|aborted|exchanges|resumed|refused" \
        "$OUT/rerun.$label.log" | sed 's/^/  /' | head -12
}

wc -l < "$RUN/rerun.charger.log" > "$RUN/rerun.mark"

half s1 --pause || exit 1
SID=$(grep -oE 'Paused session id: [0-9A-Fa-f]+' "$OUT/rerun.s1.log" | awk '{print $4}')
[ -n "$SID" ] || { echo "!! no paused session id"; tail -20 "$OUT/rerun.s1.log"; exit 1; }
echo "  paused session: $SID"

half s2 --resume "$SID" || exit 1

echo
echo "=== their station, this run ==="
tail -n +"$(cat "$RUN/rerun.mark")" "$RUN/rerun.charger.log" \
  | sed 's/\x1b\[[0-9;]*m//g' \
  | grep -iE "Old session resumed|New session created|Handshake complete|Verify certificate|Received session setup|Paused session|SequenceError|FAILED" \
  | tail -20 | sed 's/^/  /'
