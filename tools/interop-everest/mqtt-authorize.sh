#!/bin/sh
# Authorizes an EVerest session over MQTT, without touching their code or their config.
#
# Why this is needed. EVerest authorizes a session when a token arrives, and in the SIL configs the
# token comes from DummyTokenProvider, which is wired to EvseManager's *plug-in* events. An EV that
# arrives over TCP produces no plug-in event, so no token is ever published and the station answers
# AuthorizationRes with EVSEProcessing = Ongoing for ever — correctly, and for ever
# (docs/interop-runs/2026-08-02-everest-iso2-dc-notls/).
#
# What this does. It publishes the same ProvidedIdToken on the same topic their own provider uses, but
# triggered by the HLC instead of by hardware: EvseV2G sets Require_Auth_EIM the moment the EV has
# selected EIM and sent AuthorizationReq. Their Auth module cannot tell the difference — it validates
# the token, calls evse_manager.authorize_response, and the next AuthorizationRes says Finished.
#
# It also logs every V2G message their charger publishes on that topic: the station's own record of the
# session, by message name. Read the caveat in the run notes before trusting the bytes — the responses
# they publish carry the *request's* V2GTP length, so they are truncated or padded. The names are good.
#
# Run it against the MQTT broker EVerest is using, before starting the session:
#
#   docker cp mqtt-authorize.sh mqtt:/tmp/
#   docker exec -d mqtt sh -c "/tmp/mqtt-authorize.sh > /tmp/auth.log 2>&1"
#
# Topics are everest/<module_id>/<impl_id>/var with payload {"data": <value>, "name": "<var>"}. The
# module ids are the ones in the config file — iso15118_charger and token_provider in ours.

CHARGER=${CHARGER_MODULE:-iso15118_charger}
PROVIDER=${TOKEN_PROVIDER_MODULE:-token_provider}
TOKEN_ID=${TOKEN_ID:-TOKEN1}
BROKER=${MQTT_HOST:-localhost}

TOKEN="{\"data\":{\"id_token\":\"$TOKEN_ID\",\"authorization_type\":\"RFID\",\"id_token_type\":\"ISO14443\",\"prevalidated\":false,\"connectors\":[1]},\"name\":\"provided_token\"}"

mosquitto_sub -h "$BROKER" -t "everest/$CHARGER/charger/var" | while read -r line; do
  echo "$(date -u +%H:%M:%S) charger/var: $line"
  case "$line" in
    *Require_Auth_EIM*)
      echo "$(date -u +%H:%M:%S) -> $TOKEN_ID to everest/$PROVIDER/main/var"
      mosquitto_pub -h "$BROKER" -t "everest/$PROVIDER/main/var" -m "$TOKEN"
      ;;
  esac
done
