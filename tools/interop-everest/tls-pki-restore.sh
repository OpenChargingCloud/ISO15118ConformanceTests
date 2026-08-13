#!/usr/bin/env bash
# Put EVerest's pristine test PKI back after a TLS run.
#
# Why it matters that this is a separate, run-it-every-time step: `tls-pki-setup.sh` replaces their
# whole certificate tree, station leaf included. Leaving the generated tree in place means every later
# run — Plug & Charge above all — is standing on material this harness minted, which is exactly the
# thing a conformance harness must not quietly do. The 2026-08-06 run established the discipline; this
# script is it, so that "restore afterwards" stops being a sentence in a run note.
#
#   bash tls-pki-restore.sh                       # newest backup recorded by tls-pki-setup.sh
#   bash tls-pki-restore.sh <backup.tgz>          # a specific one
#
# Prints the V2G root fingerprint at the end. `88:F8:C2:D5…` is the pristine everest-aux root; anything
# else means the tree is still generated material and the restore did not do what you think.

set -euo pipefail

DIST="${EVEREST_CERTS:-$HOME/everest/dist/etc/everest/certs}"
OUT="${TLS_OUT:-$HOME/everest/tlsac}"

BACKUP="${1:-}"
if [ -z "$BACKUP" ]; then
    [ -f "$OUT/backup-path.txt" ] || {
        echo "no backup path recorded at $OUT/backup-path.txt — pass one explicitly" >&2
        exit 2
    }
    BACKUP="$(cat "$OUT/backup-path.txt")"
fi
[ -f "$BACKUP" ] || { echo "no such backup: $BACKUP" >&2; exit 2; }

echo "restoring $DIST from $BACKUP"
rm -rf "$DIST"
tar xzf "$BACKUP" -C "$(dirname "$DIST")"

echo "$(find "$DIST" -type f | wc -l) files"
openssl x509 -in "$DIST/ca/v2g/V2G_ROOT_CA.pem" -noout -subject -fingerprint -sha256
