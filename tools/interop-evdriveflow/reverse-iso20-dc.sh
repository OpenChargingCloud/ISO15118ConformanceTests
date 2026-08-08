#!/usr/bin/env bash
#
# Their EV (eVDriveFlow start_ev.py) against OUR SECC — ISO 15118-20 DC, EIM, plain TCP, Dynamic control.
#
# --dynamic is not optional here: it makes our station offer the Dynamic (ControlMode 2) parameter set
# first, and an EV that takes the first offered set then runs a Dynamic session. Their stack is built
# around Dynamic, so without it expect the session to stop shortly after service selection.
#
# Usage: reverse-iso20-dc.sh [iface=enp0s3] [port=55000]
set -uo pipefail

iface="${1:-enp0s3}"
port="${2:-55000}"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli="$here/../../libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_SECC"
log="${V2G_INTEROP_LOG:-/tmp/edf-reverse-secc.log}"

if ! ip link show "$iface" >/dev/null 2>&1; then
    echo "no interface '$iface' — it must be the one named in their evcc/ev_config.ini." >&2
    ip -br link show 2>/dev/null | awk '{print "  have: " $1}' >&2
    exit 1
fi

cleanup() { [ -n "${SECC_PID:-}" ] && kill "$SECC_PID" 2>/dev/null; }
trap cleanup EXIT

echo ">>> our SECC on :$port  — ISO 15118-20 DC, EIM, plain TCP, Dynamic control mode, SDP on $iface"
dotnet run --project "$cli" -c Release -- \
    --listen "$port" --protocol 20 --mode dc --dynamic --sdp --interface "$iface" >"$log" 2>&1 &
SECC_PID=$!

sleep 3
if ! kill -0 "$SECC_PID" 2>/dev/null; then
    echo "our SECC exited immediately:" >&2
    cat "$log" >&2
    exit 1
fi
head -4 "$log"

cat <<EOF

>>> now start their EV, in another terminal:

    conda activate edf15118-20
    cd <their-checkout>/evcc && python3 start_ev.py        # or ev_gui.py, for the power slider

    Check first, in ev_config.ini: interface = $iface, and whether virtual_mode should still be true.
    TLS is on by default — for this run turn it off in shared/global_values.py (SECURITY_PROTOCOL).

>>> waiting; Ctrl-C when the session has finished. Our side is logging to $log

EOF

tail -f "$log" | grep --line-buffered -iE \
    "advertising|listening|SessionSetup|AuthorizationSetup|Authorization|ServiceDiscovery|ServiceSelection|\
ScheduleExchange|Dynamic|CableCheck|PreCharge|ChargeLoop|PowerDelivery|WeldingDetection|SessionStop|\
Session complete|renegotiat|error|exception|abort|timeout"
