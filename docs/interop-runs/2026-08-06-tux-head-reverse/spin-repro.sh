#!/bin/bash
# Minimal reproduction of the tux-evse binder spin: one peer connects, speaks one message, and closes.
# Run as root (netns + pkill). Everything sequenced in one script so nothing overlaps.
set -u
RUN=/home/ahzf/tux-evse/run
LOG=$RUN/responder-eof.log
SAP='\x01\xfe\x80\x01\x00\x00\x00\x24\x80\x00\xeb\xab\x93\x71\xd3\x4b\x9b\x79\xd1\x89\xa9\x89\x89\xc1\xd1\x91\xd1\x91\x81\x89\x99\xd2\x6b\x9b\x3a\x23\x2b\x30\x02\x00\x00\x04\x00\x40'

pkill -x afb-evse 2>/dev/null; pkill -x afb-evcc 2>/dev/null   # NB: --name renames the process
sleep 3
rm -f "$LOG"

ip netns exec tuxev bash "$RUN/run-responder.sh" "$RUN/audi-stock-autorun.json" 45 "$LOG" &
sleep 8

peer=$(grep -o 'tcp-wserver listen socket:\[[^]]*\]:[0-9]*' "$LOG" | tail -1 | sed 's/.*socket://')
echo "their responder is listening on $peer"
addr=$(echo "$peer" | sed 's/%[0-9]*\]/%evse-veth]/')
started=$(wc -l < "$LOG")

printf "$SAP" | timeout 4 socat - "TCP6:$addr" > "$RUN/sap-reply.bin"
echo "sent one SupportedAppProtocolReq; got $(wc -c < "$RUN/sap-reply.bin") byte(s) back; socket closed."

sleep 1;  at_close=$(wc -l < "$LOG")
sleep 10; after=$(wc -l < "$LOG")

echo "log lines: at-start=$started  at-close=$at_close  after-10s-idle=$after"
echo "written during 10 s with no peer connected: $((after - at_close)) lines, $(du -h "$LOG" | cut -f1) total"
echo "--- the line it repeats:"
tail -2 "$LOG" | cut -c1-150
echo "--- sockets to their port:"
ip netns exec tuxev ss -tn 2>/dev/null | tail -3
pkill -x afb-evse 2>/dev/null; pkill -x afb-evcc 2>/dev/null   # NB: --name renames the process
