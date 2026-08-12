#!/usr/bin/env bash
# Mint a SECOND V2G root plus a SECC leaf chain under it, in EVerest's test-PKI style,
# so a -20 station can be given two roots and asked which chain it presents.
#
# Nothing here touches the existing PKI: everything lands in $OUT, and installing it
# is a separate, explicit step.
set -euo pipefail

OUT=${OUT:-/home/ahzf/everest/pki-rootb}
CURVE=prime256v1          # matches their test PKI (see josev-iso20-pki-curve.md)
DAYS=3650

rm -rf "$OUT"; mkdir -p "$OUT"
cd "$OUT"

mkext() {
  cat > "$1" <<EOF
[ca]
basicConstraints = critical, CA:TRUE
keyUsage         = critical, keyCertSign, cRLSign
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid:always

[leaf]
basicConstraints = critical, CA:FALSE
keyUsage         = critical, digitalSignature, keyAgreement
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid:always
EOF
}
mkext ext.cnf

# --- root B -----------------------------------------------------------------
openssl ecparam -name "$CURVE" -genkey -noout -out V2G_ROOT_CA_B.key
openssl req -new -x509 -sha256 -days "$DAYS" \
  -key V2G_ROOT_CA_B.key -out V2G_ROOT_CA_B.pem \
  -subj "/CN=V2GRootCA-B/O=EVerest/C=DE/DC=V2G" \
  -extensions ca -config ext.cnf 2>/dev/null

# --- sub CA under root B ----------------------------------------------------
openssl ecparam -name "$CURVE" -genkey -noout -out CPOSubCA_B.key
openssl req -new -sha256 -key CPOSubCA_B.key -out CPOSubCA_B.csr \
  -subj "/CN=CPOSubCA-B/O=EVerest/C=DE/DC=V2G"
openssl x509 -req -sha256 -days "$DAYS" -in CPOSubCA_B.csr \
  -CA V2G_ROOT_CA_B.pem -CAkey V2G_ROOT_CA_B.key -CAcreateserial \
  -out CPOSubCA_B.pem -extfile ext.cnf -extensions ca 2>/dev/null

# --- SECC leaf under sub CA B ----------------------------------------------
openssl ecparam -name "$CURVE" -genkey -noout -out SECC_LEAF_B.key
openssl req -new -sha256 -key SECC_LEAF_B.key -out SECC_LEAF_B.csr \
  -subj "/CN=SECCCert-B/O=EVerest/C=DE/DC=CPO"
openssl x509 -req -sha256 -days "$DAYS" -in SECC_LEAF_B.csr \
  -CA CPOSubCA_B.pem -CAkey CPOSubCA_B.key -CAcreateserial \
  -out SECC_LEAF_B.pem -extfile ext.cnf -extensions leaf 2>/dev/null

cat SECC_LEAF_B.pem CPOSubCA_B.pem > CPO_CERT_CHAIN_B.pem

echo "--- root B ---"
openssl x509 -in V2G_ROOT_CA_B.pem -noout -subject -issuer
echo "--- chain B ---"
openssl crl2pkcs7 -nocrl -certfile CPO_CERT_CHAIN_B.pem | openssl pkcs7 -print_certs -noout
echo "--- verify ---"
openssl verify -CAfile V2G_ROOT_CA_B.pem -untrusted CPOSubCA_B.pem SECC_LEAF_B.pem
