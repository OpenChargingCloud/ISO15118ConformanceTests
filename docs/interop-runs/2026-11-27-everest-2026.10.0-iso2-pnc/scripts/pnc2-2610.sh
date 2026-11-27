#!/usr/bin/env bash
# -2 Plug & Charge against everest-core 2026.10.0, forward, over TLS 1.2 with the release's own test PKI.
# 2026-11-26 stopped at Authorization with the stock config: Ongoing until FAILED. The arms separate
# the two candidate causes and then measure what the August arm measured against 2026.02.1.
#   pnc2-2610.sh [arm ...]     (no argument = all)
set -u
E=/home/ahzf/everest
C=$E/configs-2610
P=$E/dist-2610/etc/everest/certs
T=$E/run/2610-pnc2/tls
F="bash $E/fwd-2610v.sh"
mkdir -p "$T"

cp "$P/ca/v2g/V2G_ROOT_CA.pem" "$T/v2g-root.pem"
TLS="V2G_INTEROP_TLS=1 V2G_INTEROP_TLS_TRUST=$T/v2g-root.pem"
PNC="V2G_INTEROP_CONTRACT_CERT=$P/client/mo/MO_CERT_CHAIN.p12 V2G_INTEROP_CONTRACT_PASS=123456"
STOCK=$C/config-dc2-pnc-fwd2610.yaml
HOP=$C/config-dc2-pnc-hop-fwd2610.yaml
BATTERY="V2G_INTEROP_BATTERY=kwh=60,soc=20,target=70,power=0,loops=200 V2G_INTEROP_CHARGE_INTERVAL=1000"

WANTED=" $* "
want() { [ "$WANTED" = "  " ] || [[ "$WANTED" == *" $1 "* ]]; }

# control: yesterday's config, no token_provider hop, their dummy validator
want stock          && SDPSEC=00 $F stock          $STOCK $TLS $PNC
# the hop alone: their DummyTokenValidator (Accepted) decides
want hop-dummy      && SESSIONS=2 SDPSEC=00 $F hop-dummy $HOP $TLS $PNC
# our validator decides, and logs what their station hands over
want val-accepted   && SESSIONS=2 SDPSEC=00 VALIDATOR="--status Accepted" $F val-accepted $HOP $TLS $PNC
want val-revoked    && SESSIONS=2 SDPSEC=00 VALIDATOR="--status Invalid --certificate-status CertificateRevoked" $F val-revoked $HOP $TLS $PNC
# the station verifies the contract chain itself (verify_contract_cert_chain, default false): only
# then does EvseV2G derive iso15118CertificateHashData for the backend
VERIFY=$C/config-dc2-pnc-verify-fwd2610.yaml
want val-verify     && SESSIONS=2 SDPSEC=00 VALIDATOR="--status Accepted" $F val-verify $VERIFY $TLS $PNC
# a real charge, 20 -> 70 %
want val-charge     && SDPSEC=00 VALIDATOR="--status Accepted" $F val-charge $HOP $TLS $PNC $BATTERY
