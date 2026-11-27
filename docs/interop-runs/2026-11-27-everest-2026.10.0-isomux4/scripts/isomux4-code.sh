#!/usr/bin/env bash
# IsoMux's trusted_ca_keys lines per arm, and the 2026.10.0 code that prints them.
cd /home/ahzf/everest/run/2610-isomux4 || exit 1
for a in a b c a2; do
  echo "== $a"
  sed 's/\x1b\[[0-9;]*m//g' $a/their-side.log | grep iso_mux | grep -iE "trusted_ca_keys|alert|No trust anchors" | cut -c60-250 | head -5
done
C=/home/ahzf/everest/core-2610/lib/everest/tls/src/tls.cpp
echo "== tls.cpp around the boot warning"
sed -n 1370,1415p $C
echo "== where the per-connection line comes from"
grep -n "no configured chain matched\|serving default chain\|trusted_ca_keys" $C | head -20
echo "== IsoMux: how many chains it is given"
grep -rn "get_all_valid_certificates_info\|get_leaf_certificate_info\|include_all\|chains\." /home/ahzf/everest/core-2610/modules/EVSE/IsoMux/*.cpp /home/ahzf/everest/core-2610/modules/EVSE/IsoMux/*/*.cpp 2>/dev/null | head -12
