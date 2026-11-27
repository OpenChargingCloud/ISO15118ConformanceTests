#!/usr/bin/env bash
# Decode the acap capture: what the car named, what the station served, and which root that chain verifies under.
set -u
E=/home/ahzf/everest
D=$E/run/2610-isomux4/capture-a
SP=/mnt/c/Users/achim/AppData/Local/Temp/claude/443da193b49d45a8/b340fbb5-0a48-4a7c-bb63-fd8aec8ece2e/scratchpad/repro
ROOT_A=$E/dist-2610/etc/everest/certs/ca/v2g/V2G_ROOT_CA.pem
ROOT_B=$E/pki-rootb/V2G_ROOT_CA_B.pem
echo "root A sha1 $(openssl x509 -in $ROOT_A -noout -fingerprint -sha1 | cut -d= -f2 | tr -d : | tr A-F a-f)"
echo "root B sha1 $(openssl x509 -in $ROOT_B -noout -fingerprint -sha1 | cut -d= -f2 | tr -d : | tr A-F a-f)"
python3 -I $SP/tls-handshake.py $D/capture.pcap 64110 $D/acap | tee $D/handshake.txt
echo "--- served chain"
openssl crl2pkcs7 -nocrl -certfile $D/acap.served-chain.pem | openssl pkcs7 -print_certs -noout | grep subject
csplit -s -z -f $D/served- $D/acap.served-chain.pem '/-----BEGIN CERTIFICATE-----/' '{*}'
LEAF=$D/served-00; tail -n +1 $D/served-0[1-9] > $D/served-intermediates.pem 2>/dev/null
echo "under root A (the one the car named): $(openssl verify -CAfile $ROOT_A -untrusted $D/served-intermediates.pem $LEAF 2>&1 | tail -1)"
echo "under root B (not named):             $(openssl verify -CAfile $ROOT_B -untrusted $D/served-intermediates.pem $LEAF 2>&1 | tail -1)"
