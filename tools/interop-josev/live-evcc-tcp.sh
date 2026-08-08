#!/usr/bin/env bash
# Live over-the-wire ISO 15118-20 DC interop: SDP-discover a running Josev SECC and immediately connect
# OUR EVCC to it over plain TCP (no TLS), in one tight sequence so Josev's per-request TCP port is fresh.
#
# Prereqs (see docs/interop-runs/2026-07-21-iso20-dc-tcp-live/notes.md):
#   - Josev SECC running in HOST mode with SECC_ENFORCE_TLS=False on this host's V2G interface, e.g.
#       docker compose -f docker-compose-host-mode.yml -f docker-compose.livetest.yml up secc redis
#   - .NET 10 available here (WSL: `dotnet --version`); build the CLI first: dotnet build -c Release.
#
# Usage: live-evcc-tcp.sh [interface] [mode]      (defaults: eth0 dc)
set -euo pipefail

iface="${1:-eth0}"
mode="${2:-dc}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli_dll="$here/../../libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EVCC/bin/Release/net10.0/WWCP_ISO15118_EVCC.dll"

# The CLI's own --sdp discovery works live since the MulticastLoopback fix (2026-07-23).
exec dotnet "$cli_dll" --sdp --interface "$iface" --protocol 20 --mode "$mode"
