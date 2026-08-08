#!/usr/bin/env bash
#
# EVerest's PyEvJosev against OUR SECC.
#
# Lower value than the forward run and worth saying so: PyEvJosev is the Josev-derived stack, the same
# implementation family as every run already recorded under docs/interop-runs/. Do the forward direction
# first, and check there before calling anything found here new.
#
# Usage: reverse-iso2-dc.sh [iface=eth0] [port=55000] [protocol=2] [mode=dc]
set -uo pipefail

iface="${1:-eth0}"
port="${2:-55000}"
protocol="${3:-2}"
mode="${4:-dc}"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli="$here/../../libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_CLI"
log="${V2G_INTEROP_LOG:-/tmp/everest-reverse-secc.log}"

if ! ip link show "$iface" >/dev/null 2>&1; then
    echo "no interface '$iface' — it must be the one in PyEvJosev's 'device' setting." >&2
    ip -br link show 2>/dev/null | awk '{print "  have: " $1}' >&2
    exit 1
fi

dynamic=()
[ "$protocol" = "20" ] && dynamic=(--dynamic)

cleanup() { [ -n "${SECC_PID:-}" ] && kill "$SECC_PID" 2>/dev/null; }
trap cleanup EXIT

echo ">>> our SECC on :$port  — ISO 15118-$protocol $mode, EIM, plain TCP, SDP on $iface"
dotnet run --project "$cli" -c Release -- \
    secc --listen "$port" --protocol "$protocol" --mode "$mode" "${dynamic[@]}" \
         --sdp --interface "$iface" >"$log" 2>&1 &
SECC_PID=$!

sleep 3
if ! kill -0 "$SECC_PID" 2>/dev/null; then
    echo "our SECC exited immediately:" >&2
    cat "$log" >&2
    exit 1
fi
head -4 "$log"

cat <<EOF

>>> now start their EV side.

    PyEvJosev supports NOTHING by default — every supported_* key defaults to false — so set the ones
    this run needs in your config:

        iso15118_car:
          module: PyEvJosev
          config_module:
            device: $iface
            supported_ISO15118_2: true          # or supported_ISO15118_20_DC for a -20 run
            supported_d20_energy_services: DC,DC_BPT

    Their EV discovers a station by SDP on that interface, so it will find ours — provided EVerest's own
    charger is not also answering on it. If the EV half cannot be started without a charger alongside,
    point EvseV2G's 'device' at a different interface so ours is the only station on this one.

>>> waiting; Ctrl-C when the session has finished. Our side is logging to $log

EOF

tail -f "$log" | grep --line-buffered -iE \
    "advertising|listening|SessionSetup|ServiceDiscovery|Authorization|ChargeParameter|CableCheck|\
PreCharge|CurrentDemand|ChargeLoop|PowerDelivery|WeldingDetection|SessionStop|Session complete|\
error|exception|abort|timeout"
