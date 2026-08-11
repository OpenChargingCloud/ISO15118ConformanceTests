#!/usr/bin/env bash
# Decode the two consecutive CurrentDemandRes frames EVerest's EvseV2G sent on 2026-08-02,
# with an EXI codec that is not ours: V2Gdecoder (RISE-V2G + EXIficient).
#
# Both frames are lifted verbatim from
#   docs/interop-runs/2026-08-02-everest-iso2-dc-full-charge/frames.log
# entries [30] and [31], with the 8-byte V2GTP header stripped. [30] is the response that
# carries MeterInfo; [31] is the very next one, and carries none -- their meter_info_is_used
# flag is one-shot (iso_server.cpp:2048).
#
#   bash decode.sh > currentdemandres.decoded.xml
set -eu
cd "${V2GDEC_DIR:-$HOME/v2gdec/V2Gdecoder}"
JAR="${V2GDEC_JAR:-$HOME/v2gdec/decoder.jar}"

WITH=8098022c5abdfbffcffb8fd0e000000020408408827020c05000202102a1180218504e0080c50c0ac0101144452a504e582a4531323334352a3100000f44435f504f5745524d455445520001a0
WITHOUT=8098022c5abdfbffcffb8fd0e000000020408408827020c05000202102a1180218504e0080c50c0ac0101144452a504e582a4531323334352a310008

echo "########## frame [30] -- 85 B on the wire, the one carrying MeterInfo ##########"
java -jar "$JAR" -e -s "$WITH"   2>/dev/null
echo
echo "########## frame [31] -- 68 B, the very next CurrentDemandRes ##########"
java -jar "$JAR" -e -s "$WITHOUT" 2>/dev/null
