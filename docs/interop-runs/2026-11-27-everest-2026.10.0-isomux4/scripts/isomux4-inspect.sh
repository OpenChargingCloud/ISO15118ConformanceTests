#!/usr/bin/env bash
cd /home/ahzf/everest/run/2610-isomux4 || exit 1
for a in a b c a2; do
  echo "=== $a ours"
  grep -E "Error Message|Exception|chain|certificate|TLS:|trusted_ca_keys|RemoteCertificate|name" $a/our-evcc-1.log | grep -vE "^\s+at " | sort -u | head -8 | cut -c1-260
  echo "=== $a theirs"
  sed 's/\x1b\[[0-9;]*m//g' $a/their-side.log | grep -iE "trusted_ca_keys|chain|leaf|SECC_LEAF|alert|SSL|handshake|certificate" | grep -viE "Requesting|DEBUG" | head -10 | cut -c1-260
done
