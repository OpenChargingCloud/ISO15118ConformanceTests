#!/usr/bin/env bash
# client-auth §1/§2 and d20-trust-anchor against the 2026.10.0 Evse15118D20, with openssl s_client.
# One manager start per arm. The config is the stock default for client auth (enforce_tls_1_3 left
# at its default, false), derived from config-d20-tls-fwd2610.yaml by dropping that one line.
#   tls-arms-2610.sh [arm ...]
set -u
E=/home/ahzf/everest
SP=/mnt/c/Users/achim/AppData/Local/Temp/claude/443da193b49d45a8/b340fbb5-0a48-4a7c-bb63-fd8aec8ece2e/scratchpad/repro
REPO=/mnt/d/Coding/OpenChargingCloud/ISO15118ConformanceTests
P=$E/dist-2610/etc/everest/certs
C=$E/configs-2610
OUTROOT=$E/run/2610-repro/tls
mkdir -p "$OUTROOT"
CONF=$C/config-d20-tls-default-2610.yaml
grep -v "enforce_tls_1_3: true" $C/config-d20-tls-fwd2610.yaml \
  | sed 's/^# 2026-11-26: config-d20-tls-ours.yaml.*/# 2026-11-27: config-d20-tls-fwd2610.yaml with enforce_tls_1_3 left at its default (false)./' > "$CONF"
cp "$CONF" "$OUTROOT/"

WANTED=" $* "
want() { [ "$WANTED" = "  " ] || [[ "$WANTED" == *" $1 "* ]]; }

endpoint() {
  local R
  for t in 1 2 3; do
    R=$(SECURITY=00 bash $E/sdp-probe.sh eth0 2>&1) && { echo "${R%% *}" | sed 's/^\[\(.*\)\]:\([0-9]*\)$/\1 \2/'; return 0; }
    sleep 2
  done
  return 1
}

arm() {
  local name=$1; shift
  local RUN=$OUTROOT/$name
  rm -rf "$RUN"; mkdir -p "$RUN"
  echo "################ arm $name"
  bash $SP/station-2610.sh "$RUN" "$CONF" || return
  local EP; EP=$(endpoint) || { echo "FAIL: no SDP answer"; return; }
  local ADDR=${EP% *} PORT=${EP#* }
  echo "endpoint [$ADDR]:$PORT"
  echo "openssl s_client $*" > "$RUN/openssl.cmd"
  if [ "$name" = ca12-replay ]; then
    # The REQUEST frames, exchanges [0] and [1] of libs/EVSimulatorApp/vectors/Session.iso20-dc-eim.trace.json
    # (supportedAppProtocolReq with one entry, -20:DC; SessionSetupReq). Not 2026-08-10's
    # replay.consequence.hex: that file holds the station's responses.
    SAP=01fe8001000000258000f3ab9371d34b9b79d39ba321d34b9b79d189a98989c1d1699181d22218010000040040
    SETUP=01fe800200000019808c040000000000000000080f2d6ca0620422ab21a1981880
    echo "$SAP"   | xxd -r -p > "$RUN/req-sap.bin"
    echo "$SETUP" | xxd -r -p > "$RUN/req-sessionsetup.bin"
    timeout 15 openssl s_client -connect "[$ADDR]:$PORT" "$@" -quiet -ign_eof \
      < <(cat "$RUN/req-sap.bin"; sleep 2; cat "$RUN/req-sessionsetup.bin"; sleep 4) > "$RUN/replay.out" 2> "$RUN/openssl.txt"
    echo "sent $(( $(wc -c < "$RUN/req-sap.bin") + $(wc -c < "$RUN/req-sessionsetup.bin") )) B, got $(wc -c < "$RUN/replay.out") B"
    xxd "$RUN/replay.out" | head -8
  else
    timeout 15 openssl s_client -connect "[$ADDR]:$PORT" "$@" -state -msg < /dev/null > "$RUN/openssl.txt" 2>&1
  fi
  echo "--- openssl"
  grep -aE "^(New|Protocol|Cipher|Verify return code)|alert|CertificateRequest|certificate required|Acceptable client certificate CA names|Requested Signature Algorithms|Negotiated TLS1.3 group|Server Temp Key|Peer signing" "$RUN/openssl.txt" | sort | uniq -c | head -14
  sleep 2
  echo "--- their side"
  sed 's/\x1b\[[0-9;]*m//g' "$RUN/their-side.log" | grep -iE "verify|Vehicle Cert|certificate|handshake|TLS|ssl|session id|Negotiated|SupportedAppProtocol|SessionSetup" | grep -viE "debug" | tail -14 | cut -c1-220
  echo "manager alive: $(pgrep -x manager | wc -l)"
  bash $E/stop-rig2.sh > /dev/null; pkill -f sil-car.sh 2>/dev/null
  cp -r /tmp/everest-logs "$RUN/session-logs" 2>/dev/null
}

# client-auth §1: offer TLS 1.2 only, no client certificate -> is a CertificateRequest sent?
want ca12        && arm ca12        -tls1_2
# client-auth §1 consequence: same, then supportedAppProtocolReq(-20:DC) + SessionSetupReq
want ca12-replay && arm ca12-replay -tls1_2
# client-auth §1 control and §2: TLS 1.3, no certificate -> CertificateRequest, and what it carries
want ca13        && arm ca13        -tls1_3
# d20-trust-anchor arm A: OEM provisioning chain (OEMRootCA) as the TLS client certificate
cat $P/ca/oem/OEM_SUB_CA2.pem $P/ca/oem/OEM_SUB_CA1.pem > "$OUTROOT/oem-subcas.pem"
cat $P/ca/vehicle/VEHICLE_SUB_CA2.pem $P/ca/vehicle/VEHICLE_SUB_CA1.pem > "$OUTROOT/vehicle-subcas.pem"
want ta-oem      && arm ta-oem      -tls1_3 -cert $P/client/oem/OEM_LEAF.pem -key $P/client/oem/OEM_LEAF.key -cert_chain "$OUTROOT/oem-subcas.pem" -pass file:$P/client/oem/OEM_LEAF_PASSWORD.txt
# d20-trust-anchor arm B: a contract chain (MORootCA) as the TLS client certificate
want ta-mo       && arm ta-mo       -tls1_3 -cert $P/client/mo/MO_LEAF.pem -key $P/client/mo/MO_LEAF.key -cert_chain $P/ca/mo/INTERMEDIATE_MO_CA_CERTS.pem -pass file:$P/client/mo/MO_LEAF_PASSWORD.txt
# control: the release's own vehicle chain (V2G root), which should verify
want ta-vehicle  && arm ta-vehicle  -tls1_3 -cert $P/client/vehicle/VEHICLE_LEAF.pem -key $P/client/vehicle/VEHICLE_LEAF.key -cert_chain "$OUTROOT/vehicle-subcas.pem" -pass file:$P/client/vehicle/VEHICLE_LEAF_PASSWORD.txt
echo "=== TLS ARMS DONE $(date -Is)"
