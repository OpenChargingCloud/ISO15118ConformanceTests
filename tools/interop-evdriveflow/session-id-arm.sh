#!/usr/bin/env bash
#
# `[V2G20-460]` against eVDriveFlow's SECC — three arms, one variable.
#
# The rule is one sentence: any request except SessionSetupReq whose SessionID is not the stored one
# shall be answered FAILED_UnknownSession. `evdriveflow-session-id.md` says their fifteen
# `secc/states/process_*_request.py` handlers never read the incoming header at all; this is that claim
# put on the wire.
#
#   SECC='[fd00:edf::2]:49152' OUT=/tmp/edf460 bash tools/interop-evdriveflow/session-id-arm.sh [repo]
#
# SERVICE_IDS=2,6 sends a `SupportedServiceIDs` filter, which is what gets past their own
# `process_service_discovery_request.py` crash (docs/reports/evdriveflow-service-discovery-filter.md) and
# so decides how many of their fifteen handlers the wrong id reaches. Without it the session ends at the
# fifth message and three handlers are measured; with it, the session goes as far as their station goes.
#
# Read the *arms against each other*, not any one of them. A station that ignores the field answers a
# foreign id exactly as it answers the right one — so "the session completed" is the finding only when
# the control completed the same way and the frame log proves the wrong bytes went out. The reference
# answer is EVerest's `Evse15118D20`, measured on 2026-08-11 refusing the same zero id:
# docs/interop-runs/2026-08-11-iso20-session-id-probe/.
#
# Their SECC, per docs/interop-runs/2026-08-15-edf-session-id-entropy/notes.md:
#   docker run -d --name edf-secc --network edfnet edf-ev-unpatched \
#       sh -c "cd /app/secc && python3 start_evse.py > /tmp/secc.log 2>&1; sleep infinity"
#   docker exec edf-secc ss -lnt        # their log names neither the address nor the port
set -uo pipefail

SECC="${SECC:?set SECC to their endpoint, e.g. '[fd00:edf::2]:49152'}"
OUT="${OUT:-/tmp/edf-460}"
REPO="${1:-$HOME/i15118}"

mkdir -p "$OUT"

arm() {

    local name="$1" sid="$2"

    echo
    echo "############ arm $name (V2G_INTEROP_SESSIONID=${sid:-unset}) ############"

    # `export`, not a `${sid:+VAR=value} cmd` prefix: that form is a command *word*, not an assignment,
    # and it cost a run on 2026-08-15 by failing with exit 127 before dotnet was reached.
    (
        cd "$REPO" || exit 1
        export V2G_INTEROP_SECC="$SECC"
        export V2G_INTEROP_PROTOCOL=20
        export V2G_INTEROP_MODE=dc
        export V2G_INTEROP_DYNAMIC=1
        export V2G_INTEROP_RECORD="$OUT/$name"
        [ -n "$sid" ] && export V2G_INTEROP_SESSIONID="$sid"
        [ -n "${SERVICE_IDS:-}" ] && export V2G_INTEROP_SERVICE_IDS="$SERVICE_IDS"

        dotnet test -c Release ISO15118ConformanceTests.Simulation \
            --logger "console;verbosity=detailed" \
            --filter "FullyQualifiedName~EvDriveFlowInteropTests.OurEvcc"
    ) 2>&1 | tee "$OUT/$name.log"

    echo "--- arm $name: exit ${PIPESTATUS[0]} ---"

}

# control first, and it is not a formality: it is the run the other two are read against.
arm control ''
arm zero    'zero'
arm foreign 'deadbeefdeadbeef'

echo
echo "=== what our EV actually put on the wire (no decoder; see session-id-from-frames.py) ==="
python3 "$(dirname "$0")/session-id-from-frames.py" --requests --expect deadbeefdeadbeef     "$OUT/*/*.frames.log" 2>/dev/null \
    || echo "(run session-id-from-frames.py --requests over $OUT yourself)"
