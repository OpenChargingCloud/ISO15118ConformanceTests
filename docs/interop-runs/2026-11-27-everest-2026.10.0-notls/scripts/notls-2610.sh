#!/usr/bin/env bash
# NoTLS arms against everest-core 2026.10.0: their EV (Ev15118) against their own -20 station
# (Evse15118D20), one manager per arm, auto_exec starting the session at boot. Run as ROOT, for the
# capture; the manager runs as ahzf.
#   notls-2610.sh [arm ...]      arms: notls-prefer notls-prefer-both notls-require tls-prefer
set -u
U=ahzf
E=/home/ahzf/everest
D=$E/dist-2610
C=$E/configs-2610
R=$E/run/2610-notls
ARMS=${*:-notls-prefer notls-prefer-both notls-require tls-prefer}
SECS=${SECS:-75}

as_user() { runuser -u $U -- "$@"; }

for ARM in $ARMS; do
  RUN=$R/$ARM
  as_user rm -rf "$RUN"; as_user mkdir -p "$RUN"
  as_user bash $E/stop-rig2.sh > /dev/null
  pgrep -x mosquitto > /dev/null || { as_user setsid /usr/sbin/mosquitto -p 1883 > /dev/null 2>&1 < /dev/null & sleep 1; }
  rm -rf /tmp/everest-logs; as_user mkdir -p /tmp/everest-logs
  echo "################ $ARM  $(date -Is)"

  # SDP is UDP 15118; the V2G session is the station's TCP port. MQTT (1883) excluded.
  timeout $((SECS + 5)) tcpdump -i any -U -s 0 -w "$RUN/capture.pcap" 'udp port 15118 or (tcp and not port 1883)' \
      > "$RUN/tcpdump.log" 2>&1 &
  sleep 1

  ( cd $D && as_user setsid ./bin/manager --config "$C/config-$ARM.yaml" > "$RUN/their-side.log" 2>&1 < /dev/null & )
  sleep "$SECS"
  echo "manager alive: $(pgrep -x manager | wc -l)"
  as_user bash $E/stop-rig2.sh > /dev/null
  sleep 6
  chown $U:$U "$RUN/capture.pcap" "$RUN/tcpdump.log" 2>/dev/null
  cp -r /tmp/everest-logs "$RUN/session-logs" 2>/dev/null; chown -R $U:$U "$RUN" 2>/dev/null

  L="$RUN/their-side.log"
  sed -i 's/\x1b\[[0-9;]*m//g' "$L"
  echo "--- SDP / TLS / SAP / outcome"
  grep -iE "SDP|TLS|plaintext|tls|SupportedAppProtocol|namespace|protocol|SessionStop|Session Finished|error|FAILED" "$L" \
    | grep -viE "debug|error_history|ErrorHistory|key log|tls_key" | head -30 | cut -c1-230
  echo "--- capture: $(tcpdump -r "$RUN/capture.pcap" 2>/dev/null | wc -l) packets"
  tcpdump -r "$RUN/capture.pcap" -nn 'udp port 15118' 2>/dev/null | head -4
  strings -n 12 "$RUN/capture.pcap" | grep -E "urn:(iso|din)" | sort | uniq -c
done
