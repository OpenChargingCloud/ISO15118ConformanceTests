#!/usr/bin/env bash
#
# Fetch what a tux-evse interop run needs — and stop short of anything that needs a password.
#
# Their bring-up has one step that must run as root (creating the veth pairs their simulators talk over).
# This script downloads that step and shows it to you; it does not run it. A script fetched over the
# network and piped into sudo is exactly the shape of thing that should be read first, and the reading is
# not ours to skip on your behalf.
#
# Usage: prepare-tux-evse.sh [--no-image]
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
image="registry.redpesk.bzh/tux-evse/afb-iso15118:v0.1"
bridge_url="https://raw.githubusercontent.com/tux-evse/iso15118-simulator-rs/refs/heads/main/afb-test/network/client-server-bridge.sh"
bridge="$here/client-server-bridge.sh"

echo "=== 1/3  their network script"
if [ -f "$bridge" ]; then
    echo "    already here: $bridge"
else
    curl -fsSL "$bridge_url" -o "$bridge"
    chmod a+x "$bridge"
    echo "    downloaded to $bridge"
fi
echo "    sha256: $(shasum -a 256 "$bridge" | cut -d' ' -f1)"
echo "    $(wc -l < "$bridge") lines. Read it, then run it yourself:"
echo
echo "        sudo $bridge"
echo
echo "    It creates the virtual interfaces their simulators use: evse-tun, evse-veth, evcc-veth."
echo "    Nothing else in this harness needs root."

echo "=== 2/3  the container image"
if [ "${1:-}" = "--no-image" ]; then
    echo "    skipped (--no-image)"
elif command -v podman >/dev/null 2>&1; then
    podman pull "$image"
else
    echo "    podman is not installed; not installing it. Either install it yourself, or use their"
    echo "    distribution packages (iso15118-simulator-rs, iso15118-simulator-rs-test) — see README.md."
fi

echo "=== 3/3  check"
missing=0
for iface in evse-tun evse-veth evcc-veth; do
    if ip link show "$iface" >/dev/null 2>&1; then
        echo "    $iface: up"
    else
        echo "    $iface: MISSING — run the bridge script above"
        missing=1
    fi
done

if [ "$missing" = 0 ]; then
    echo
    echo "Ready. Next: ./reverse-iso2-dc.sh evse-veth 55000   (their car against our station)"
    echo "          or ./live-evcc-iso2-dc.sh evcc-veth       (our car against their station)"
fi
