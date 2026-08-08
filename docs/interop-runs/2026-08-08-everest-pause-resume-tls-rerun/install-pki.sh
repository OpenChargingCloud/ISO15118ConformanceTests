#!/usr/bin/env bash
# Back up their PKI, then install the minted iso-20 tree wholesale (station leaf included -- station
# and client have to agree on one chain), and build the client p12 our EVCC presents.
# The material was minted by their own vendored create_certs.sh -v iso-20 on 2026-08-08 and is still
# valid; only the installation was rolled back at the end of that run.
export HOME=/home/ahzf
set -uo pipefail

P=$HOME/everest/everest-core/build/_deps/josev-src/iso15118/shared/pki/iso15118_20/certs
DIST=$HOME/everest/dist/etc/everest
OUT=$HOME/everest/tlsrun
mkdir -p "$OUT"

echo "=== backing up their PKI first ==="
B=$HOME/everest/pki-backup-rerun-$(date +%H%M%S).tgz
tar czf "$B" -C "$DIST" certs && echo "  $B ($(stat -c%s "$B") bytes)"

echo
echo "=== installing over $DIST/certs ==="
cp -r "$P/." "$DIST/certs/" && echo "  ok"
openssl x509 -in "$DIST/certs/client/cso/SECC_LEAF.pem" -noout -subject -dates 2>&1 | sed 's/^/  station leaf now: /'

echo
echo "=== our client credential: VEHICLE_CERT_CHAIN + VEHICLE_LEAF.key -> p12 ==="
openssl pkcs12 -export \
    -inkey "$P/client/vehicle/VEHICLE_LEAF.key" -passin pass:123456 \
    -in    "$P/client/vehicle/VEHICLE_CERT_CHAIN.pem" \
    -name vehicle -passout pass:123456 -out "$OUT/vehicle.p12" 2>&1 | sed 's/^/  /'
echo "  certificates inside: $(openssl pkcs12 -in "$OUT/vehicle.p12" -passin pass:123456 -nokeys 2>/dev/null | grep -c 'subject=')"

echo
echo "=== trust bundle for our side (their V2G root + CPO intermediates) ==="
cat "$P/ca/v2g/V2G_ROOT_CA.pem" "$P/ca/cso/CPO_SUB_CA1.pem" "$P/ca/cso/CPO_SUB_CA2.pem" > "$OUT/trust.pem" 2>/dev/null
echo "  $(grep -c 'BEGIN CERTIFICATE' "$OUT/trust.pem") certificate(s) -> $OUT/trust.pem"
