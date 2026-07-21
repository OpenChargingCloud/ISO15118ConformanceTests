#!/usr/bin/env bash
# Run OUR EVCC against a running Josev SECC.
# Usage: run-our-evcc.sh <josev-host:port> [protocol=2|20] [mode=ac|dc]
set -euo pipefail

endpoint="${1:?usage: run-our-evcc.sh <josev-host:port> [protocol] [mode]}"
protocol="${2:-2}"
mode="${3:-ac}"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli="$here/../../Vanaheimr.V2G.Simulation.Cli"

echo "our EVCC -> Josev SECC $endpoint  (protocol -$protocol, $mode)"
exec dotnet run --project "$cli" -c Release -- \
    evcc --connect "$endpoint" --protocol "$protocol" --mode "$mode"
