#!/usr/bin/env bash
# -20 Plug & Charge, forward, against 2026.10.0, with our car signing ecdsa-sha512 ([V2G20-2473]) since
# the fix of 2026-11-27, and stopping on an AuthorizationRes WARNING ([V2G20-1583]).
#   pnc521-*      a P-521 MO hierarchy of ours that meets their Annex B profile check (mint-mo521.sh),
#                 trusted through EvseSecurity's mo_ca_bundle = their MO root + ours
#   pnc256-stop   control: the release's own P-256 contract, which they must refuse with
#                 WARNING_CertificateValidationError and on which our car must now stop
set -u
E=/home/ahzf/everest
C=$E/configs-2610
P=$E/dist-2610/etc/everest/certs
T=$E/run/2610-fwd/tls
M=$E/run/2610-fwd/mo521
F="bash $E/fwd-2610.sh"

python3 - <<'EOF'
import yaml
C = "/home/ahzf/everest/configs-2610"
d = yaml.safe_load(open(f"{C}/config-d20-pnc-fwd2610.yaml"))
d["active_modules"]["evse_security"]["config_module"]["mo_ca_bundle"] = "/home/ahzf/everest/run/2610-fwd/mo521/mo-bundle.pem"
out = f"{C}/config-d20-pnc521-fwd2610.yaml"
with open(out, "w") as f:
    f.write("# 2026-11-27: config-d20-pnc-fwd2610.yaml, MO trust = their MO root + our P-521 MO root.\n")
    yaml.safe_dump(d, f, sort_keys=False)
print("wrote", out)
EOF
grep -n "mo_ca_bundle" $C/config-d20-pnc521-fwd2610.yaml || { echo "FAIL: no mo_ca_bundle"; exit 1; }

bash $E/sync-build-i15118.sh || exit 1
grep -c "ecdsa-sha512" /home/ahzf/i15118/libs/EVSimulatorApp/libs/WWCP_ISO15118/WWCP_ISO15118_Session/StateMachines/Iso20/XmlDsigInteropSign.cs

MTLS="V2G_INTEROP_TLS=1 V2G_INTEROP_TLS_TRUST=$T/v2g-root.pem V2G_INTEROP_TLS_CLIENT=$T/vehicle.p12:123456"
P521="V2G_INTEROP_CONTRACT_CERT=$M/contract521.p12 V2G_INTEROP_CONTRACT_PASS=123456"
P256="V2G_INTEROP_CONTRACT_CERT=$P/client/mo/MO_CERT_CHAIN.p12 V2G_INTEROP_CONTRACT_PASS=123456"

SESSIONS=2 SDPSEC=00 PROTO=20 MODE=dc $F pnc521-tls13     $C/config-d20-pnc521-fwd2610.yaml $MTLS $P521
           SDPSEC=00 PROTO=20 MODE=dc $F pnc521-tls13-dyn $C/config-d20-pnc521-fwd2610.yaml $MTLS $P521 V2G_INTEROP_DYNAMIC=1
           SDPSEC=00 PROTO=20 MODE=dc $F pnc256-stop      $C/config-d20-pnc-fwd2610.yaml    $MTLS $P256
echo "=== PNC521 ARMS DONE $(date -Is)"
