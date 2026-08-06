#!/bin/bash
# Regenerate eVDriveFlow's PKI with their own generateCertificates.sh.
#
# Why: the material they ship expired long ago — seccCert on 2022-10-06 (60 days, which is what ISO
# 15118 asks of a SECC leaf), cpoSubCA2 in 2023, and cpoSubCA1 on 2026-08-06. Their EV rejects our
# handshake with CERTIFICATE_VERIFY_FAILED / "certificate has expired", correctly.
#
# Everything below is theirs: their script, their configs, their passphrase, secp521r1 throughout.
set -eu
cd /home/ahzf/edf/eVDriveFlow/shared/certificates

bash generateCertificates.sh > /home/ahzf/edf/pki-regen.log 2>&1 || {
    echo "generator failed; tail:"; tail -15 /home/ahzf/edf/pki-regen.log; exit 1; }

echo "=== fresh validity:"
echo -n "seccCert    "; openssl x509 -in certs/seccCert.pem       -noout -enddate
echo -n "cpoSubCA2   "; openssl x509 -in certs/cpoSubCA2Cert.pem  -noout -enddate
echo -n "cpoSubCA1   "; openssl x509 -in certs/cpoSubCA1Cert.pem  -noout -enddate
echo -n "v2gRootCA   "; openssl x509 -in certs/v2gRootCACert.pem  -noout -enddate
echo -n "vehicleCert "; openssl x509 -in certs/vehicleCert.pem    -noout -enddate

# our station serves their SECC chain
openssl pkcs12 -export -inkey privateKeys/secc.key -passin pass:123456789abcdefgh \
    -in certs/seccCertChain.pem -name edf-secc -passout pass:interop -out /home/ahzf/edf/secc.pfx
echo "=== wrote /home/ahzf/edf/secc.pfx"
openssl pkcs12 -in /home/ahzf/edf/secc.pfx -passin pass:interop -nokeys 2>/dev/null | grep -c "subject="
