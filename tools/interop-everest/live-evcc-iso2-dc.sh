#!/usr/bin/env bash
#
# OUR EVCC against EVerest's charger — the direction worth the setup.
#
# Their EvseV2G (DIN 70121 / ISO 15118-2, cbV2G underneath) or Evse15118D20 (-20) is the half of EVerest
# nothing here has met. Their EV is Josev, which we already have recorded runs against, so the forward
# direction is where the findings are.
#
# Usage: live-evcc-iso2-dc.sh [iface=eth0] [endpoint] [protocol=2] [mode=dc]
#   with an endpoint   connect straight to it, e.g. '[fe80::1%eth0]:15118'
#   without one        SDP-discover their charger on <iface> (EvseV2G runs an SDP server by default)
set -uo pipefail

iface="${1:-eth0}"
endpoint="${2:-}"
protocol="${3:-2}"
mode="${4:-dc}"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli="$here/../../Vanaheimr.V2G.Simulation.Cli"

if ! ip link show "$iface" >/dev/null 2>&1; then
    echo "no interface '$iface' — it must be the one in EvseV2G's 'device' setting." >&2
    ip -br link show 2>/dev/null | awk '{print "  have: " $1}' >&2
    exit 1
fi

cat <<EOF
>>> their charger must already be running, e.g.

    ./run_sil.sh                       # or however your build starts EVerest
    # config: config/config-sil-dc.yaml        (-2 DC)
    #         config/config-sil-dc-d20.yaml    (-20 DC, Evse15118D20)
    #         config/config-sil-dc-isomux.yaml (both, one endpoint)

    Check in the config: EvseV2G device = $iface, enable_sdp_server = true,
    and tls_security = prohibit for a first plain-TCP run.

EOF

if [ -n "$endpoint" ]; then
    echo ">>> our EVCC -> $endpoint  (ISO 15118-$protocol $mode, EIM, plain TCP)"
    exec dotnet run --project "$cli" -c Release -- \
        evcc --connect "$endpoint" --protocol "$protocol" --mode "$mode"
else
    echo ">>> our EVCC, SDP-discovering their charger on $iface  (ISO 15118-$protocol $mode, EIM, plain TCP)"
    exec dotnet run --project "$cli" -c Release -- \
        evcc --sdp --interface "$iface" --protocol "$protocol" --mode "$mode"
fi
