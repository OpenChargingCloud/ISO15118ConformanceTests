#!/bin/bash
#
# Bring up V2Gdecoder as a second, cbV2G-independent EXI oracle. See README.md.
#
# No Docker, unlike their own tools/docker/decoder: the release jar is self-contained and only
# needs a JRE plus ./schemas in the working directory. That also keeps the run reproducible —
# their Dockerfile clones master and downloads the same jar, so the image adds nothing but a
# daemon that has to be started by hand on this rig anyway.
#
#   bash tools/interop-v2gdecoder/setup.sh          # idempotent; re-run to verify
#   V2GDEC_HOME=<dir> bash …/setup.sh               # somewhere other than ~/v2gdec
#
set -eu

HOME_DIR="${V2GDEC_HOME:-$HOME/v2gdec}"
REPO="$HOME_DIR/V2Gdecoder"
JAR="$HOME_DIR/decoder.jar"

command -v java >/dev/null || { echo "no java on PATH (any JRE 8+; tested on OpenJDK 21)"; exit 1; }

mkdir -p "$HOME_DIR"

if [ ! -d "$REPO/.git" ]; then
  echo "== cloning V2Gdecoder — for its schemas, which is the part the jar cannot carry"
  git clone --depth 1 https://github.com/FlUxIuS/V2Gdecoder "$REPO"
else
  echo "== V2Gdecoder already cloned at $REPO"
fi

if [ ! -f "$JAR" ]; then
  echo "== fetching the v1.1 release jar (the one their own Dockerfile pulls)"
  curl -fsSL -o "$JAR" \
    https://github.com/FlUxIuS/V2Gdecoder/releases/download/v1.1/V2Gdecoder-jar-with-dependencies.jar
fi

echo "== jar:     $(ls -l "$JAR" | awk '{print $5" bytes"}')"
echo "== schemas: $(ls -1 "$REPO/schemas"/*.xsd | wc -l) XSDs — SAP, ISO 15118-2:2013, DIN 70121"

# Smoke test with a frame from their own README, so a failure here is theirs and not ours.
# cd matters: V2Gdecoder resolves ./schemas relative to the working directory, not to the jar.
echo "== smoke test"
cd "$REPO"
if java -jar "$JAR" -e -s 8000DBAB9371D3234B71D1B981899189D191818991D26B9B3A232B300200000000\
01D75726E3A69736F3A31353131383A323A323031333A4D73674465660040000080880 2>/dev/null \
     | grep -q supportedAppProtocol; then
  echo "   ok — decodes a SupportedAppProtocolReq"
else
  echo "   FAILED — the oracle is not usable; check java and \$REPO/schemas"
  exit 1
fi

echo
echo "Ready. Run the corpus through it with:"
echo "   python3 tools/interop-v2gdecoder/roundtrip.py <vectors-or-trace>.json"
