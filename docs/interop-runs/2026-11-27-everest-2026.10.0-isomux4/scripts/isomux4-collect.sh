#!/usr/bin/env bash
set -u
E=/home/ahzf/everest
R=$E/run/2610-isomux4
SP=/mnt/c/Users/achim/AppData/Local/Temp/claude/443da193b49d45a8/b340fbb5-0a48-4a7c-bb63-fd8aec8ece2e/scratchpad/repro
OUT=/mnt/d/Coding/OpenChargingCloud/ct-isomux4/docs/interop-runs/2026-11-27-everest-2026.10.0-isomux4
strip() { sed 's/\x1b\[[0-9;]*m//g'; }
mkdir -p "$OUT/scripts" "$OUT/arms" "$OUT/capture"
cp $SP/isomux4-2610.sh $SP/isomux4-capture.sh $SP/isomux4-decode.sh $SP/isomux4-inspect.sh $SP/isomux4-code.sh \
   $SP/tls-handshake.py $SP/isomux4-collect.sh "$OUT/scripts/"
cp $E/run/2610-isomux4.log "$OUT/arms/batch.log" 2>/dev/null
for a in a b c a2 acap; do
  d=$R/$a; [ -d "$d" ] || continue
  grep -vE "^\s*$" $d/our-evcc-1.log | grep -vE "^\s+at |Microsoft|Build started|NUnit Adapter|Test run for|VSTest" > "$OUT/arms/$a.ours.txt"
  strip < $d/their-side.log | grep -E "iso_mux|evse_security:E  :: Found valid leaf|Not requiring all valid leafs|Could not find owner|Root required" | grep -v DEBUG | cut -c1-400 > "$OUT/arms/$a.their-side.txt"
  f=$(ls $d/rec-1/*.flow.md 2>/dev/null | head -1); [ -n "$f" ] && cp "$f" "$OUT/arms/$a.flow.md"
done
C=$R/capture-a
cp $C/capture.pcap "$OUT/capture/acap.pcap"
cp $C/handshake.txt "$OUT/capture/acap.handshake.txt"
cp $C/acap.served-chain.pem "$OUT/capture/acap.served-chain.pem"
bash $SP/isomux4-decode.sh > "$OUT/capture/acap.decoded.txt" 2>&1
# the release code the finding rests on, as of the tag
K=$E/core-2610
{
  echo "# core-2610 (74845c578)"
  echo "## modules/EVSE/IsoMux/connection/tls_connection.cpp:293-311 — one leaf, no trust anchor"
  sed -n 293,311p $K/modules/EVSE/IsoMux/connection/tls_connection.cpp
  echo "## lib/everest/tls/src/tls.cpp:1380-1416 — anchors decide whether trusted_ca_keys runs at all"
  sed -n 1380,1416p $K/lib/everest/tls/src/tls.cpp
  echo "## modules/EVSE/Evse15118D20/charger/evse_security_ssl_config.cpp:70-80 — the -20 module sets one per chain"
  sed -n 70,80p $K/modules/EVSE/Evse15118D20/charger/evse_security_ssl_config.cpp
} > "$OUT/release-source.txt"
du -sh "$OUT"; ls -R "$OUT" | head -40
