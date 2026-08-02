#!/bin/sh
# Drives EVerest's SIL hardware simulation over MQTT, so a foreign EV arriving over TCP can complete a
# DC charge. Nothing in EVerest is patched; this only speaks their own external MQTT interface.
#
# Why it is needed. `EvseManager::cable_check()` closes the contactor and waits ~5 s for the
# board-support module to report it closed, then answers CableCheckRes = FAILED. In the SIL the
# contactor closes because the simulated car walks the CP line A→B→C. A V2G peer over TCP has no CP
# line, so without this every forward run ends at CableCheck
# (docs/interop-runs/2026-08-02-everest-iso2-dc-mqtt-auth/).
#
# Two steps, and both are needed:
#
#   at start          plug the simulated car in and let SLAC match, then hold. CP goes to 9 V, the
#                     station starts its 5 % PWM, EvseManager opens a transaction and their own
#                     DummyTokenProvider authorizes it — so with this script the MQTT token of
#                     mqtt-authorize.sh is not needed, because the plug event it was replacing now
#                     really happens.
#   at Start_CableCheck   move CP to 6 V (state C) so the contactor may close.
#
# Three things about their JsCarSimulator make the exact commands non-obvious:
#
#   * When a command list runs out, the module resets to defaults — which means *unplugged*. Every
#     sequence therefore ends in a long `sleep`, or the car pulls out from under the session.
#   * `execute_charging_session` is refused while a list is still running ("already running, cannot
#     start new one"), and it resets the simulation. `modify_charging_session` replaces the list
#     without resetting, which is what the second step needs. To start over, send `unplug` first.
#   * `cp C` on its own does nothing: the state machine rewrites cp_voltage from `mod.state` on every
#     tick. `draw_power_fixed 0,0` is the state that sets 6 V unconditionally and draws no current —
#     their comment calls it a break-the-rules mode for testing charging implementations, which is
#     exactly what this is. `iso_wait_pwr_ready` would be the polite route and cannot be used: it waits
#     on their own EV module, which we have deliberately pointed at `lo`.
#
# Usage — start it before the session and leave it running:
#
#   docker cp sil-car.sh mqtt:/tmp/
#   docker exec -d mqtt sh -c "/tmp/sil-car.sh > /tmp/sil-car.log 2>&1"
#
# Then wait for "Set PWM On" in their log before connecting the EVCC.

CONNECTOR=${CONNECTOR_ID:-1}
CHARGER=${CHARGER_MODULE:-iso15118_charger}
BROKER=${MQTT_HOST:-localhost}
CARSIM="everest_external/nodered/$CONNECTOR/carsim/cmd"

PLUG_IN='sleep 2;iso_wait_slac_matched;iso_wait_pwm_is_running;sleep 600'
STATE_C='draw_power_fixed 0,0;sleep 600'

# Clear whatever the last run left behind, then plug in.
mosquitto_pub -h "$BROKER" -t "$CARSIM/modify_charging_session" -m 'unplug'
sleep 4
echo "$(date -u +%H:%M:%S) -> plug in: $PLUG_IN"
mosquitto_pub -h "$BROKER" -t "$CARSIM/execute_charging_session" -m "$PLUG_IN"

mosquitto_sub -h "$BROKER" -t "everest/$CHARGER/charger/var" | while read -r line; do
  echo "$(date -u +%H:%M:%S) charger/var: $line"
  case "$line" in
    *Start_CableCheck*)
      echo "$(date -u +%H:%M:%S) -> CP to state C: $STATE_C"
      mosquitto_pub -h "$BROKER" -t "$CARSIM/modify_charging_session" -m "$STATE_C"
      ;;
  esac
done
