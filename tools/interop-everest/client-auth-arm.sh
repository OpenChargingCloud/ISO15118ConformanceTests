#!/usr/bin/env bash
# One arm of the [V2G20-2400] client-authentication measurement.
# Run notes: docs/interop-runs/2026-08-12-everest-main-client-auth/
#
# The oracle is the station's own "Change verify mode" line, COUNTED before and after --
# not the s_client output. In TLS 1.3 the CertificateRequest is encrypted, so s_client
# does not print "Acceptable client certificate CA names" the way it does for TLS 1.2 and
# the detection below is only a hint; the alert and the counter are what decide.
#
#   clientauth-arm.sh <label> <tls1_2|tls1_3>
#
# Plugs the SIL car in, sends one SDP request asking for TLS, then connects with
# openssl s_client offering exactly one TLS version and NO client certificate --
# and reports whether the station asked for one.
set -u

LOG=${STATION_LOG:-/home/ahzf/everest/mainstation4.log}
CARSIM=${CARSIM_TOPIC:-'everest_external/nodered/1/carsim/cmd'}
IFACE=${IFACE:-eth0}
LABEL=$1
VERSION=$2

ready0=$(grep -c "D-LINK_READY (true)" "$LOG" || true)
verify0=$(grep -c "Change verify mode" "$LOG" || true)

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
echo "### $LABEL   -> [$ADDR]:$PORT   offering ${VERSION} only, no client certificate"

OUT=$(timeout 12 openssl s_client -connect "[$ADDR]:$PORT" "-${VERSION}" </dev/null 2>&1)

echo "--- did the station ask for a client certificate? ---"
if echo "$OUT" | grep -q "Acceptable client certificate CA names\|Client Certificate Types"; then
  echo "YES -- CertificateRequest seen"
  echo "$OUT" | grep -A2 "Acceptable client certificate CA names" | head -4
else
  echo "NO CertificateRequest in the handshake output"
fi
echo "--- outcome ---"
echo "$OUT" | grep -E "^(New|Protocol|Cipher|Verify return code)|alert|Handshake" | head -6
echo "--- station log, this arm ---"
verify1=$(grep -c "Change verify mode" "$LOG" || true)
echo "\"Change verify mode\" lines: $verify0 -> $verify1"
sed 's/\x1b\[[0-9;]*m//g' "$LOG" | tail -6 | grep -E "iso15118_charge|TLS" | cut -c1-150
