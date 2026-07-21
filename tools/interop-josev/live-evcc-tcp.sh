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
cli_dll="$here/../../Vanaheimr.V2G.Simulation.Cli/bin/Release/net10.0/Vanaheimr.V2G.Simulation.Cli.dll"

ep=$(IFACE="$iface" python3 - <<'PY'
import os, socket, struct
req = bytes([0x01, 0xFE, 0x90, 0x00, 0x00, 0x00, 0x00, 0x02, 0x10, 0x00])  # SDP req: NO_TLS, TCP
ifidx = socket.if_nametoindex(os.environ["IFACE"])
s = socket.socket(socket.AF_INET6, socket.SOCK_DGRAM)
s.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_MULTICAST_IF, struct.pack("I", ifidx))
s.settimeout(5)
s.sendto(req, ("ff02::1", 15118, 0, ifidx))
data, _ = s.recvfrom(1024)
p = data[8:]  # skip the 8-byte V2GTP header
print(socket.inet_ntop(socket.AF_INET6, p[0:16]), int.from_bytes(p[16:18], "big"))
PY
)
addr=$(echo "$ep" | awk '{print $1}')
port=$(echo "$ep" | awk '{print $2}')
echo ">>> SDP discovered SECC at [$addr%$iface]:$port"

exec dotnet "$cli_dll" evcc --connect "[$addr%$iface]:$port" --protocol 20 --mode "$mode"
