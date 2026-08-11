#!/usr/bin/env bash
# Bridges EVerest's certificate-provisioning MQTT interface to a directory the .NET side watches.
#
# EvseV2G does not answer a CertificateInstallationReq itself: it publishes the EV's EXI on
#   everest/modules/<id>/impl/extensions/var/iso15118_certificate_request
# and waits V2G_SECC_MSG_CERTINSTALL_TIME (4500 ms, iso_server.cpp:30) for a response delivered to
#   everest/modules/<id>/impl/extensions/cmd/set_get_certificate_response
# Their SIL ships nobody to do that, which is why a stock run dies on the timeout.
#
# This script is the missing backend's *transport* only. The contract is issued by
# ISO15118ConformanceTests.Simulation/Interop/Iso2MoBackend.cs, which watches the same directory —
# MQTT is spoken here, with their own mosquitto tools, because this project has no MQTT client and
# does not need one for a single request-response.
#
# Run it before plugging the car in:
#   bash mo-backend-bridge.sh /mnt/c/…/scratchpad/mo-backend
#
# Bash, not dash: the JSON handling below uses process substitution.

set -u

DIR="${1:?usage: mo-backend-bridge.sh <exchange-directory> [module-id]}"
MODULE="${2:-iso15118_charger}"
DIST="${DIST:-$HOME/everest/dist}"

VAR_TOPIC="everest/modules/${MODULE}/impl/extensions/var/iso15118_certificate_request"
CMD_TOPIC="everest/modules/${MODULE}/impl/extensions/cmd/set_get_certificate_response"

mkdir -p "$DIR"
rm -f "$DIR/request.b64" "$DIR/response.b64"

echo "bridge: subscribing to $VAR_TOPIC"
echo "bridge: exchange directory $DIR"

# One request is all a session sends. -C 1 exits after it, which also bounds this script.
PAYLOAD="$("$DIST/bin/mosquitto_sub" -t "$VAR_TOPIC" -C 1)"

ACTION="$(printf '%s' "$PAYLOAD" | python3 -c 'import sys,json; print(json.load(sys.stdin)["data"]["data"]["certificate_action"])')"
EXI="$(printf '%s' "$PAYLOAD" | python3 -c 'import sys,json; print(json.load(sys.stdin)["data"]["data"]["exi_request"])')"

echo "bridge: $ACTION, $(printf '%s' "$EXI" | wc -c) base64 chars in"

# Write-then-move, so the watcher never reads half a file inside a 4,5 s window.
printf '%s' "$EXI" > "$DIR/request.b64.tmp"
mv "$DIR/request.b64.tmp" "$DIR/request.b64"

# Wait for the issuer. Bounded well inside their timeout: if the answer is not ready in ~3 s it will
# not arrive in time anyway, and a bridge that publishes late is worse than one that publishes nothing
# — the station has already failed the session and the next message would land in a dead context.
for _ in $(seq 1 60); do
    [ -f "$DIR/response.b64" ] && break
    sleep 0.05
done

if [ ! -f "$DIR/response.b64" ]; then
    echo "bridge: no response.b64 within 3 s — not publishing (their station has timed out by now)" >&2
    exit 1
fi

RESPONSE="$(cat "$DIR/response.b64")"
echo "bridge: publishing $(printf '%s' "$RESPONSE" | wc -c) base64 chars back"

# The command envelope EVerest's MQTT layer expects: args by name, plus an id it echoes on the
# response topic. The member names are types/iso15118.yaml's ResponseExiStreamStatus — `status`,
# `certificate_action` (required, and it has to match the request's) and `exi_response`. Read, not
# guessed: an argument name that is close but wrong is silently dropped and looks like a timeout.
"$DIST/bin/mosquitto_pub" -t "$CMD_TOPIC" -m "$(python3 - "$RESPONSE" "$ACTION" <<'PY'
import json, sys
print(json.dumps({"data": {"args": {"certificate_response":
                                    {"status": "Accepted",
                                     "certificate_action": sys.argv[2],
                                     "exi_response": sys.argv[1]}},
                           "id": "mo-backend-0001",
                           "origin": "mo_backend"},
                  "msg_type": "Cmd"}))
PY
)"

# The envelope above is copied from a command their own modules publish, captured with
#   mosquitto_sub -t "everest/modules/+/impl/+/cmd/+" -v
# rather than reconstructed from the docs. `msg_type` and `origin` are both required and both were
# missing on the first attempt: the message is then dropped in silence and the station runs into its
# timeout, which is indistinguishable from publishing nothing at all.

echo "bridge: done"
