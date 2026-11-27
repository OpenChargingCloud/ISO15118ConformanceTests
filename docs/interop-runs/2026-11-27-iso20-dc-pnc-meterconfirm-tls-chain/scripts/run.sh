#!/usr/bin/env bash
# MeteringConfirmation over -20 TLS with every chain validated to a root (2026-11-27).
#
# 1. The stack's own PKI builder mints a strict -20 P-521 hierarchy with the three anchors of -20
#    Annex C (--separate-roots): a V2G root above the CPO branch, an OEM root above the vehicle
#    branch, an MO root above the contract.
# 2. openssl packages the SECC, vehicle and contract chains as PKCS#12, each leaf with its two Sub-CAs.
# 3. Three runs of the stack's SECC against its EVCC, BouncyCastle backend on both sides:
#      ok     station trusts OEM + MO, car trusts V2G            -> everything anchored, two confirmations
#      no-mo  station trusts OEM only                            -> the contract chain is rejected
#      wrong  car trusts MO only, not the V2G root               -> the car refuses the station's TLS chain
#
# Run from libs/EVSimulatorApp/libs/WWCP_ISO15118 after a Release build of WWCP_ISO15118_SECC,
# WWCP_ISO15118_EVCC and WWCP_ISO15118_PKI/V2GPKI_CLI. Throwaway material only; the output directory
# holds private keys, so keep it out of the repository.
set -u
OUT=${OUT:-/tmp/mcchain}
PORT=${PORT:-15231}
export MSYS_NO_PATHCONV=1
# With path conversion off, Git Bash hands /tmp/... to .NET and to its openssl verbatim, and neither
# reads it. C:/... works for both and for the shell, so on Windows use that form throughout.
OUT=$(cygpath -m "$OUT" 2>/dev/null || echo "$OUT")
W=$OUT

rm -rf "$OUT"; mkdir -p "$OUT/p12" "$OUT/trust-ok" "$OUT/trust-no-mo" "$OUT/trust-car" "$OUT/trust-car-wrong" "$OUT/logs"
dotnet WWCP_ISO15118_PKI/V2GPKI_CLI/V2GPKI_CLI/bin/Release/net11.0/cloud.charging.open.protocols.ISO15118.PKI.dll \
  --out "$W/pki" --algo p521 --profile strict-20 --separate-roots --no-revocation-uris || exit 1
H="$OUT/pki/strict_15118_20_ecdsa_p521"

p12() {  # name leaf sub2 sub1 — leaf-issuer first, as SubCertificates wants
  cat "$H/$3.cert.pem" "$H/$4.cert.pem" > "$OUT/p12/$1.subs.pem"
  openssl pkcs12 -export -inkey "$H/$2.key.pem" -in "$H/$2.cert.pem" -certfile "$OUT/p12/$1.subs.pem" \
    -passout pass:123456 -out "$OUT/p12/$1.p12"
}
p12 secc     04_secc_leaf/secc       03_cpo_sub_ca_2/cpo_sub_ca_2         02_cpo_sub_ca_1/cpo_sub_ca_1
p12 vehicle  13_vehicle_leaf/vehicle 12_vehicle_sub_ca_2/vehicle_sub_ca_2 11_vehicle_sub_ca_1/vehicle_sub_ca_1
p12 contract 07_contract_leaf/contract 06_mo_sub_ca_2/mo_sub_ca_2         05_mo_sub_ca_1/mo_sub_ca_1

cp "$H/01_oem_root_ca/oem_root_ca.cert.pem" "$H/01_mo_root_ca/mo_root_ca.cert.pem" "$OUT/trust-ok/"
cp "$H/01_oem_root_ca/oem_root_ca.cert.pem"                                        "$OUT/trust-no-mo/"
cp "$H/01_v2g_root_ca/v2g_root_ca.cert.pem"                                        "$OUT/trust-car/"
cp "$H/01_mo_root_ca/mo_root_ca.cert.pem"                                          "$OUT/trust-car-wrong/"

run() {  # label station-trust car-trust port
  timeout 120 dotnet WWCP_ISO15118_SECC/bin/Release/net11.0/WWCP_ISO15118_SECC.dll \
    --listen "$4" --protocol 20 --mode dc --tls-backend bc \
    --server-cert "$W/p12/secc.p12" --server-cert-pass 123456 --require-client-cert --trust-roots "$W/$2" \
    --meter "VAN*M1" --metering-confirmation 1 --require-metering-confirmation > "$OUT/logs/$1.secc.log" 2>&1 &
  local secc=$!
  for _ in $(seq 1 100); do grep -q "SECC listening" "$OUT/logs/$1.secc.log" 2>/dev/null && break; sleep 0.2; done
  timeout 90 dotnet WWCP_ISO15118_EVCC/bin/Release/net11.0/WWCP_ISO15118_EVCC.dll \
    --connect "[::1]:$4" --protocol 20 --mode dc --tls-backend bc \
    --vehicle-cert "$W/p12/vehicle.p12" --vehicle-cert-pass 123456 --trust-roots "$W/$3" \
    --contract-cert "$W/p12/contract.p12" --contract-cert-pass 123456 > "$OUT/logs/$1.evcc.log" 2>&1
  echo "== $1: car exit $?"
  sleep 2; kill $secc 2>/dev/null; wait $secc 2>/dev/null
  cat "$OUT/logs/$1.secc.log" "$OUT/logs/$1.evcc.log"
}
run ok    trust-ok    trust-car       "$PORT"
run no-mo trust-no-mo trust-car       "$((PORT + 1))"
run wrong trust-ok    trust-car-wrong "$((PORT + 2))"
