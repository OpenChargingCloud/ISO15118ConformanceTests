#!/usr/bin/env bash
# Run OUR SECC so a Josev EVCC can connect to it.
# Usage: run-our-secc.sh <listen-port> [protocol=2|20] [mode=ac|dc]
set -euo pipefail

port="${1:?usage: run-our-secc.sh <listen-port> [protocol] [mode]}"
protocol="${2:-2}"
mode="${3:-ac}"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli="$here/../../libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_SECC"

echo "our SECC listening on :$port for a Josev EVCC  (protocol -$protocol, $mode)"
exec dotnet run --project "$cli" -c Release -- \
    --listen "$port" --protocol "$protocol" --mode "$mode"
