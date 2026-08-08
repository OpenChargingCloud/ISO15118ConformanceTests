#!/usr/bin/env bash
#
# Build and run the probe against libcbv2g at the commit the reports cite.
#
# Reuses the clone the app's cbv2g-ref harness already fetched, if it is there, and clones the pinned
# commit itself otherwise — so this works on a machine that has never built the reference oracle.
#
#   bash tools/cbv2g-defect-probe/build.sh
#
# Exit 0 means every claim in issue C of docs/reports/libcbv2g-grammar-deviations.md held.
#
set -euo pipefail

PIN=03350be048b35b179905129005a97144a4bdcf93
HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="${CBV2G_PROBE_BUILD:-$HOME/cbv2g-defect-probe}"
SHARED="$HOME/cbv2g-ref-build/_deps/libcbv2g-src"

mkdir -p "$WORK"

if [ -d "$SHARED/lib/cbv2g" ]; then
    SRC="$SHARED"
    echo "using the existing clone: $SRC"
else
    SRC="$WORK/libcbv2g"
    if [ ! -d "$SRC/lib/cbv2g" ]; then
        echo "cloning libcbv2g at $PIN"
        git clone -q https://github.com/EVerest/libcbv2g.git "$SRC"
        git -C "$SRC" checkout -q "$PIN"
    fi
fi

actual=$(git -C "$SRC" rev-parse HEAD)
if [ "$actual" != "$PIN" ]; then
    echo "WARNING: the clone is at $actual, not the pinned $PIN — the citations may not line up." >&2
fi
echo "libcbv2g $actual"
echo

cc -std=c99 -O1 -Wall -Wextra -Wno-unused-parameter \
   -I"$SRC/include" \
   -o "$WORK/probe" \
   "$HERE/probe.c" \
   "$SRC/lib/cbv2g/common/exi_basetypes.c" \
   "$SRC/lib/cbv2g/common/exi_basetypes_encoder.c" \
   "$SRC/lib/cbv2g/common/exi_bitstream.c" \
   "$SRC/lib/cbv2g/common/exi_header.c" \
   "$SRC/lib/cbv2g/common/exi_basetypes_decoder.c" \
   "$SRC/lib/cbv2g/common/exi_types_decoder.c" \
   "$SRC/lib/cbv2g/iso_20/iso20_WPT_Datatypes.c" \
   "$SRC/lib/cbv2g/iso_20/iso20_WPT_Encoder.c" \
   "$SRC/lib/cbv2g/iso_20/iso20_ACDP_Datatypes.c" \
   "$SRC/lib/cbv2g/iso_20/iso20_ACDP_Encoder.c" \
   "$SRC/lib/cbv2g/iso_20/iso20_ACDP_Decoder.c"

"$WORK/probe"
