#!/usr/bin/env bash
#
# Their injector (a captured Audi) against OUR SECC — ISO 15118-2 DC, EIM, plain TCP.
#
# Starts our station with SDP advertising on the charger-side interface, then waits for their EV to
# discover it and run the scenario. Their injector's own pass/fail is not the verdict — see README.md,
# "Reading the verdict": their `expect` blocks hold the captured charger's values, and ours is not that
# charger.
#
# Usage: reverse-iso2-dc.sh [iface=evse-veth] [port=55000]
set -uo pipefail

iface="${1:-evse-veth}"
port="${2:-55000}"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli="$here/../../libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_CLI"
log="${V2G_INTEROP_LOG:-/tmp/tux-reverse-secc.log}"

if ! ip link show "$iface" >/dev/null 2>&1; then
    echo "no interface '$iface' — run ./prepare-tux-evse.sh and then their bridge script." >&2
    ip -br link show 2>/dev/null | awk '{print "  have: " $1}' >&2
    exit 1
fi

cleanup() { [ -n "${SECC_PID:-}" ] && kill "$SECC_PID" 2>/dev/null; }
trap cleanup EXIT

echo ">>> our SECC on :$port  — ISO 15118-2 DC, EIM, plain TCP, SDP on $iface"
dotnet run --project "$cli" -c Release -- \
    secc --listen "$port" --protocol 2 --mode dc --sdp --interface "$iface" >"$log" 2>&1 &
SECC_PID=$!

sleep 3
if ! kill -0 "$SECC_PID" 2>/dev/null; then
    echo "our SECC exited immediately:" >&2
    cat "$log" >&2
    exit 1
fi
head -4 "$log"

cat <<EOF

>>> now start their injector, in another terminal:

    podman run --rm --name podman_evcc --network=host --cap-add=NET_ADMIN -it \\
      registry.redpesk.bzh/tux-evse/afb-iso15118:v0.1 bash -c \\
      "binding-start-evcc \\
         --simulation_conf /usr/share/iso15118-simulator-rs/binding-simu15118-evcc-no-tls.yaml \\
         --scenario_file  /usr/share/iso15118-simulator-rs/audi-dc-iso2-compact.json --no-clean"

    Their transaction log is at http://localhost:1234/devtools/ .

>>> waiting; Ctrl-C when the session has finished. Our side is logging to $log

EOF

# Follow our station's side until interrupted. The session's own outcome is in this log; the recording,
# if you want one, comes from the fixture rather than from here (see README.md).
tail -f "$log" | grep --line-buffered -iE \
    "advertising|listening|SessionSetup|ServiceDiscovery|Authorization|ChargeParameter|CableCheck|PreCharge|CurrentDemand|WeldingDetection|PowerDelivery|SessionStop|Session complete|error|exception|abort|timeout"
