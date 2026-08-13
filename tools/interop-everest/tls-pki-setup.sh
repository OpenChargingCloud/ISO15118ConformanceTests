#!/usr/bin/env bash
# Regenerate EVerest's test PKI so their station and our EVCC share one V2G root, and export the two
# files our fixture needs. Their own documented workflow (Josev's create_certs.sh); nothing is patched.
#
# ── Why a -20 TLS run cannot skip this ────────────────────────────────────────────────────────────
#
# `Evse15118D20` switches to SSL_VERIFY_PEER | SSL_VERIFY_FAIL_IF_NO_PEER_CERT the moment the client
# offers TLS 1.3, so every -20 TLS session is **mutual** — and their pristine `everest-aux` PKI ships
# no vehicle credential at all. There is nothing to present until one is generated.
#
# Nor can the material be kept between runs. `create_certs.sh` regenerates the *whole* tree, station
# leaf included, so the two sides agree only if the generated tree is installed wholesale — and the
# runs that do this restore the pristine tree afterwards, deliberately, so that a later Plug & Charge
# run is not left standing on generated material. The consequence is that each TLS run starts here.
# Measured on 2026-08-13: the credentials left from 2026-08-06 chain to V2G root `5E:77:33:20…` and the
# installed root was `88:F8:C2:D5…`, so they could not have worked.
#
#   bash tls-pki-setup.sh            # back up, regenerate, install, export, verify
#   bash tls-pki-restore.sh          # put the pristine tree back when the run is done
#
# Exports into $OUT (default ~/everest/tlsac):
#   trust.pem    V2G root + both CPO Sub-CAs — their station sends only its leaf, so the EV must
#                already hold the intermediates. Pass it as V2G_INTEROP_TLS_TRUST.
#   vehicle.p12  VEHICLE_LEAF + VehicleSubCA2 + VehicleSubCA1, password 123456.
#                Pass it as V2G_INTEROP_TLS_CLIENT=<path>:123456.
#
# **Both Sub-CAs, and that is not a detail.** The path their station has to build is
# VEHICLE_LEAF <- VehicleSubCA2 <- VehicleSubCA1 <- V2GRootCA. Ship only SubCA2 and the handshake dies
# with `tls_process_client_certificate:certificate verify failed` — correctly, and the error names the
# client certificate rather than the missing link. That cost one attempt on 2026-08-13, and the refused
# handshake then took their whole V2G event loop down with it (everest-loop-shutdown.md), so the
# station needs restarting before the next try.

set -euo pipefail

PKI="${EVEREST_PKI:-$HOME/everest/pki}"
DIST="${EVEREST_CERTS:-$HOME/everest/dist/etc/everest/certs}"
OUT="${TLS_OUT:-$HOME/everest/tlsac}"
PASS="${PKI_PASSWORD:-123456}"

[ -x "$PKI/create_certs.sh" ] || { echo "no create_certs.sh at $PKI" >&2; exit 2; }
[ -d "$DIST" ]                || { echo "no installed cert tree at $DIST" >&2; exit 2; }

mkdir -p "$OUT"
STAMP="$(date -u +%y%m%d-%H%M%S)"
BACKUP="$(dirname "$PKI")/pki-backup-tls-$STAMP.tgz"

echo "== 1. backing up the installed tree =="
tar czf "$BACKUP" -C "$(dirname "$DIST")" "$(basename "$DIST")"
echo "$BACKUP" > "$OUT/backup-path.txt"
echo "   $BACKUP"

# It reads configs/*.cnf by relative path: run from anywhere else and it fills the tree with empty
# files, cascades confusing errors downstream, and still exits 0.
echo "== 2. regenerating, from its own directory =="
( cd "$PKI" && ./create_certs.sh -v iso-20 -p "$PASS" ) > "$OUT/create_certs.log" 2>&1
echo "   $(wc -l < "$OUT/create_certs.log") lines of log"

C="$PKI/iso15118_20/certs"

echo "== 3. installing wholesale — the *_PASSWORD.txt files come with it, and the config reads them =="
rm -rf "$DIST"; mkdir -p "$DIST"; cp -a "$C/." "$DIST/"
echo "   $(find "$DIST" -type f | wc -l) files"

echo "== 4. exporting for our EVCC =="
cat "$C/ca/v2g/V2G_ROOT_CA.pem" "$C/ca/cso/CPO_SUB_CA1.pem" "$C/ca/cso/CPO_SUB_CA2.pem" > "$OUT/trust.pem"
cat "$C/ca/vehicle/VEHICLE_SUB_CA2.pem" "$C/ca/vehicle/VEHICLE_SUB_CA1.pem" > "$OUT/vehicle-subcas.pem"
openssl pkcs12 -export -out "$OUT/vehicle.p12" \
    -inkey "$C/client/vehicle/VEHICLE_LEAF.key" -passin "pass:$PASS" \
    -in    "$C/client/vehicle/VEHICLE_LEAF.pem" \
    -certfile "$OUT/vehicle-subcas.pem" -passout "pass:$PASS"
echo "   trust.pem ($(grep -c 'BEGIN CERT' "$OUT/trust.pem") certs), vehicle.p12"

echo "== 5. one root on both sides? =="
a=$(openssl x509 -in "$DIST/ca/v2g/V2G_ROOT_CA.pem" -noout -fingerprint -sha256 | sed 's/.*=//')
b=$(openssl x509 -in "$C/ca/v2g/V2G_ROOT_CA.pem"    -noout -fingerprint -sha256 | sed 's/.*=//')
printf '   station %s\n   trust   %s\n' "$a" "$b"
[ "$a" = "$b" ] || { echo "   MISMATCH — do not start the station" >&2; exit 1; }
echo "   MATCH"

echo "== 6. both chains verify to it? =="
openssl verify -CAfile "$C/ca/v2g/V2G_ROOT_CA.pem" \
    -untrusted "$C/ca/vehicle/VEHICLE_SUB_CA2.pem" -untrusted "$C/ca/vehicle/VEHICLE_SUB_CA1.pem" \
    "$C/client/vehicle/VEHICLE_LEAF.pem" | sed 's/^/   /'
openssl verify -CAfile "$C/ca/v2g/V2G_ROOT_CA.pem" \
    -untrusted "$C/ca/cso/CPO_SUB_CA1.pem" -untrusted "$C/ca/cso/CPO_SUB_CA2.pem" \
    "$C/client/cso/SECC_LEAF.pem" | sed 's/^/   /'

echo
echo "Copy $OUT/trust.pem and $OUT/vehicle.p12 somewhere the EVCC can read them,"
echo "and run tls-pki-restore.sh when you are done."
