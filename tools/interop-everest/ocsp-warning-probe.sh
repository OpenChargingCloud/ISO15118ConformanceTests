#!/usr/bin/env bash
# Does Evse15118D20 log "<n> certificates != <n> OCSP responses" when a TLS session
# is set up?  One plug-in, one SDP request asking for TLS, counted before and after.
#
# Written for everest-core main (ebcd36d); on 2026.02.1 the warning cannot appear at
# all, because the -20 stack had no OCSP plumbing to disagree with itself about.
# Run notes: docs/interop-runs/2026-08-12-everest-main-ocsp-warning/
#
# Three things this probe learned the hard way, all worth keeping:
#   - the warning is NOT emitted at process start; ConnectionSSL is built per SDP
#     request, in TbdController::handle_sdp_server_input()
#   - an SDP request alone is not enough either: without a plugged-in car the station
#     answers "Ignoring SDP request because dlink is not ready" and returns before the
#     connection factory runs
#   - their SIL car never sent an SDP request in any run here, so the probe sends its
#     own datagram inside the 18 s communication-setup window
set -u

LOG=${STATION_LOG:-/home/ahzf/everest/mainstation.log}
CARSIM=${CARSIM_TOPIC:-'everest_external/nodered/1/carsim/cmd'}
IFACE=${IFACE:-eth0}

before=$(grep -c "OCSP responses" "$LOG" || true)
ready0=$(grep -c "D-LINK_READY (true)" "$LOG" || true)
echo "before: OCSP-warning lines = $before"

mosquitto_pub -h localhost -t "$CARSIM/modify_charging_session" -m 'unplug'
sleep 5
mosquitto_pub -h localhost -t "$CARSIM/execute_charging_session" \
  -m 'sleep 2;iso_wait_slac_matched;iso_wait_pwm_is_running;draw_power_fixed 0,0;sleep 600'

echo -n "waiting for D-LINK_READY"
for i in $(seq 1 60); do
  now=$(grep -c "D-LINK_READY (true)" "$LOG" || true)
  if [ "$now" -gt "$ready0" ]; then echo " -> ready after ${i}s"; break; fi
  echo -n "."
  sleep 1
done

# One SDP request, security byte 0x00 = TLS, transport 0x00 = TCP.
printf '\x01\xfe\x90\x00\x00\x00\x00\x02\x00\x00' \
  | timeout 5 socat -T3 - "UDP6-DATAGRAM:[ff02::1%${IFACE}]:15118,bind=:15119" \
  | od -An -tx1 | head -2

sleep 4
after=$(grep -c "OCSP responses" "$LOG" || true)
echo "after:  OCSP-warning lines = $after"
echo "--- station reaction to the SDP request ---"
sed 's/\x1b\[[0-9;]*m//g' "$LOG" | tail -14 | cut -c1-150
