#!/usr/bin/env bash
#
# OUR EVCC against their SECC (eVDriveFlow start_evse.py) — ISO 15118-20 DC, EIM, plain TCP.
#
# The direction that tests what we send, against a station that runs Dynamic control mode by design and
# sets its own departure time and SoC targets.
#
# Usage: live-evcc-iso20-dc.sh [iface=enp0s3] [endpoint]
#   with an endpoint   connect straight to it, e.g. '[fe80::1%enp0s3]:49152'  (evse_config.ini tcp_port)
#   without one        SDP-discover their station on <iface>
#
# With an endpoint the <iface> argument is ignored — pass '' for it. That is the relay path in
# README.md, and it runs from any machine, this Mac included: no interface, no ip(8), no multicast.
set -uo pipefail

iface="${1:-enp0s3}"
endpoint="${2:-}"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli="$here/../../EVSimulatorApp/simulation/Vanaheimr.V2G.Simulation.Cli"

# Only when we have to discover. With an endpoint in hand the interface is irrelevant — that is the
# whole point of the relay path in README.md, and it is what lets this run from a machine that has no
# such interface and no `ip` at all.
if [ -z "$endpoint" ] && ! ip link show "$iface" >/dev/null 2>&1; then
    echo "no interface '$iface' — it must be the one named in their secc/evse_config.ini," >&2
    echo "or pass an endpoint as the second argument (see 'The short path' in README.md)." >&2
    ip -br link show 2>/dev/null | awk '{print "  have: " $1}' >&2
    exit 1
fi

cat <<'EOF'
>>> their SECC must already be running:

    conda activate edf15118-20
    cd <their-checkout>/secc && python3 start_evse.py     # or evse_gui.py, for departure time / SoC

    Check first, in evse_config.ini: the interface, tcp_port (49152 by default), and whether
    virtual_mode should still be true. TLS is on by default — turn it off for this run in
    shared/global_values.py (SECURITY_PROTOCOL), or use --tls-backend bc (see README.md).

EOF

if [ -n "$endpoint" ]; then
    echo ">>> our EVCC -> $endpoint  (ISO 15118-20 DC, EIM, plain TCP)"
    exec dotnet run --project "$cli" -c Release -- \
        evcc --connect "$endpoint" --protocol 20 --mode dc
else
    echo ">>> our EVCC, SDP-discovering their station on $iface  (ISO 15118-20 DC, EIM, plain TCP)"
    echo "    (if nothing is discovered, take the endpoint from evse_config.ini and pass it as the"
    echo "     second argument: '[<their-link-local>%$iface]:49152')"
    exec dotnet run --project "$cli" -c Release -- \
        evcc --sdp --interface "$iface" --protocol 20 --mode dc
fi
