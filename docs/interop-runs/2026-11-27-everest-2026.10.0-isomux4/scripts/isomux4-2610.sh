#!/usr/bin/env bash
# isomux §4 against the 2026.10.0 release: does IsoMux pick its server chain by the trusted_ca_keys the
# car names? The 2026-08-16 design, unchanged: a second V2G root (B) with an SECC chain under it is
# installed beside the release's own (A), and four arms of our -2 DC EVCC over TLS 1.2 through the mux:
#   a   names A, trusts A   control
#   b   names B, trusts B   the question
#   c   names B, trusts A   the proof
#   a2  names A, trusts A   control, last
# Only four files are ever added to their PKI, and exactly those are removed again, whatever happens.
#   isomux4-2610.sh [arm ...]
set -u
E=/home/ahzf/everest
P=$E/dist-2610/etc/everest/certs
B=$E/pki-rootb
OUT=$E/run/2610-isomux4
C=$E/configs-2610/config-mux-tls-fwd2610.yaml
F="bash $E/fwd-2610.sh"
mkdir -p "$OUT"
ROOT_A=$P/ca/v2g/V2G_ROOT_CA.pem
ROOT_B=$B/V2G_ROOT_CA_B.pem

[ -f "$ROOT_B" ] || { echo "FAIL: no root B at $ROOT_B (run tools/interop-everest/mint-second-root.sh)"; exit 1; }
openssl verify -CAfile "$ROOT_B" -untrusted $B/CPOSubCA_B.pem $B/SECC_LEAF_B.pem || exit 1
openssl x509 -in "$ROOT_A" -noout -fingerprint -sha1 | sed 's/^/root A /'
openssl x509 -in "$ROOT_B" -noout -fingerprint -sha1 | sed 's/^/root B /'

ADDED="$P/ca/v2g/V2G_ROOT_CA_B.pem $P/client/cso/SECC_LEAF_B.pem $P/client/cso/SECC_LEAF_B.key $P/client/cso/CPO_CERT_CHAIN_B.pem"
restore() { rm -f $ADDED; echo "### PKI restored: $(ls $P/ca/v2g | tr '\n' ' ') / $(ls $P/client/cso | tr '\n' ' ')"; }
trap restore EXIT
for f in $ADDED; do [ -e "$f" ] && { echo "FAIL: $f already exists, refusing to touch their PKI"; trap - EXIT; exit 1; }; done
cp $ROOT_B $P/ca/v2g/
cp $B/SECC_LEAF_B.pem $B/SECC_LEAF_B.key $B/CPO_CERT_CHAIN_B.pem $P/client/cso/
echo "### installed: $ADDED"

WANTED=" $* "
want() { [ "$WANTED" = "  " ] || [[ "$WANTED" == *" $1 "* ]]; }
arm() {
  local name=$1 named=$2 trusted=$3
  echo "################ $name  names $(basename $named)  trusts $(basename $trusted)  $(date -Is)"
  PROTO=2 MODE=dc PROBE=muxtls $F isomux4-$name $C \
    V2G_INTEROP_TLS=1 V2G_INTEROP_TLS_TRUST=$trusted V2G_INTEROP_TRUSTED_CA_KEYS=$named 2>&1 | grep -vE "evse_security|Requesting"
  rm -rf "$OUT/$name"; cp -r $E/run/2610-fwd/isomux4-$name "$OUT/$name"
}
want a  && arm a  $ROOT_A $ROOT_A
want b  && arm b  $ROOT_B $ROOT_B
want c  && arm c  $ROOT_B $ROOT_A
want a2 && arm a2 $ROOT_A $ROOT_A
# arm a again under a root tcpdump (isomux4-capture.sh): the ClientHello names A, what does the station serve?
want acap && arm acap $ROOT_A $ROOT_A
echo "=== ISOMUX4 ARMS DONE $(date -Is)"
