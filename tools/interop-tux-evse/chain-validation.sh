#!/usr/bin/env bash
#
# Their injector's TLS client chain, judged by our station against their roots.
#
# tux-evse's PKI is two deep, not three: `_root` -> `_server` -> `_client` / `_contract`, all
# prime256v1, and `_server` is a CA certificate that also serves as a station's TLS leaf. Their
# `_client_chain.pem` carries THREE certificates — leaf, sub-CA and **their own root** — which makes
# this the counterparty that tests whether a validator can be talked into trusting an anchor a peer
# supplied. It cannot: CustomRootTrust takes anchors from the store and nowhere else, and the refusal
# reads "self-signed certificate in certificate chain" rather than the "unable to get local issuer
# certificate" every other counterparty produces.
#
# Usage: chain-validation.sh <trust-roots-path|""> <label>
#   run/pki/_root.pem      valid, anchored at DC=root
#   run/pki/_server.pem    refused: a Sub-CA in the store is never an anchor
#   somebody else's root   refused: their root is on the wire and still not trusted
#
# Unlike the recording fixture, the station program does not pin cipher suites, so no
# V2G_INTEROP_TLS_SUITES=platform equivalent is needed to meet their GnuTLS profile — and no
# conformance claim about suites is made here either.
set -uo pipefail

TRUST="${1:-}"
LABEL="${2:-run}"

T="${TUX_ROOT:-$HOME/tux-evse}"
PKI="$T/run/pki"
RUN="${TUX_RUN:-$T/run/chain}"
SCENARIO="${TUX_SCENARIO:-$T/run/audi-relaxed-autorun.json}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="${CONFORMANCE_REPO:-$(cd "$here/../.." && pwd)}"
SECC="${SECC_DLL:-$REPO/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_SECC/bin/Release/net10.0/WWCP_ISO15118_SECC.dll}"
mkdir -p "$RUN"

# Their SDP client binds without SO_REUSEADDR and would collide with our SDP server on one stack.
# Idempotent, and it does not survive a WSL restart.
bash "$T/run/netns.sh" >/dev/null 2>&1

# We serve THEIR station certificate, or their EVCC has nothing to verify us against.
if [ ! -f "$PKI/server.pfx" ]; then
    openssl pkcs12 -export -inkey "$PKI/_server_key.pem" -in "$PKI/_server.pem" \
        -certfile "$PKI/_root.pem" -name tux-evse-server -passout pass:interop -out "$PKI/server.pfx" \
        && echo "  wrote server.pfx from their _server + _root"
fi

# They rename the process to the --name value, so `pkill -x afb-binder` never matches.
pkill -x afb-evcc 2>/dev/null; pkill -f WWCP_ISO15118_SECC 2>/dev/null; sleep 2

echo "=== our SECC :55000, -2 DC, SDP on evse-veth, mutual TLS, trust-roots='${TRUST:-<none>}' ==="
if [ -n "$TRUST" ]; then
    setsid nohup dotnet "$SECC" --listen 55000 --protocol 2 --mode dc --sdp --interface evse-veth \
        --tls --server-cert "$PKI/server.pfx" --server-cert-pass interop \
        --require-client-cert --trust-roots "$TRUST" > "$RUN/secc-$LABEL.log" 2>&1 < /dev/null &
else
    setsid nohup dotnet "$SECC" --listen 55000 --protocol 2 --mode dc --sdp --interface evse-veth \
        --tls --server-cert "$PKI/server.pfx" --server-cert-pass interop \
        --require-client-cert > "$RUN/secc-$LABEL.log" 2>&1 < /dev/null &
fi
sleep 8
grep -E 'Trust roots|Presenting|listening|advertis' "$RUN/secc-$LABEL.log" | sed 's/^/  /'

echo
echo "=== their injector, in netns tuxev ==="
# THE CAP IS OURS AND IT IS -k. Their run-injector-tls.sh uses `timeout` without it, and a refused
# handshake is exactly the "peer disconnected" case that sends this binder into a ~20 MB/s log spin
# that SIGTERM stops logging without ending. Measured 2026-08-09: 1.18 GB and seven minutes.
timeout -k 5 60 ip netns exec tuxev bash "$T/run/run-injector-tls.sh" \
    "$SCENARIO" 40 "$RUN/injector-$LABEL.log"
pkill -9 -x afb-evcc 2>/dev/null
if [ "$(stat -c%s "$RUN/injector-$LABEL.log" 2>/dev/null || echo 0)" -gt 5000000 ]; then
    head -c 200000 "$RUN/injector-$LABEL.log" > "$RUN/injector-$LABEL.head" \
      && mv "$RUN/injector-$LABEL.head" "$RUN/injector-$LABEL.log"
    echo "  (their log truncated — the spin, not content)"
fi

echo
echo "=== our station's verdict ==="
grep -E 'TLS client|Session' "$RUN/secc-$LABEL.log" | sed 's/^/  /' | head -6

echo
echo "=== their injector, how far it got ==="
grep -iE 'Check |SimulationStatus|no_challenge' "$RUN/injector-$LABEL.log" 2>/dev/null | head -6 | cut -c1-150 | sed 's/^/  /'

sleep 1
pkill -f WWCP_ISO15118_SECC 2>/dev/null
echo
echo "=== logs: $RUN/secc-$LABEL.log, $RUN/injector-$LABEL.log ==="
