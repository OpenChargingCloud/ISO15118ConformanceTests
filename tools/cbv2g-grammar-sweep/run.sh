#!/usr/bin/env bash
#
# Sweep every grammar cbexigen generated, and check it three ways.
#
#   bash tools/cbv2g-grammar-sweep/run.sh                       # against the pinned commit
#   CBV2G_SWEEP_REF=main bash tools/cbv2g-grammar-sweep/run.sh   # ...has anything moved?
#
# Two of the three checks need nothing but the libcbv2g checkout, so anyone can re-run them. The
# third holds the grammars against ISO's schemas and runs only where those are in place — see
# `libs/EVSimulatorApp/libs/WWCP_ISO15118/tools/download-schemas.sh`. It says so and carries on
# when they are not.
#
# Unlike `tools/cbv2g-defect-probe/`, this exits 0 whatever it finds: it is a measurement, not an
# assertion. Read the counts.
#
set -uo pipefail

PIN=03350be048b35b179905129005a97144a4bdcf93
REF="${CBV2G_SWEEP_REF:-$PIN}"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
WORK="${CBV2G_SWEEP_BUILD:-$HOME/cbv2g-grammar-sweep}"
OUT="${1:-$WORK/out}"
STACK="$ROOT/libs/EVSimulatorApp/libs/WWCP_ISO15118"

mkdir -p "$WORK" "$OUT"

SRC="$WORK/libcbv2g"
if [ ! -d "$SRC/.git" ]; then
    git clone -q https://github.com/EVerest/libcbv2g.git "$SRC"
fi
git -C "$SRC" fetch -q origin
if ! git -C "$SRC" checkout -q "origin/$REF" 2>/dev/null &&
   ! git -C "$SRC" checkout -q "$REF"        2>/dev/null; then
    echo "no such ref in EVerest/libcbv2g: '$REF' (its default branch is 'main')" >&2
    exit 2
fi

actual=$(git -C "$SRC" rev-parse HEAD)
echo "libcbv2g $actual ($REF)"
[ "$actual" != "$PIN" ] && echo "note: not the commit the reports cite ($PIN); ids and line numbers may have moved."
echo

echo "=== 1/3  the generated state machines, against themselves ==="
python3 "$HERE/cbsweep.py" "$SRC" --json "$OUT/sweep.json" | tee "$OUT/sweep.txt" | grep -E '^(parsed|### )'
echo

echo "=== 2/3  the document element codes, against EXI 1.0 §8.5.1 ==="
python3 "$HERE/cbdoc.py" "$SRC" > "$OUT/doc-order.txt"
grep -E '^==|EXI .8.5.1 order|order by type name' "$OUT/doc-order.txt"
echo

echo "=== 3/3  the content models, against ISO's schemas ==="
if [ -f "$STACK/WWCP_ISO15118_2/Schemas/V2G_CI_MsgBody.xsd" ]; then
    python3 "$HERE/cbschema.py" "$SRC" "$STACK" --json "$OUT/schema.json" \
        | tee "$OUT/schema.txt" | grep -E '^(checked|### |  \S)'
else
    echo "  skipped: no schemas in $STACK — run libs/EVSimulatorApp/libs/WWCP_ISO15118/tools/download-schemas.sh"
fi
echo
echo "full output in $OUT"
