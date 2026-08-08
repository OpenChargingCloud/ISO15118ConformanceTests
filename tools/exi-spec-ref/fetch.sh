#!/usr/bin/env bash
#
# Re-fetch the W3C EXI 1.0 (Second Edition) Recommendation and its four figures, and check them
# against the checksums recorded next to this script.
#
# The copy in this directory is checked in on purpose — see README.md for why, and for the licence
# that permits it. This script exists so the copy can be *verified* and refreshed, not because the
# repository fetches anything at run time. Nothing in `dotnet test` reads any of it.
#
#   bash tools/exi-spec-ref/fetch.sh          # verify what is checked in
#   bash tools/exi-spec-ref/fetch.sh --update # overwrite it and rewrite SHA256SUMS
#
set -euo pipefail

BASE="https://www.w3.org/TR/exi"
HERE="$(cd "$(dirname "$0")" && pwd)"
DOC="exi-1.0-second-edition.html"
FIGURES=(channels.png compression.png eventCodeTree.png restrictedCharset.png)

update=false
[ "${1:-}" = "--update" ] && update=true

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

curl -sSL --fail -o "$work/$DOC" "$BASE/"
for figure in "${FIGURES[@]}"; do
    curl -sSL --fail -o "$work/$figure" "$BASE/$figure"
done

if $update; then
    cp "$work/$DOC" "$HERE/$DOC"
    for figure in "${FIGURES[@]}"; do cp "$work/$figure" "$HERE/$figure"; done
    ( cd "$HERE" && sha256sum "$DOC" "${FIGURES[@]}" > SHA256SUMS )
    echo "updated: $HERE"
    echo "Read the diff before committing — a Recommendation changing under you is itself news."
else
    ( cd "$work" && cp "$HERE/SHA256SUMS" . && sha256sum -c SHA256SUMS )
    echo "the checked-in copy is byte-identical to $BASE/ as served today"
fi
