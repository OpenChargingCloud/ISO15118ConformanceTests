#!/usr/bin/env bash
# Compiles and runs probe.cpp against EVerest's own libiso15118 headers, with EVerest's own
# warning set. Exit 0 means the defect in docs/reports/everest-iso20-ac-contactor-latch.md is
# still there; exit 1 is the good news.
#
#   bash tools/everest-contactor-probe/build.sh
#   EVEREST_CORE=/path/to/everest-core bash tools/everest-contactor-probe/build.sh
#
# Needs a checkout of everest-core and a C++17 compiler. Nothing in `dotnet test` touches it.

set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
core="${EVEREST_CORE:-$HOME/everest/everest-core}"
inc="$core/lib/everest/iso15118/include"
out="${TMPDIR:-/tmp}/everest-contactor-probe"

if [ ! -d "$inc" ]; then
    echo "everest-core not found at: $core" >&2
    echo "Point EVEREST_CORE at a checkout — the headers are read, nothing is built or run from it." >&2
    exit 2
fi

echo "everest-core: $core"
if commit="$(git -C "$core" rev-parse --short HEAD 2>/dev/null)"; then
    tag="$(git -C "$core" describe --tags 2>/dev/null || echo '(untagged)')"
    echo "commit:       $commit  $tag"
    echo "the report cites b61bb12  2026.02.1"
fi
echo

# libiso15118's own flags, from lib/everest/iso15118/CMakeLists.txt:53. -Werror is the point:
# the assignment under test compiles clean under them, which is why it survived.
g++ -std=c++17 -Wall -Wextra -Wno-unused-function -Werror \
    -I "$inc" "$here/probe.cpp" -o "$out"

echo "compiled clean under -Wall -Wextra -Werror"
echo
"$out"
