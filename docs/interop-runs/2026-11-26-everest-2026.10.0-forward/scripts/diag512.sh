#!/usr/bin/env bash
# DIAGNOSTIC, WSL copy only: does Evse15118D20 (2026.10.0) accept our -20 PnC AuthorizationReq once the
# Josev-form signature uses ecdsa-sha512 / SHA-512 ([V2G20-2473]) instead of ecdsa-sha256 / SHA-256?
# Same P-256 contract key, same standalone-xmldsig SignedInfo grammar. The Windows tree is not touched;
# the copy is restored afterwards, with a touch, because rsync -a gives the restored file its old mtime
# and MSBuild would keep the patched DLL.
set -u
E=/home/ahzf/everest
I=/home/ahzf/i15118
SRC=$I/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_Session/StateMachines/Iso20/XmlDsigInteropSign.cs
P=$E/dist-2610/etc/everest/certs
T=$E/run/2610-fwd/tls

sed -i 's/xmldsig-more#ecdsa-sha256/xmldsig-more#ecdsa-sha512/; s/xmlenc#sha256/xmlenc#sha512/; s/SHA256\.HashData/SHA512.HashData/; s/HashAlgorithmName\.SHA256/HashAlgorithmName.SHA512/' "$SRC"
grep -n "sha512\|SHA512" "$SRC"
cd $I && dotnet build ISO15118ConformanceTests.Simulation/ISO15118ConformanceTests.Simulation.csproj -c Release \
  --artifacts-path /home/ahzf/wsl-artifacts -v q -nologo > $E/run/2610-fwd/diag512-build.log 2>&1
rc=$?; echo "diag build exit $rc"
if [ $rc -eq 0 ]; then
  SDPSEC=00 PROTO=20 MODE=dc bash $E/fwd-2610.sh pnc20-diag512 $E/configs-2610/config-d20-pnc-fwd2610.yaml \
    V2G_INTEROP_TLS=1 V2G_INTEROP_TLS_TRUST=$T/v2g-root.pem V2G_INTEROP_TLS_CLIENT=$T/vehicle.p12:123456 \
    V2G_INTEROP_CONTRACT_CERT=$P/client/mo/MO_CERT_CHAIN.p12 V2G_INTEROP_CONTRACT_PASS=123456 | grep -E "Passed|Failed|endpoint|FAIL"
fi

# restore the copy from the Windows tree and make MSBuild see it
bash $E/sync-build-i15118.sh > /dev/null 2>&1
touch "$SRC"
cd $I && dotnet build ISO15118ConformanceTests.Simulation/ISO15118ConformanceTests.Simulation.csproj -c Release \
  --artifacts-path /home/ahzf/wsl-artifacts -v q -nologo > /dev/null 2>&1; echo "restore build exit $?"
grep -c "ecdsa-sha256" "$SRC" | sed 's/^/restored, ecdsa-sha256 occurrences: /'
