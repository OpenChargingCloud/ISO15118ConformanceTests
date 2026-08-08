#!/usr/bin/env bash
# Our EVCC pauses a -20 DC session against Evse15118D20 over mutual TLS, then reconnects and resumes
# with the old session id, re-presenting the same client certificate.
#
# Two rig facts drive the shape of this:
#   * Evse15118D20 answers SDP only when no session is running ("Ignoring sdp request message because a
#     session is already created and running"), so each half needs the simulated car replugged first.
#   * Their resume branch keys on SHA-512(session_id || vehicle_cert_hash) taken from the verified TLS
#     peer certificate, so both halves must present the same credential. That is the point of the run.
set -uo pipefail
REPO=/mnt/d/Coding/OpenChargingCloud/ISO15118ConformanceTests
CLI=$REPO/libs/EVSimulatorApp/simulation/Vanaheimr.V2G.Simulation.Cli
OUT=$HOME/everest/tlsrun
RUN=$HOME/everest/run
CARSIM="everest_external/nodered/1/carsim/cmd"
PLUG='sleep 2;iso_wait_slac_matched;iso_wait_pwm_is_running;draw_power_fixed 0,0;sleep 600'
mkdir -p "$OUT"
# The manager writes a session log into $PWD; keep it out of the repo.
cd "$OUT" || exit 1

# Restart the station so no half-open session is left over. Their SDP refuses while one exists, and a
# probe that is not followed by a TCP connect leaves exactly that -- which is how the first attempt at
# this run wedged itself. Both halves must then share this one process: pause_ctx lives in it.
pkill -f 'dist/bin/manager' 2>/dev/null; sleep 2
setsid "$HOME/everest/dist/bin/manager" --conf "$HOME/everest/configs-ours/config-d20-tls-ours.yaml"     > "$RUN/pr.charger.log" 2>&1 &
sleep 18
pgrep -f 'dist/bin/manager' >/dev/null || { echo "manager did not come up"; tail -10 "$RUN/pr.charger.log"; exit 1; }
echo "station restarted"

replug() {
    mosquitto_pub -h localhost -t "$CARSIM/modify_charging_session" -m 'unplug' 2>/dev/null
    sleep 4
    mosquitto_pub -h localhost -t "$CARSIM/execute_charging_session" -m "$PLUG" 2>/dev/null
    sleep 10
}
probe() { timeout 20 bash "$HOME/everest/sdp-probe.sh" 2>/dev/null | grep -oE '\[[0-9a-f:]+%[a-z0-9]+\]:[0-9]+' | tail -1; }

common=(--protocol 20 --mode dc --tls --client-cert "$OUT/vehicle.p12" --client-cert-pass 123456)

half() {  # $1 = label, $2.. = extra CLI args
    local label="$1"; shift
    echo "=== $label ==="
    replug
    local ep; ep=$(probe); echo "  SDP -> ${ep:-<none>}"
    [ -n "$ep" ] || { echo "  no SDP answer"; return 1; }
    dotnet run --project "$CLI" -c Release -- evcc --connect "$ep" "${common[@]}" "$@" \
        > "$OUT/evcc.$label.log" 2>&1
    echo "  exit $?"
    grep -E "TLS |session setup:|Paused session id|Session complete|aborted|exchanges" \
        "$OUT/evcc.$label.log" | sed 's/^/  /' | head -10
}

: > "$RUN/pr.charger.log.mark"; wc -l < "$RUN/pr.charger.log" > "$RUN/pr.charger.log.mark"

half s1 --pause || exit 1
SID=$(grep -oE 'Paused session id: [0-9A-Fa-f]+' "$OUT/evcc.s1.log" | awk '{print $4}')
[ -n "$SID" ] || { echo "!! no paused session id"; tail -20 "$OUT/evcc.s1.log"; exit 1; }

echo
half s2 --resume "$SID" || exit 1

echo
echo "=== their station, since the run started ==="
tail -n +"$(cat "$RUN/pr.charger.log.mark")" "$RUN/pr.charger.log" \
  | sed 's/\x1b\[[0-9;]*m//g' \
  | grep -iE "Old session resumed|New session created|Handshake complete|Verify certificate|session setup|SessionSetup" \
  | tail -14 | sed 's/^/  /'
