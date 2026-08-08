#!/usr/bin/env bash
# Live over-the-wire ISO 15118-20 DC interop over TLS (our EVCC -> Josev SECC): SDP-discover a running Josev
# TLS SECC (security = TLS) and connect OUR EVCC to it with the .NET SslStream backend. Pass a client PKCS#12
# as the first arg for mutual TLS 1.3 (Stage 2); omit it for TLS 1.2 unilateral (Stage 1).
#
# Prereqs (see docs/interop-runs/2026-07-21-iso20-dc-tls-forward/notes.md):
#   - Josev SECC in HOST mode with SECC_ENFORCE_TLS=True; ENABLE_TLS_1_3=False (Stage 1) or True (Stage 2).
#   - .NET 10 here; build the CLI first: dotnet build -c Release.
#   - Stage 2 client cert: build oem.p12 from Josev's OEM leaf+key+Sub-CAs (pw 12345, venv iso15118_2/certs).
#
# Usage: live-evcc-tls.sh [client-cert.p12] [interface]     (defaults: no client cert, eth0)
set -euo pipefail

client_cert="${1:-}"
iface="${2:-eth0}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli_dll="$here/../../libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_EVCC/bin/Release/net10.0/WWCP_ISO15118_EVCC.dll"

# The CLI's own --sdp discovery works live since the MulticastLoopback fix (2026-07-23);
# with --tls-backend set it requests security=TLS in the SDP_Request.
args=(--sdp --interface "$iface" --protocol 20 --mode dc --tls-backend dotnet)
[ -n "$client_cert" ] && args+=(--client-cert "$client_cert" --client-cert-pass 12345)
exec dotnet "$cli_dll" "${args[@]}"
