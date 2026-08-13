#!/usr/bin/env bash
# Bring up an EVerest SIL station whose contract decision is ours to make.
# Run notes: docs/interop-runs/2026-08-13-everest-contract-validator/
#
#   contract-validator-arm.sh <config.yaml> [policy.json]
#
# Starts the manager with the `token_validator` module withheld, puts
# [`contract-validator.py`](contract-validator.py) in its place over MQTT, and leaves both running.
# Drive the session yourself afterwards — this arm owns the station, not the car.
#
# Their SIL answers every authorization request from `DummyTokenValidator`'s config file, so a run
# against a stock station cannot measure what their station does with a *verdict*. With this in place
# it can: the answer reaches the ISO 15118-2 wire, including `FAILED_CertificateRevoked`, which is
# unreachable otherwise because the dummy never sets `certificate_status`.
#
# Bash, not dash: `[[` and arrays below.

set -u

CONFIG="${1:?usage: contract-validator-arm.sh <config.yaml> [policy.json]}"
POLICY="${2:-}"
DIST="${DIST:-$HOME/everest/dist}"
RUN="${RUN_DIR:-$HOME/everest}"
HERE="$(cd "$(dirname "$0")" && pwd)"

[[ -f "$CONFIG" ]] || { echo "no such config: $CONFIG" >&2; exit 1; }

# --- the gate that costs a whole run when it is missing --------------------------------------------
#
# EvseManager republishes the PnC token through its own `token_provider` implementation, and only the
# two OCPP PnC configs connect that to `auth`. Without the connection the token is published to a
# variable nobody subscribed to: PaymentDetailsRes is OK, the signature verifies, and then the session
# polls AuthorizationReq until auth_timeout_pnc and answers FAILED — with no token anywhere and nothing
# in any log to say why. Measured on 2026-08-13; it is what a PnC run against their plain SIL has
# always been doing.
#
# It gates PnC only. An EIM token reaches `auth` from `DummyTokenProvider` without this hop, and an
# EIM-only measurement is a real use of this arm — `Evse15118D20` offers no PnC at all, so every -20
# run is one. `EIM_ONLY=1` says you meant it.
if [[ "${EIM_ONLY:-0}" != "1" ]] && ! grep -q 'implementation_id: token_provider' "$CONFIG"; then
    cat >&2 <<EOF
$CONFIG does not connect EvseManager's token_provider to auth.

A Plug & Charge token will be published and dropped, and the session will run to auth_timeout_pnc and
answer FAILED without this validator ever being called. Add to the auth module's connections:

      token_provider:
      - module_id: token_provider
        implementation_id: main
      - module_id: evse_manager
        implementation_id: token_provider

(EIM tokens reach auth without it; PnC ones do not. Set EIM_ONLY=1 to proceed anyway --
every -20 run is EIM-only, since Evse15118D20 offers no PnC.)
EOF
    exit 2
fi

STATION_LOG="$RUN/contract-validator-station.log"
VALIDATOR_LOG="$RUN/contract-validator.log"
TOKENS="$RUN/contract-validator-tokens.jsonl"

echo "### station  $CONFIG"
echo "### logs     $STATION_LOG"
echo "###          $VALIDATOR_LOG"
echo "### tokens   $TOKENS"

# `--standalone <id>` makes the manager not spawn that child and wait for it. The id is the config key,
# not the module type: the declared type stays DummyTokenValidator and we answer on its topics, which
# is why nothing has to be added to the config and no manifest has to be written.
( cd "$DIST" && setsid ./bin/manager --config "$CONFIG" --standalone token_validator \
    > "$STATION_LOG" 2>&1 < /dev/null & )

for _ in $(seq 1 60); do
    grep -q "waiting for standalone modules" "$STATION_LOG" 2>/dev/null && break
    sleep 1
done
if ! grep -q "waiting for standalone modules" "$STATION_LOG" 2>/dev/null; then
    echo "the manager never reached 'waiting for standalone modules' -- see $STATION_LOG" >&2
    exit 3
fi
echo "### manager is holding for the standalone module"

ARGS=(--config "$CONFIG" --log "$TOKENS")
[[ -n "$POLICY" ]] && ARGS+=(--policy "$POLICY")

setsid python3 "$HERE/contract-validator.py" "${ARGS[@]}" > "$VALIDATOR_LOG" 2>&1 < /dev/null &

for _ in $(seq 1 60); do
    grep -q "Ready to start charging" "$STATION_LOG" 2>/dev/null && break
    sleep 1
done

echo "--- station ---"
sed 's/\x1b\[[0-9;]*m//g' "$STATION_LOG" | grep -E "standalone module|server on|Ready to start charging" | tail -5
echo "--- validator ---"
sed 's/\x1b\[[0-9;]*m//g' "$VALIDATOR_LOG" | grep -E "registered as|answering" | tail -3

cat <<EOF

Drive a session now. Every validate_token call is appended to $TOKENS whole.
${POLICY:+The verdict is re-read from $POLICY on every call, so it can be changed between sessions.}

Stop both, and mind that a pattern matching the command that carries it kills the shell too --
kill the validator by pid and the station by its prefix:

  kill \$(pgrep -f contract-validator.py | head -1)
  pkill -f "prefix $DIST"
EOF
