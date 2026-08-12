#!/bin/sh
# The Plug & Charge counterpart of mqtt-authorize.sh.
#
# Their EvseV2G verifies our signature and then publishes require_auth_pnc carrying the
# ProvidedIdToken it built from the contract certificate's eMAID. In the SIL nothing authorizes it,
# so the station polls Ongoing until auth_timeout_pnc and answers FAILED. This hands that token
# straight to the auth module, which is what an OCPP backend would do in a real deployment.
#
# **It does not get a session authorized**, and is checked in for what it documents rather than for
# what it achieves: their auth module answers NO_CONNECTOR_AVAILABLE even with the connector free and
# their own DummyTokenProviderManual in place. See docs/interop-runs/2026-08-03-everest-pnc/notes.md —
# the signature verifying is the result of that run; this is where it stops.
#
# **Superseded 2026-08-13, and the diagnosis above was wrong twice over.** Use
# contract-validator-arm.sh instead.
#
# NO_CONNECTOR_AVAILABLE is what a token *without* `connectors` gets: EvseV2G builds the PnC token
# without them (iso_server.cpp:1118-1125) and EvseManager adds them on the way through
# (EvseManager.cpp:1047), so forwarding the raw require_auth_pnc payload skips exactly the hop that
# makes it routable. And the hop was never missing — what was missing is a *connection in the config*,
# from EvseManager's own token_provider implementation to auth, which only the two OCPP PnC configs
# carry. With it, EVerest forwards its own token correctly and this script has nothing left to do.
# docs/interop-runs/2026-08-13-everest-contract-validator/notes.md.
#
# Topic shapes as in mqtt-authorize.sh, and for the same reason — carrying only one of them is how a
# script in this directory came to silently do nothing:
#
#   2023.10.0  (manager:main)                everest/<module_id>/<impl_id>/var
#   2025.10.0  (manager:2025.10.0-patches)   everest/modules/<module_id>/impl/<impl_id>/var
#
# An empty log means no message arrived on either — not that PnC did not happen.

CHARGER=${CHARGER_MODULE:-iso15118_charger}
PROVIDER=${TOKEN_PROVIDER_MODULE:-token_provider}
BROKER=${MQTT_HOST:-localhost}

CHARGER_TOPIC_FLAT="everest/$CHARGER/charger/var"                 # 2023.10.0
CHARGER_TOPIC_MODULES="everest/modules/$CHARGER/impl/charger/var" # 2025.10.0

echo "$(date -u +%H:%M:%S) watching $CHARGER_TOPIC_FLAT"
echo "$(date -u +%H:%M:%S)      and $CHARGER_TOPIC_MODULES"

# -v so each line is "<topic> <payload>": the topic is what tells us which shape this image speaks.
mosquitto_sub -h "$BROKER" -v \
              -t "$CHARGER_TOPIC_FLAT" \
              -t "$CHARGER_TOPIC_MODULES" | while read -r line; do

  topic=${line%% *}
  payload=${line#* }

  case "$payload" in
    *equire_[Aa]uth_[Pp]n[Cc]*)
      echo "$(date -u +%H:%M:%S) $topic: $(printf '%s' "$payload" | cut -c1-200)"

      case "$topic" in
        everest/modules/*) provider_topic="everest/modules/$PROVIDER/impl/main/var" ;;
        *)                 provider_topic="everest/$PROVIDER/main/var" ;;
      esac

      # Re-publish their own token under the provider's variable name; both spellings of the
      # variable are rewritten, since the two images differ there as well.
      TOKEN=$(printf '%s' "$payload" | sed -E 's/"name"[[:space:]]*:[[:space:]]*"[Rr]equire_[Aa]uth_[Pp]n[Cc]"/"name":"provided_token"/')

      mosquitto_pub -h "$BROKER" -t "$provider_topic" -m "$TOKEN"
      echo "$(date -u +%H:%M:%S) -> forwarded to $provider_topic"
      ;;
  esac
done
