#!/usr/bin/env bash
# The TLS forward arms against 2026.10.0, with the release's own test PKI (dist-2610/etc/everest/certs):
# our EVCC trusts their V2G root, presents their VEHICLE_LEAF chain for mutual TLS 1.3, and their own
# MO contract credential for Plug & Charge. Nothing is minted.
#   tls-2610.sh [arm ...]     (no argument = all)
set -u
E=/home/ahzf/everest
C=$E/configs-2610
P=$E/dist-2610/etc/everest/certs
T=$E/run/2610-fwd/tls
F="bash $E/fwd-2610.sh"
mkdir -p "$T"

cp "$P/ca/v2g/V2G_ROOT_CA.pem" "$T/v2g-root.pem"
cat "$P/ca/vehicle/VEHICLE_SUB_CA2.pem" "$P/ca/vehicle/VEHICLE_SUB_CA1.pem" > "$T/vehicle-subcas.pem"
openssl pkcs12 -export -out "$T/vehicle.p12" \
  -inkey "$P/client/vehicle/VEHICLE_LEAF.key" -passin "file:$P/client/vehicle/VEHICLE_LEAF_PASSWORD.txt" \
  -in "$P/client/vehicle/VEHICLE_LEAF.pem" -certfile "$T/vehicle-subcas.pem" -passout pass:123456 || exit 1
openssl pkcs12 -in "$P/client/mo/MO_CERT_CHAIN.p12" -passin pass:123456 -nokeys 2>/dev/null \
  | grep -c "BEGIN CERTIFICATE" | sed 's/^/MO_CERT_CHAIN.p12 certificates: /'
openssl x509 -in "$P/client/vehicle/VEHICLE_LEAF.pem" -noout -subject -issuer

TLS="V2G_INTEROP_TLS=1 V2G_INTEROP_TLS_TRUST=$T/v2g-root.pem"
MTLS="$TLS V2G_INTEROP_TLS_CLIENT=$T/vehicle.p12:123456"
PNC="V2G_INTEROP_CONTRACT_CERT=$P/client/mo/MO_CERT_CHAIN.p12 V2G_INTEROP_CONTRACT_PASS=123456"

WANTED=" $* "
want() { [ "$WANTED" = "  " ] || [[ "$WANTED" == *" $1 "* ]]; }

want ac2-tls12      && SDPSEC=00 PROTO=2  MODE=ac $F ac2-tls12      $C/config-ac2-tls-fwd2610.yaml $TLS
want d20-tls13      && SESSIONS=2 SDPSEC=00 PROTO=20 MODE=dc $F d20-tls13 $C/config-d20-tls-fwd2610.yaml $MTLS
want ac20-tls13     && TRIGGER=1 SDPSEC=00 PROTO=20 MODE=ac $F ac20-tls13 $C/config-ac20-tls-fwd2610.yaml $MTLS
want mux-tls-iso2   && PROBE=muxtls PROTO=2  MODE=dc $F mux-tls-iso2   $C/config-mux-tls-fwd2610.yaml $TLS
want mux-tls-iso20  && PROBE=muxtls PROTO=20 MODE=dc $F mux-tls-iso20  $C/config-mux-tls-fwd2610.yaml $MTLS
want mux-tls-both   && PROBE=muxtls PROTO=both MODE=dc $F mux-tls-both $C/config-mux-tls-fwd2610.yaml $MTLS V2G_INTEROP_SAP_FIRST=20
want d20-tls13-dyn  && SDPSEC=00 PROTO=20 MODE=dc $F d20-tls13-dyn  $C/config-d20-tls-fwd2610.yaml $MTLS V2G_INTEROP_DYNAMIC=1
want ac20-tls13-dyn && TRIGGER=1 SDPSEC=00 PROTO=20 MODE=ac $F ac20-tls13-dyn $C/config-ac20-tls-fwd2610.yaml $MTLS V2G_INTEROP_DYNAMIC=1
want mux-tls-both-dyn && PROBE=muxtls PROTO=both MODE=dc $F mux-tls-both-dyn $C/config-mux-tls-fwd2610.yaml $MTLS V2G_INTEROP_SAP_FIRST=20 V2G_INTEROP_DYNAMIC=1
want dc2-pnc-tls    && SDPSEC=00 PROTO=2  MODE=dc $F dc2-pnc-tls    $C/config-dc2-pnc-fwd2610.yaml $TLS $PNC
want dc2-pnc-plain  && PROTO=2  MODE=dc $F dc2-pnc-plain  $C/config-dc2-pnc-fwd2610.yaml $PNC
echo "=== TLS ARMS DONE $(date -Is)"
