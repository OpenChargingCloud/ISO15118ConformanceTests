#!/usr/bin/env bash
# Sends a JsCarSimulator command list the moment the station logs a chosen line.
#
# ── Why this exists ───────────────────────────────────────────────────────────────────────────────
#
# Their `-20` AC `PowerDelivery(Start)` asks the board-support layer to close the AC contactor, arms a
# 3 s timeout and then waits for a `ClosedContactor` *event*:
#
#   power_delivery.cpp:118-126   if (m_ctx.session.is_ac_charger() and ac_connector_closed == false …)
#                                    m_ctx.feedback.signal(Signal::AC_CLOSE_CONTACTOR);
#                                    m_ctx.start_timeout(TimeoutType::CONTACTOR, 3000);
#
# `is_ac_charger()` is why `-20` DC never meets this wait at all — DC falls straight through and
# answers. `-2` does not meet it either, for a different reason: `EvseV2G` stores the value in
# `v2g_ctx->contactor_is_closed` and waits in a condition-variable loop that **re-tests** it, so a
# `true` that arrived earlier is remembered. `libiso15118` remembers nothing and needs the event
# **inside** the window. Latch against edge — that is the whole difference between the two protocols
# here, not anything about AC.
#
# In the topology this harness runs, `sil-car.sh CP_AT_PLUGIN=1` raises the CP line at plug-in, so
# `EVSE IEC Event PowerOn` — the thing that makes `EvseManager` call `ac_contactor_closed(true)` —
# fires long before `PowerDelivery` is entered, and the event lands in a state that does not read it.
# Measured 2026-08-09 (`their-charger.cphold.log`):
#
#   14:13:08.234  EVSE IEC Event PowerOn                    <- the confirmation is produced …
#   14:13:09.397  iso15118_charge :: Waiting for contactor is closed   <- … 1,16 s before it is wanted
#   +3.032 s      FAILED_ContactorError
#
# So: hold the car at state B, and raise CP only once the window is open. That is also the
# ISO-correct ordering — the vehicle draws power *after* `PowerDelivery`, not before. Their SIL
# inverts it because `ac_hlc_use_5percent: false` lets the IEC layer run ahead of the HLC session.
#
#   CP_AT_PLUGIN=0 bash sil-car.sh &                       # plug in, hold at state B
#   bash carsim-on-trigger.sh --watch ~/everest/run/ac20.charger.log &
#
# The margin is the measurement: on 2026-08-09 the station took 2,51 s from CP-to-C to `PowerOn`
# (PWM on 14:13:05.719 → `PrepareCharging->Charging` 14:13:07.965 → `PowerOn` 14:13:08.234) against a
# 3,000 s timeout. It fits, with about half a second to spare, and that is worth knowing either way.
#
# ── The command list ──────────────────────────────────────────────────────────────────────────────
#
#   bash carsim-on-trigger.sh --watch <log>
#   bash carsim-on-trigger.sh --watch <log> --commands 'draw_power_regulated 16,3;sleep 600'
#   bash carsim-on-trigger.sh --now --commands 'unplug'
#
# The default is `draw_power_fixed 0,0;sleep 600` — their break-the-rules mode that sets 6 V
# unconditionally and draws no current, which is the same lever `sil-car.sh` uses to get the DC cable
# check past its contactor. `cp C` on its own does nothing: the simulator rewrites `cp_voltage` from
# `mod.state` on every tick.
#
# The trailing `sleep` is not decoration. When a JsCarSimulator command list runs out the module
# resets to defaults, which means **unplugged**, and the car pulls out from under the session.
#
# `modify_charging_session`, not `execute_charging_session`: the latter is refused while a list is
# still running ("already running, cannot start new one") and resets the simulation. Same reason
# `sil-car.sh` uses it for its own second step.

set -euo pipefail

WATCH=""
NOW=0
COMMANDS="${CARSIM_COMMANDS:-draw_power_fixed 0,0;sleep 600}"
CONNECTOR="${CONNECTOR_ID:-1}"
BROKER="${MQTT_HOST:-localhost}"
PUB="${MOSQUITTO_PUB:-mosquitto_pub}"
TRIGGER="${TRIGGER_LINE:-Waiting for contactor is closed}"
TIMEOUT="${WATCH_TIMEOUT:-120}"
DELAY="${FIRE_DELAY:-0}"

# Every value-taking option checks that its value is there before reading it: under `set -u` a bare
# `--watch` at the end makes `$2` an unbound variable and the script dies on that rather than on the
# usage message below. Same guard, same reason, as contactor-report.sh next door.
need_value() {
    [ $# -ge 2 ] || { echo "$1 needs a value" >&2; exit 2; }
}

while [ $# -gt 0 ]; do
    case "$1" in
        --watch)    need_value "$@"; WATCH="$2";    shift 2 ;;
        --commands) need_value "$@"; COMMANDS="$2"; shift 2 ;;
        --delay)    need_value "$@"; DELAY="$2";    shift 2 ;;
        --now)      NOW=1;                          shift   ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

if [ "$NOW" -eq 0 ] && [ -z "$WATCH" ]; then
    echo "give --now or --watch <logfile>" >&2
    exit 2
fi

topic="everest_external/nodered/${CONNECTOR}/carsim/cmd/modify_charging_session"

fire() {
    [ "$DELAY" != "0" ] && sleep "$DELAY"
    echo "$(date -u +%H:%M:%S.%3N) -> $COMMANDS"
    "$PUB" -h "$BROKER" -t "$topic" -m "$COMMANDS"
    echo "$(date -u +%H:%M:%S.%3N) published"
}

if [ "$NOW" -eq 1 ]; then
    fire
    exit 0
fi

echo "$(date -u +%H:%M:%S.%3N) watching $WATCH for: $TRIGGER"

# Polled rather than `tail -F | grep -m1`: when grep exits on its match tail takes SIGPIPE, and under
# `set -o pipefail` that turns a successful match into a failed pipeline. contactor-report.sh's first
# version did exactly that and reported "trigger never appeared" 20 ms *after* the line appeared,
# publishing nothing — a failure that looks like a clean negative result. Do not reintroduce it.
#
# 50 ms is a twentieth of the 3 s window and the file is small.
from=$(( $(wc -l < "$WATCH" 2>/dev/null || echo 0) + 1 ))
deadline=$(( SECONDS + TIMEOUT ))

# -F and --: the trigger is a literal log line, and TRIGGER_LINE exists to be overridden. Without -F
# a bracketed EVerest level like "[ERRO] Shutdown loop()" is a character class and matches nothing.
while [ "$SECONDS" -lt "$deadline" ]; do
    if tail -n "+$from" "$WATCH" 2>/dev/null | grep -qF -- "$TRIGGER"; then
        fire
        exit 0
    fi
    sleep 0.05
done

echo "$(date -u +%H:%M:%S.%3N) trigger never appeared within ${TIMEOUT}s — nothing published" >&2
exit 1
