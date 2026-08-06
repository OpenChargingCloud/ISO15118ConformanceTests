#!/bin/bash
# The stdin experiment against eVDriveFlow's EV.
#
#   edf-run.sh eof     -> start their EV with stdin at EOF   (what `docker exec -d` gives; the 2026-08-01 rig)
#   edf-run.sh open    -> start their EV with stdin held open (a pipe nobody writes to)
#
# Everything else is identical between the two runs. Our SECC is started by the caller.
set -u
MODE="${1:?eof|open}"
LOG=/home/ahzf/edf/ev-$MODE.log

docker rm -f edf-ev >/dev/null 2>&1
docker run -d --name edf-ev --network edfnet edf-ev-unpatched sleep infinity >/dev/null

# The relay: their EV connects to the address the SDP shim hands it; socat carries it to our SECC
# on the WSL host. Same shim as the 2026-08-01 run, from tools/interop-josev.
docker exec -d edf-ev socat TCP6-LISTEN:15118,fork,reuseaddr TCP:172.30.0.1:55000
docker exec -d edf-ev python3 /usr/local/bin/sdp-responder.py eth0 15118 notls
sleep 2

case "$MODE" in
  eof)
    # -d detaches; stdin is /dev/null, so sys.stdin.readline() returns '' immediately.
    docker exec -d edf-ev sh -c "cd /app/evcc && python3 start_ev.py > /tmp/ev.log 2>&1"
    ;;
  open)
    # A pipe that stays open and is never written to: readline() blocks, as it does at a terminal.
    docker exec -d edf-ev sh -c "mkfifo -m 600 /tmp/kb 2>/dev/null; (sleep 600 > /tmp/kb &) ; cd /app/evcc && python3 start_ev.py < /tmp/kb > /tmp/ev.log 2>&1"
    ;;
esac

sleep 25
docker exec edf-ev sh -c "cat /tmp/ev.log" > "$LOG" 2>&1
echo "=== their EV ($MODE), the messages it sent:"
grep -E "Sent |Received |stop has been requested|Traceback|Error" "$LOG" | head -30
echo "=== log: $LOG"
