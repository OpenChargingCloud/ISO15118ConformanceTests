#!/usr/bin/env bash
# isomux §1 and §2 were already on the wire against 2026.10.0 in the forward run (2026-11-27 00:25 and
# 00:28); this lifts the lines that show them out of those arms' logs, unedited apart from ANSI colour.
set -u
F=/home/ahzf/everest/run/2610-fwd
O=/home/ahzf/everest/run/2610-repro/isomux
SP=/mnt/c/Users/achim/AppData/Local/Temp/claude/443da193b49d45a8/b340fbb5-0a48-4a7c-bb63-fd8aec8ece2e/scratchpad/repro
mkdir -p "$O"
strip() { sed 's/\x1b\[[0-9;]*m//g'; }
for a in fix-mux-both-2first fix-mux-tls-both mux-tls-iso20; do
  {
    echo "# $a — their side (IsoMux / Evse15118D20 / EvseManager), SAP and routing lines"
    strip < $F/$a/their-side.log | grep -E "handshake_req|Handling SupportedAppProtocolReq|Negotiated ISO 15118-20|SupportedAppProtocolRes|Selected DC service|TLS server on|presented on TLS|unsecured|alert|SSL|tls" | grep -v DEBUG | cut -c1-260
    echo
    echo "# $a — ours"
    grep -E "^ ?(TLS|SAP):|AuthenticationException|alert protocol version|Passed |Failed " $F/$a/our-evcc-1.log | sort -u
  } > "$O/$a.txt"
  echo "== $a: $(wc -l < "$O/$a.txt") lines"
done
cp $F/fix-mux-both-2first/rec-1/*.flow.md "$O/fix-mux-both-2first.flow.md" 2>/dev/null
cp $F/fix-mux-tls-both/rec-1/*.flow.md "$O/fix-mux-tls-both.flow.md" 2>/dev/null
# the release's IsoMux source lines the two sections are about
C=/home/ahzf/everest/core-2610/modules/EVSE/IsoMux
{
  echo "# core-2610 (74845c578) IsoMux, the lines §1 and §2 rest on"
  grep -n "Iso20Offered\|Priority" $C/v2g_server.cpp | head -8
  grep -rn "TLS1_2_VERSION\|max_proto\|TLSv1_2\|tls_1_3\|TLS1_3" $C/*.cpp $C/*/*.cpp 2>/dev/null | head -8
} > "$O/isomux-source-2610.txt"
cat "$O/isomux-source-2610.txt"
