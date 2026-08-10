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
#   2026.02.1, native:  sh mqtt-authorize.sh > /tmp/auth.log 2>&1 &
#   older, in Docker:   docker cp mqtt-authorize.sh mqtt:/tmp/
#                       docker exec -d mqtt sh -c "/tmp/mqtt-authorize.sh > /tmp/auth.log 2>&1"
#
# ── Three wire shapes, because both the topic and the payload have moved ─────────────────────────
#
#   2023.10.0  (manager:main)                everest/<module_id>/<impl_id>/var
#   2025.10.0  (manager:2025.10.0-patches)   everest/modules/<module_id>/impl/<impl_id>/var
#   2026.02.1  (source build)                everest/modules/<module_id>/impl/<impl_id>/var/<name>
#
# All three are subscribed, and the token is published back in whichever shape the trigger arrived in,
# so the same script drives any of them. This is worth the extra lines twice over: carrying only the
# oldest form made the script **silently do nothing** against 2025.10 — it matched no message, wrote an
# empty log, and looked exactly like a script that was working and simply had nothing to say, while the
# plug-in flow of sil-car.sh did the authorizing all along
# (docs/interop-runs/2026-08-03-everest-pnc/notes.md). The 2026.02.1 shape would have done it again:
# MQTT filters are level-exact without a wildcard, so a filter ending in .../var matches nothing once
# the variable name is a level of its own.
#
# So: an empty log means no message ever arrived on any of the three topics. Check the module id
# against the config file, and check the shape against the version — do not assume it is working.
#
# What changed in the payload, from their framework (lib/everest/framework/lib/everest.cpp:462-500 for
# the publish, message_handler.cpp:289-408 for the receive):
#
#   2023.10.0 / 2025.10.0   {"data": <value>, "name": "<var>"}
#   2026.02.1               {"msg_type": "Var", "data": {"data": <value>}}
#
# Two levels of "data" in the newer one, and no name — it is in the topic. A payload without msg_type
# is routed to *external* MQTT handlers rather than to variable subscribers, so publishing the old
# shape on the new topic is not an error either: it is another silent no-op.
#
# The variable is spelled Require_Auth_EIM in the oldest image and require_auth_eim since, hence the
# case-tolerant match on the payload; on 2026.02.1 the name is matched on the topic instead, because
# the payload of a "null"-typed variable carries nothing to match.
#
# ProvidedIdToken moved too (types/authorization.yaml): id_token is now an IdToken *object*
# {"value": …, "type": …} rather than a bare string beside an id_token_type. The type sets
# additionalProperties: false and the framework validates on receive, so the old shape on 2026.02.1 is
# dropped with "Ignoring incoming var ... because not matching manifest schema". The new token below is
# what their own DummyTokenProvider publishes (modules/Testing/DummyTokenProvider/main/
# auth_token_providerImpl.cpp:16-24), minus parent_id_token, which Auth does not need here.
#
# Verified against a running 2026.02.1 station on 2026-08-10:
# docs/interop-runs/2026-08-10-everest-mqtt-authorize-2026021/notes.md.

CHARGER=${CHARGER_MODULE:-iso15118_charger}
PROVIDER=${TOKEN_PROVIDER_MODULE:-token_provider}
TOKEN_ID=${TOKEN_ID:-TOKEN1}
CONNECTOR=${CONNECTOR_ID:-1}
BROKER=${MQTT_HOST:-localhost}

CHARGER_TOPIC_FLAT="everest/$CHARGER/charger/var"                 # 2023.10.0
CHARGER_TOPIC_MODULES="everest/modules/$CHARGER/impl/charger/var" # 2025.10.0
CHARGER_TOPIC_NAMED="$CHARGER_TOPIC_MODULES/require_auth_eim"     # 2026.02.1

TOKEN_OLD="{\"data\":{\"id_token\":\"$TOKEN_ID\",\"authorization_type\":\"RFID\",\"id_token_type\":\"ISO14443\",\"prevalidated\":false,\"connectors\":[$CONNECTOR]},\"name\":\"provided_token\"}"
TOKEN_NEW="{\"msg_type\":\"Var\",\"data\":{\"data\":{\"id_token\":{\"value\":\"$TOKEN_ID\",\"type\":\"ISO14443\"},\"authorization_type\":\"RFID\",\"prevalidated\":false,\"connectors\":[$CONNECTOR]}}}"

echo "$(date -u +%H:%M:%S) watching $CHARGER_TOPIC_FLAT"
echo "$(date -u +%H:%M:%S)      and $CHARGER_TOPIC_MODULES"
echo "$(date -u +%H:%M:%S)      and $CHARGER_TOPIC_NAMED"

# -v so each line is "<topic> <payload>": the topic is what tells us which shape this station speaks.
mosquitto_sub -h "$BROKER" -v \
              -t "$CHARGER_TOPIC_FLAT" \
              -t "$CHARGER_TOPIC_MODULES" \
              -t "$CHARGER_TOPIC_NAMED" | while read -r line; do

  topic=${line%% *}
  payload=${line#* }

  echo "$(date -u +%H:%M:%S) $topic: $payload"

  # 2026.02.1 puts the name in the topic; before that it was in the payload.
  case "$topic" in
    */var/require_auth_eim)
      provider_topic="everest/modules/$PROVIDER/impl/main/var/provided_token"
      token="$TOKEN_NEW"
      ;;
    *)
      case "$payload" in
        *equire_[Aa]uth_[Ee][Ii][Mm]*)
          token="$TOKEN_OLD"
          case "$topic" in
            everest/modules/*) provider_topic="everest/modules/$PROVIDER/impl/main/var" ;;
            *)                 provider_topic="everest/$PROVIDER/main/var" ;;
          esac
          ;;
        *) continue ;;
      esac
      ;;
  esac

  echo "$(date -u +%H:%M:%S) -> $TOKEN_ID to $provider_topic"
  mosquitto_pub -h "$BROKER" -t "$provider_topic" -m "$token"
done
