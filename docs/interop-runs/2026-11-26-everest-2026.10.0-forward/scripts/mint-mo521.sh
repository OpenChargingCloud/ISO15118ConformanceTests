#!/usr/bin/env bash
# A P-521 MO hierarchy that meets the -20 contract profile Evse15118D20 (2026.10.0) checks in
# d20/crypto.cpp profile_fault(): secp521r1 + ecdsa-with-SHA512 on leaf and every sub-CA, critical
# KeyUsage and BasicConstraints, sub-CA2 pathLen 0 / sub-CA1 pathLen 1, CRLDistributionPoints, no EKU on
# the leaf, subject CN (EMAID) + O only. Their own MO test PKI is P-256 throughout, so it cannot pass.
# Throwaway test material in the WSL run dir; nothing of theirs is modified.
set -eu
E=/home/ahzf/everest
O=$E/run/2610-fwd/mo521
D=$E/dist-2610/etc/everest/certs
rm -rf "$O"; mkdir -p "$O"; cd "$O"

CRL="crlDistributionPoints=URI:http://crl.invalid/mo521.crl"
cat > root.ext <<EOF
basicConstraints=critical,CA:TRUE
keyUsage=critical,keyCertSign,cRLSign
subjectKeyIdentifier=hash
EOF
cat > sub1.ext <<EOF
basicConstraints=critical,CA:TRUE,pathlen:1
keyUsage=critical,keyCertSign,cRLSign
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid
$CRL
EOF
cat > sub2.ext <<EOF
basicConstraints=critical,CA:TRUE,pathlen:0
keyUsage=critical,keyCertSign,cRLSign
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid
$CRL
EOF
cat > leaf.ext <<EOF
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid
$CRL
EOF

key() { openssl ecparam -name secp521r1 -genkey -noout -out "$1.key"; }
key root; key sub1; key sub2; key leaf

openssl req -new -x509 -key root.key -sha512 -days 30 -subj "/CN=MO521 Root (test)/O=ISO15118ConformanceTests" \
  -extensions v3 -config <(printf '[req]\ndistinguished_name=dn\n[dn]\n[v3]\n'; cat root.ext) -out root.pem
sign() { # name issuer subject
  openssl req -new -key "$1.key" -sha512 -subj "$3" -out "$1.csr"
  openssl x509 -req -in "$1.csr" -CA "$2.pem" -CAkey "$2.key" -CAcreateserial -sha512 -days 20 \
    -extfile "$1.ext" -out "$1.pem" 2>/dev/null
}
sign sub1 root "/CN=MO521 Sub-CA1 (test)/O=ISO15118ConformanceTests"
sign sub2 sub1 "/CN=MO521 Sub-CA2 (test)/O=ISO15118ConformanceTests"
sign leaf sub2 "/CN=DEOCCC521000001/O=ISO15118ConformanceTests eMSP (test)"

openssl verify -x509_strict -CAfile root.pem -untrusted <(cat sub2.pem sub1.pem) leaf.pem
# Sub-CA2 first: our loader puts every certificate without a key into SubCertificates in file order,
# and their profile check expects index 0 to be the one nearest the leaf (pathLen 0).
cat sub2.pem sub1.pem > subs.pem
openssl pkcs12 -export -inkey leaf.key -in leaf.pem -certfile subs.pem -passout pass:123456 -out contract521.p12
# The station's MO trust: their own root plus this one, as one bundle for EvseSecurity's mo_ca_bundle.
cat "$D/ca/mo/MO_ROOT_CA.pem" root.pem > mo-bundle.pem

for c in leaf sub2 sub1; do
  printf '%-5s ' $c; openssl x509 -in $c.pem -noout -text | grep -E "Signature Algorithm|ASN1 OID|pathlen|CA:" | sort -u | tr -s ' ' | tr '\n' ' '; echo
done
openssl pkcs12 -in contract521.p12 -passin pass:123456 -nokeys 2>/dev/null | grep subject=
