#!/usr/bin/env bash
# One arm of the [V2G20-2379] chain-selection measurement.
# Run notes: docs/interop-runs/2026-08-12-everest-main-chain-selection/
#
# Stop any previous station FIRST, and stop it properly:
#   pkill -f "prefix <your-prefix>"        # the manager execs modules with --prefix as a FLAG,
#                                          # so "…/bin/manager" is not in their argv
#   pgrep -cf "[d]ist-main"                # bracket one letter, or pgrep matches its own shell
# Two managers on one MQTT prefix produce json type_error / "Promise already satisfied" and a
# manager-wide crash shutdown that looks exactly like a finding. It is not.
#
#   chain-arm.sh <label> <requestCAfile|none>
#
# Plugs the SIL car in, sends one SDP request asking for TLS, then connects with
# openssl s_client sending a certificate_authorities extension built from
# <requestCAfile> -- and prints which certificate chain the station served.
set -u

LOG=${STATION_LOG:-/home/ahzf/everest/mainstation3.log}
CARSIM=${CARSIM_TOPIC:-'everest_external/nodered/1/carsim/cmd'}
IFACE=${IFACE:-eth0}
LABEL=$1
CAFILE=$2

ready0=$(grep -c "D-LINK_READY (true)" "$LOG" || true)

mosquitto_pub -h localhost -t "$CARSIM/modify_charging_session" -m 'unplug'
sleep 5
mosquitto_pub -h localhost -t "$CARSIM/execute_charging_session" \
  -m 'sleep 2;iso_wait_slac_matched;iso_wait_pwm_is_running;draw_power_fixed 0,0;sleep 600'

for i in $(seq 1 60); do
  now=$(grep -c "D-LINK_READY (true)" "$LOG" || true)
  [ "$now" -gt "$ready0" ] && break
  sleep 1
done

printf '\x01\xfe\x90\x00\x00\x00\x00\x02\x00\x00' \
  | timeout 5 socat -T3 - "UDP6-DATAGRAM:[ff02::1%${IFACE}]:15118,bind=:15119" >/dev/null 2>&1

sleep 1
EP=$(sed 's/\x1b\[[0-9;]*m//g' "$LOG" | grep "Start TLS server" | tail -1 \
     | sed 's/.*Start TLS server \[\(.*\)\]:\([0-9]*\).*/\1 \2/')
ADDR=$(echo "$EP" | cut -d' ' -f1)
PORT=$(echo "$EP" | cut -d' ' -f2)
echo "### $LABEL  -> [$ADDR]:$PORT   requestCAfile=$CAFILE"

if [ "$CAFILE" = "none" ]; then
  OUT=$(timeout 12 openssl s_client -connect "[$ADDR]:$PORT" -tls1_3 -showcerts </dev/null 2>&1)
else
  OUT=$(timeout 12 openssl s_client -connect "[$ADDR]:$PORT" -tls1_3 -showcerts \
        -requestCAfile "$CAFILE" </dev/null 2>&1)
fi

echo "--- chain the station served ---"
echo "$OUT" | grep -E "^ *[0-9]+ s:|^ *i:" | head -8
echo "--- handshake ---"
echo "$OUT" | grep -E "^(New|Verify return code|Protocol|Cipher)" | head -4
