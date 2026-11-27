#!/usr/bin/env bash
# MeteringConfirmation over -20 TLS, our station against our car (2026-11-27).
#
# 1. A throwaway P-521 contract hierarchy to the -20 contract profile, the same recipe as
#    ../../2026-11-26-everest-2026.10.0-forward/scripts/mint-mo521.sh, minus EVerest's trust bundle.
# 2. The stack's SECC with the BouncyCastle backend and --pki-dir: TLS 1.3 only, a P-521 V2G hierarchy it
#    mints, mutual TLS with the car's leaf pinned. A signing meter, a confirmation asked for after every
#    charge-loop response from the second on, and an invalid one refused.
# 3. The stack's EVCC with the same --pki-dir (it pins the station's leaf) and the P-521 contract.
#
# Run from libs/EVSimulatorApp/libs/WWCP_ISO15118 after a Release build. Throwaway material only; the
# output directory holds private keys, so keep it out of the repository.
set -eu
OUT=${OUT:-/tmp/mctls}
PORT=${PORT:-15221}
export MSYS_NO_PATHCONV=1
# With path conversion off, Git Bash hands /tmp/... to .NET and to its openssl verbatim, and neither
# reads it. C:/... works for both and for the shell, so on Windows use that form throughout.
OUT=$(cygpath -m "$OUT" 2>/dev/null || echo "$OUT")

rm -rf "$OUT"; mkdir -p "$OUT/mo521" "$OUT/v2gpki"
( cd "$OUT/mo521"
  CRL="crlDistributionPoints=URI:http://crl.invalid/mo521.crl"
  printf 'basicConstraints=critical,CA:TRUE\nkeyUsage=critical,keyCertSign,cRLSign\nsubjectKeyIdentifier=hash\n' > root.ext
  printf "basicConstraints=critical,CA:TRUE,pathlen:1\nkeyUsage=critical,keyCertSign,cRLSign\nsubjectKeyIdentifier=hash\nauthorityKeyIdentifier=keyid\n$CRL\n" > sub1.ext
  printf "basicConstraints=critical,CA:TRUE,pathlen:0\nkeyUsage=critical,keyCertSign,cRLSign\nsubjectKeyIdentifier=hash\nauthorityKeyIdentifier=keyid\n$CRL\n" > sub2.ext
  printf "basicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature\nsubjectKeyIdentifier=hash\nauthorityKeyIdentifier=keyid\n$CRL\n" > leaf.ext
  for k in root sub1 sub2 leaf; do openssl ecparam -name secp521r1 -genkey -noout -out $k.key; done
  printf '[req]\ndistinguished_name=dn\n[dn]\n[v3]\n' > root.cnf; cat root.ext >> root.cnf
  openssl req -new -x509 -key root.key -sha512 -days 30 -subj "/CN=MO521 Root (test)/O=ISO15118ConformanceTests" \
    -extensions v3 -config root.cnf -out root.pem
  sign() { openssl req -new -key $1.key -sha512 -subj "$3" -out $1.csr
           openssl x509 -req -in $1.csr -CA $2.pem -CAkey $2.key -CAcreateserial -sha512 -days 20 -extfile $1.ext -out $1.pem 2>/dev/null; }
  sign sub1 root "/CN=MO521 Sub-CA1 (test)/O=ISO15118ConformanceTests"
  sign sub2 sub1 "/CN=MO521 Sub-CA2 (test)/O=ISO15118ConformanceTests"
  sign leaf sub2 "/CN=DEOCCC521000001/O=ISO15118ConformanceTests eMSP (test)"
  cat sub2.pem sub1.pem > subs.pem       # Sub-CA2 first: leaf-issuer first, as SubCertificates wants
  openssl verify -x509_strict -CAfile root.pem -untrusted subs.pem leaf.pem
  openssl pkcs12 -export -inkey leaf.key -in leaf.pem -certfile subs.pem -passout pass:123456 -out contract521.p12 )

timeout 120 dotnet WWCP_ISO15118_SECC/bin/Release/net11.0/WWCP_ISO15118_SECC.dll \
  --listen "$PORT" --protocol 20 --mode dc --tls-backend bc --pki-dir "$OUT/v2gpki" \
  --meter "VAN*M1" --metering-confirmation 1 --require-metering-confirmation > "$OUT/secc.log" 2>&1 &
until grep -q "SECC listening" "$OUT/secc.log" 2>/dev/null; do sleep 0.2; done

timeout 90 dotnet WWCP_ISO15118_EVCC/bin/Release/net11.0/WWCP_ISO15118_EVCC.dll \
  --connect "[::1]:$PORT" --protocol 20 --mode dc --tls-backend bc --pki-dir "$OUT/v2gpki" \
  --contract-cert "$OUT/mo521/contract521.p12" --contract-cert-pass 123456 | tee "$OUT/evcc.log"
wait
cat "$OUT/secc.log"
