#!/usr/bin/env bash
#
# OUR EVCC against their responder — ISO 15118-2 DC, EIM, plain TCP.
#
# Their responder answers with the recorded charger's replies ("when response is not defined, expect is
# used as the response"), so this direction puts a real ABB/Audi capture's answers in front of our car —
# including the fields our own SECC would never think to send.
#
# Usage: live-evcc-iso2-dc.sh [iface=evcc-veth] [endpoint]
#   with an endpoint   connect straight to it, e.g. '[fe80::ac52:27ff:fef3:d0d7%evcc-veth]:64109'
#   without one        SDP-discover their responder on <iface>
#
# With an endpoint the <iface> argument is ignored — pass '' for it. That is the relay path in
# README.md, and it runs from any machine, this Mac included: no interface, no ip(8), no multicast.
set -uo pipefail

iface="${1:-evcc-veth}"
endpoint="${2:-}"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli="$here/../../Vanaheimr.V2G.Simulation.Cli"

# Only when we have to discover. With an endpoint in hand the interface is irrelevant — that is the
# whole point of the relay path in README.md, and it is what lets this run from a machine that has no
# such interface and no `ip` at all.
if [ -z "$endpoint" ] && ! ip link show "$iface" >/dev/null 2>&1; then
    echo "no interface '$iface' — run ./prepare-tux-evse.sh and then their bridge script," >&2
    echo "or pass an endpoint as the second argument (see 'The short path' in README.md)." >&2
    ip -br link show 2>/dev/null | awk '{print "  have: " $1}' >&2
    exit 1
fi

cat <<EOF
>>> their responder must already be running:

    podman run --rm --name podman_evse --network=host --cap-add=NET_ADMIN -it \\
      registry.redpesk.bzh/tux-evse/afb-iso15118:v0.1 bash -c \\
      "binding-start-evse \\
         --simulation_conf /usr/share/iso15118-simulator-rs/binding-simu15118-evse-no-tls.yaml \\
         --scenario_file  /usr/share/iso15118-simulator-rs/audi-dc-iso2-compact.json --no-clean"

    Its transaction log is at http://localhost:1235/devtools/ .

EOF

if [ -n "$endpoint" ]; then
    # Bracketed, with the zone inside the brackets: [fe80::1%evcc-veth]:64109. Anything else is refused
    # rather than silently connected to a scope-0 address that cannot reach a link-local peer.
    echo ">>> our EVCC -> $endpoint  (ISO 15118-2 DC, EIM, plain TCP)"
    exec dotnet run --project "$cli" -c Release -- \
        evcc --connect "$endpoint" --protocol 2 --mode dc
else
    echo ">>> our EVCC, SDP-discovering their responder on $iface  (ISO 15118-2 DC, EIM, plain TCP)"
    echo "    (if their responder does not answer SDP — its shipped scenario marks the SDP transaction"
    echo "     'injector_only' — take the TCP endpoint from its log and pass it as the second argument.)"
    exec dotnet run --project "$cli" -c Release -- \
        evcc --sdp --interface "$iface" --protocol 2 --mode dc
fi
