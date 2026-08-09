#!/usr/bin/env bash
# Reports an AC contactor state to Evse15118D20, over EVerest's own MQTT command interface.
#
# Why this exists. `ac_contactor_closed(bool)` is a command on their ISO15118_charger interface, and in
# a SIL it is called by EvseManager from Control-Pilot events. A foreign EV arriving over TCP produces
# no CP events, so the -20 AC session stops at PowerDelivery(Start) with FAILED_ContactorError from the
# 3 s timeout — the wall in docs/interop-runs/2026-08-03-everest-ac/. This speaks the same command
# their own EvseManager speaks, on the same topic, so the state machine cannot tell the difference.
#
# Nothing in EVerest is patched or reconfigured. Same principle as mqtt-authorize.sh next door.
#
#   bash contactor-report.sh --status false --watch ~/everest/run/ac20.charger.log
#   bash contactor-report.sh --status true  --now
#
# --watch fires the moment the station logs "Waiting for contactor is closed", which is the line that
# opens the 3 s window. --now publishes immediately.
#
# ── The wire format, from their framework rather than from guesswork ──────────────────────────────
#
#   topic    everest/modules/<module>/impl/<impl>/cmd/<cmd>
#            lib/everest/framework/lib/everest.cpp:877 with config.cpp:445-448
#   payload  {"msg_type":"Cmd","data":{"id":<id>,"args":{...},"origin":<who>}}
#            everest.cpp:408 wrapped by types.cpp:177-179
#
# The handler validates `args` against the interface schema and publishes its result to
# <topic>/response/<origin>, so `origin` has to be present but can be anything.

set -euo pipefail

STATUS=""
WATCH=""
NOW=0
MODULE="${CHARGER_MODULE:-iso15118_charger}"
IMPL="${CHARGER_IMPL:-charger}"
BROKER="${MQTT_HOST:-localhost}"
PREFIX="${EVEREST_PREFIX:-everest/}"
PUB="${MOSQUITTO_PUB:-mosquitto_pub}"
TRIGGER="${TRIGGER_LINE:-Waiting for contactor is closed}"
TIMEOUT="${WATCH_TIMEOUT:-120}"

while [ $# -gt 0 ]; do
    case "$1" in
        --status) STATUS="$2"; shift 2 ;;
        --watch)  WATCH="$2";  shift 2 ;;
        --now)    NOW=1;       shift ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

case "$STATUS" in
    true|false) ;;
    *) echo "--status must be true or false (got '${STATUS}')" >&2; exit 2 ;;
esac

if [ "$NOW" -eq 0 ] && [ -z "$WATCH" ]; then
    echo "give --now or --watch <logfile>" >&2
    exit 2
fi

topic="${PREFIX}modules/${MODULE}/impl/${IMPL}/cmd/ac_contactor_closed"
payload="{\"msg_type\":\"Cmd\",\"data\":{\"id\":\"contactor-report-$$\",\"args\":{\"status\":${STATUS}},\"origin\":\"contactor-report\"}}"

fire() {
    echo "$(date -u +%H:%M:%S.%3N) -> ac_contactor_closed(${STATUS})"
    echo "$(date -u +%H:%M:%S.%3N)    $topic"
    "$PUB" -h "$BROKER" -t "$topic" -m "$payload"
    echo "$(date -u +%H:%M:%S.%3N) published"
}

if [ "$NOW" -eq 1 ]; then
    fire
    exit 0
fi

echo "$(date -u +%H:%M:%S.%3N) watching $WATCH for: $TRIGGER"

# Polled rather than `tail -F | grep -m1`: when grep exits on its match, tail takes SIGPIPE, and under
# `set -o pipefail` that turns a successful match into a failed pipeline. The first version of this
# script did exactly that and reported "trigger never appeared" 20 ms *after* the line appeared.
#
# 50 ms is comfortably inside the 3 s window and the file is small.
from=$(( $(wc -l < "$WATCH" 2>/dev/null || echo 0) + 1 ))
deadline=$(( SECONDS + TIMEOUT ))

while [ "$SECONDS" -lt "$deadline" ]; do
    if tail -n "+$from" "$WATCH" 2>/dev/null | grep -q "$TRIGGER"; then
        fire
        exit 0
    fi
    sleep 0.05
done

echo "$(date -u +%H:%M:%S.%3N) trigger never appeared within ${TIMEOUT}s — nothing published" >&2
exit 1
