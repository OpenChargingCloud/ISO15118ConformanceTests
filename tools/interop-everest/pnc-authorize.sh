#!/bin/sh
# Their EvseV2G verifies our signature and then publishes require_auth_pnc carrying the
# ProvidedIdToken it built from the contract certificate's eMAID. In the SIL nothing authorizes it,
# so the station polls Ongoing until auth_timeout_pnc and answers FAILED. This hands that token
# straight to the auth module, which is what an OCPP backend would do in a real deployment.
#
# Topic shape is the 2025.10 one: everest/modules/<module_id>/impl/<impl_id>/var. The 2023 demo
# image used everest/<module_id>/<impl_id>/var, which is why mqtt-authorize.sh looks different.
BASE=everest/modules
CHARGER=${CHARGER_MODULE:-iso15118_charger}
PROVIDER=${TOKEN_PROVIDER_MODULE:-token_provider}
BROKER=${MQTT_HOST:-localhost}

mosquitto_sub -h "$BROKER" -t "$BASE/$CHARGER/impl/charger/var" | while read -r line; do
  case "$line" in
    *equire_auth_pnc*)
      echo "$(date -u +%H:%M:%S) require_auth_pnc: $(printf '%s' "$line" | cut -c1-200)"
      TOKEN=$(printf '%s' "$line" | sed 's/"name":"require_auth_pnc"/"name":"provided_token"/')
      mosquitto_pub -h "$BROKER" -t "$BASE/$PROVIDER/impl/main/var" -m "$TOKEN"
      echo "$(date -u +%H:%M:%S) -> forwarded to $PROVIDER"
      ;;
  esac
done
