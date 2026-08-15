#!/usr/bin/env bash
# The two things the reverse Plug & Charge scripts assume and never built: the TLS server credential
# their car will trust, and the trust-root directories the arm and its control differ by. Both had to be
# reconstructed from scratch on 2026-08-15 because /tmp does not survive a reboot and the run notes only
# named the files -- so they live here now.
#
#   pnc-chain-setup.sh [path-to-josev-clone]        (default: ~/josev-src)
#
# Writes /tmp/secc.p12, /tmp/evcc_config_dc_tls.json and four /tmp/josev-roots-* directories.
#
# Josev resolves every certificate under iso15118_2/certs/ whatever the protocol -- hard-coded, with
# their own TODO above it (shared/security.py:1445) -- so one PKI serves the -2 and the -20 arm alike.
set -euo pipefail

josev="${1:-$HOME/josev-src}"
pki="$josev/iso15118/shared/pki/iso15118_2"
certs="$pki/certs"
keys="$pki/private_keys"
pw=12345

[ -f "$certs/seccLeafCert.pem" ] || {
  echo "No PKI at $certs — run:  (cd $josev/iso15118/shared/pki && ./create_certs.sh -v iso-2)" >&2
  exit 1
}

# Our station presents their SECC leaf and both CPO Sub-CAs, which is what their car's V2G root accepts.
# -legacy because their keys are RC2/3DES-wrapped and OpenSSL 3 refuses those without it.
cat "$certs/cpoSubCA2Cert.pem" "$certs/cpoSubCA1Cert.pem" > /tmp/cpoSubCAs.pem
openssl pkcs12 -export -legacy \
  -inkey "$keys/seccLeaf.key" -passin "pass:$pw" \
  -in "$certs/seccLeafCert.pem" -certfile /tmp/cpoSubCAs.pem \
  -passout "pass:$pw" -out /tmp/secc.p12

# Directories rather than files, because the -20 arm needs two anchors at once: their car's TLS client
# certificate is OEM-rooted and its contract certificate is MO-rooted, and one store holds both. The
# control drops the MO root ONLY -- take the OEM root out too and the handshake changes instead of the
# contract check, which is a different experiment.
for d in mo v2g oem oem-mo; do rm -rf "/tmp/josev-roots-$d"; mkdir -p "/tmp/josev-roots-$d"; done
cp "$certs/moRootCACert.pem"  /tmp/josev-roots-mo/
cp "$certs/v2gRootCACert.pem" /tmp/josev-roots-v2g/
cp "$certs/oemRootCACert.pem" /tmp/josev-roots-oem/
cp "$certs/oemRootCACert.pem" "$certs/moRootCACert.pem" /tmp/josev-roots-oem-mo/

# Their -20 DC example with TLS switched on; the -2 arm uses evcc_config_pnc_ac.json from the image.
sed 's/"useTls": false/"useTls": true/' \
  "$josev/iso15118/shared/examples/evcc/iso15118_20/evcc_config_dc.json" \
  > /tmp/evcc_config_dc_tls.json

echo "/tmp/secc.p12                 $(openssl x509 -in "$certs/seccLeafCert.pem" -noout -subject | sed 's/subject=//')"
for d in /tmp/josev-roots-*; do
  printf '%-28s %s\n' "$d" "$(ls "$d" | tr '\n' ' ')"
done
cat <<'DONE'

Then, per arm:
  TRUST_ROOTS=/tmp/josev-roots-mo      ./reverse-iso2-pnc-tls-sdp.sh   # -2  arm
  TRUST_ROOTS=/tmp/josev-roots-v2g     ./reverse-iso2-pnc-tls-sdp.sh   # -2  control
  TRUST_ROOTS=/tmp/josev-roots-oem-mo  ./reverse-pnc-tls-sdp.sh        # -20 arm
  TRUST_ROOTS=/tmp/josev-roots-oem     ./reverse-pnc-tls-sdp.sh        # -20 control
DONE
