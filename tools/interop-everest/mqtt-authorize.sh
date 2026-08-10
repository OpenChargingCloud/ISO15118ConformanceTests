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
# triggered by the HLC instead of by hardware: EvseV2G sets require_auth_eim the moment the EV has
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
# ── The topic shape changed between the images we use ────────────────────────────────────────────
#
#   2023.10.0  (manager:main)                everest/<module_id>/<impl_id>/var
#   2025.10.0  (manager:2025.10.0-patches)   everest/modules/<module_id>/impl/<impl_id>/var
#
# Both are subscribed, and the token is published back on whichever shape the trigger arrived on, so
# the same script drives either image. This is worth the six lines: carrying only the old form made
# the script **silently do nothing** against 2025.10 — it matched no message, wrote an empty log, and
# looked exactly like a script that was working and simply had nothing to say. The plug-in flow of
# sil-car.sh had been doing the authorizing all along
# (docs/interop-runs/2026-08-03-everest-pnc/notes.md).
#
# So: an empty log means no message ever arrived on either topic. Check the module id against the
# config file, and check the shape against the image — do not assume it is working.
#
#   2026.02.1  NOT SUPPORTED BY THIS SCRIPT. The variable name became a further topic level,
#              everest/modules/<module_id>/impl/<impl_id>/var/<name>, which neither filter below
#              matches (MQTT filters are level-exact without a wildcard), and the payload gained a
#              {"msg_type":"Var","data":{...}} envelope. It would write an empty log and look like it
#              was working. Use sil-car.sh there — its plug-in makes their DummyTokenProvider
#              authorize — and see docs/interop-runs/2026-08-10-everest-session-log-lengths/notes.md.
#
# The payload is {"data": <value>, "name": "<var>"} either way. The variable is spelled
# Require_Auth_EIM in the older image and require_auth_eim in the newer, hence the case-tolerant match.

CHARGER=${CHARGER_MODULE:-iso15118_charger}
PROVIDER=${TOKEN_PROVIDER_MODULE:-token_provider}
TOKEN_ID=${TOKEN_ID:-TOKEN1}
BROKER=${MQTT_HOST:-localhost}

CHARGER_TOPIC_FLAT="everest/$CHARGER/charger/var"                 # 2023.10.0
CHARGER_TOPIC_MODULES="everest/modules/$CHARGER/impl/charger/var" # 2025.10.0

TOKEN="{\"data\":{\"id_token\":\"$TOKEN_ID\",\"authorization_type\":\"RFID\",\"id_token_type\":\"ISO14443\",\"prevalidated\":false,\"connectors\":[1]},\"name\":\"provided_token\"}"

echo "$(date -u +%H:%M:%S) watching $CHARGER_TOPIC_FLAT"
echo "$(date -u +%H:%M:%S)      and $CHARGER_TOPIC_MODULES"

# -v so each line is "<topic> <payload>": the topic is what tells us which shape this image speaks.
mosquitto_sub -h "$BROKER" -v \
              -t "$CHARGER_TOPIC_FLAT" \
              -t "$CHARGER_TOPIC_MODULES" | while read -r line; do

  topic=${line%% *}
  payload=${line#* }

  echo "$(date -u +%H:%M:%S) $topic: $payload"

  case "$payload" in
    *equire_[Aa]uth_[Ee][Ii][Mm]*)
      case "$topic" in
        everest/modules/*) provider_topic="everest/modules/$PROVIDER/impl/main/var" ;;
        *)                 provider_topic="everest/$PROVIDER/main/var" ;;
      esac
      echo "$(date -u +%H:%M:%S) -> $TOKEN_ID to $provider_topic"
      mosquitto_pub -h "$BROKER" -t "$provider_topic" -m "$TOKEN"
      ;;
  esac
done
