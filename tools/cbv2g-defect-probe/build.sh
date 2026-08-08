#!/usr/bin/env bash
#
# Build and run the probe against libcbv2g at the commit the reports cite.
#
# Reuses the clone the app's cbv2g-ref harness already fetched, if it is there, and clones the pinned
# commit itself otherwise — so this works on a machine that has never built the reference oracle.
#
#   bash tools/cbv2g-defect-probe/build.sh                 # against the pinned commit
#   CBV2G_PROBE_REF=main bash tools/cbv2g-defect-probe/build.sh   # ...have they fixed it yet?
#
# (libcbv2g's default branch is `main`. As of 2026-08-08 it is still the pinned commit.)
#
# Exit 0 means every claim in the report still held. Which is worth reading twice: against `master`,
# a **non-zero exit is the good news** — it means one of the three has been fixed upstream and the
# report can be closed. The probe asserts the presence of defects, so it is meant to stop passing.
#
set -euo pipefail

PIN=03350be048b35b179905129005a97144a4bdcf93
REF="${CBV2G_PROBE_REF:-$PIN}"
HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="${CBV2G_PROBE_BUILD:-$HOME/cbv2g-defect-probe}"
SHARED="$HOME/cbv2g-ref-build/_deps/libcbv2g-src"

mkdir -p "$WORK"

if [ "$REF" = "$PIN" ] && [ -d "$SHARED/lib/cbv2g" ] &&
   [ "$(git -C "$SHARED" rev-parse HEAD 2>/dev/null)" = "$PIN" ]; then
    SRC="$SHARED"
    echo "using the existing clone: $SRC"
else
    SRC="$WORK/libcbv2g"
    if [ ! -d "$SRC/.git" ]; then
        git clone -q https://github.com/EVerest/libcbv2g.git "$SRC"
    fi
    git -C "$SRC" fetch -q origin
    # A branch name has to resolve through the remote; a SHA is checked out directly. Say which ref
    # was asked for when neither works — "pathspec did not match" on its own sends you looking in the
    # wrong repository, as it did here when `master` was tried against a project whose branch is `main`.
    if ! git -C "$SRC" checkout -q "origin/$REF" 2>/dev/null &&
       ! git -C "$SRC" checkout -q "$REF"        2>/dev/null; then
        echo "no such ref in EVerest/libcbv2g: '$REF' (its default branch is 'main')" >&2
        exit 2
    fi
fi

actual=$(git -C "$SRC" rev-parse HEAD)
echo "libcbv2g $actual${actual:+ }($REF)"
if [ "$actual" != "$PIN" ]; then
    echo "note: this is not the commit the report cites ($PIN) — line and id citations may have moved."
fi
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
