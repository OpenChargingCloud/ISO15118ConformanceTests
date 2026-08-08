#!/usr/bin/env bash
# Install the freshly minted tree wholesale (station leaf included — station and client must agree),
# and build the client p12 our EVCC presents.
set -uo pipefail
P=$HOME/everest/everest-core/build/_deps/josev-src/iso15118/shared/pki/iso15118_20/certs
DIST=$HOME/everest/dist/etc/everest/certs
OUT=$HOME/everest/tlsrun
mkdir -p "$OUT"

echo "=== installing over $DIST ==="
cp -r "$P/." "$DIST/" && echo "  ok"

echo
echo "=== station chain, as the station will now present it ==="
openssl crl2pkcs7 -nocrl -certfile "$DIST/client/cso/CPO_CERT_CHAIN.pem" 2>/dev/null \
  | openssl pkcs7 -print_certs -noout 2>/dev/null | sed 's/^/  /' | head -8

echo
echo "=== our client credential: VEHICLE_CERT_CHAIN + VEHICLE_LEAF.key -> p12 ==="
openssl pkcs12 -export \
    -inkey "$P/client/vehicle/VEHICLE_LEAF.key" -passin pass:123456 \
    -in    "$P/client/vehicle/VEHICLE_CERT_CHAIN.pem" \
    -name vehicle -passout pass:123456 -out "$OUT/vehicle.p12" 2>&1 | sed 's/^/  /'
ls -la "$OUT/vehicle.p12" 2>/dev/null | sed 's/^/  /'
echo "  certificates inside: $(openssl pkcs12 -in "$OUT/vehicle.p12" -passin pass:123456 -nokeys 2>/dev/null | grep -c 'subject=')"

echo
echo "=== trust bundle for our side (their V2G root + CPO intermediates) ==="
cat "$P/ca/v2g/V2G_ROOT_CA.pem" "$P/ca/cso/CPO_SUB_CA1.pem" "$P/ca/cso/CPO_SUB_CA2.pem" > "$OUT/trust.pem" 2>/dev/null
echo "  $(grep -c 'BEGIN CERTIFICATE' "$OUT/trust.pem") certificate(s) -> $OUT/trust.pem"
