#!/usr/bin/env bash
# What the station puts in its CertificateRequest, and what it negotiates.
#
# The TLS 1.3 arm of [V2G20-2401] / [V2G20-1667] / [V2G20-2460]: the station only sends a
# CertificateRequest when the client offers TLS 1.3 (that is §1), so this arm has to be the
# TLS 1.3 one -- and it ends in "certificate required" because we present none. That is
# fine: openssl has already parsed and printed the CertificateRequest by then.
#
# Keeps the WHOLE transcript, not a grep of it. The 2026-08-10 run stored only what came
# back on the wire and not what went out, and that cost a wrong experiment two days later.
set -u

LOG=${STATION_LOG:-/home/ahzf/everest/mainstation5.log}
CARSIM=${CARSIM_TOPIC:-'everest_external/nodered/1/carsim/cmd'}
IFACE=${IFACE:-eth0}
OUT=${1:-/tmp/tlsprofile.txt}

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
ADDR=$(echo "$EP" | cut -d' ' -f1); PORT=$(echo "$EP" | cut -d' ' -f2)
echo "### TLS 1.3 handshake against [$ADDR]:$PORT, no client certificate"

timeout 15 openssl s_client -connect "[$ADDR]:$PORT" -tls1_3 -state -msg </dev/null > "$OUT" 2>&1

echo "--- [V2G20-2401]  certificate_authorities in the CertificateRequest ---"
grep -E "client certificate CA names|Acceptable client certificate CA names" "$OUT" | head -3 \
  || echo "(no CA-names line at all)"
echo "--- [V2G20-1667]  signature algorithms offered to us ---"
grep -A2 "Requested Signature Algorithms" "$OUT" | head -4 || echo "(none printed)"
echo "--- [V2G20-2460]  negotiated named group ---"
grep -E "Negotiated TLS1.3 group|Server Temp Key" "$OUT" | head -3 || echo "(none printed)"
echo "--- cipher suite (Table 6 -- the one they got right) ---"
grep -E "^New,|^Cipher" "$OUT" | head -2
echo "--- was a CertificateRequest actually sent? ---"
grep -E "CertificateRequest|certificate required" "$OUT" | head -3
echo
echo "full transcript: $OUT ($(wc -l < "$OUT") lines)"
