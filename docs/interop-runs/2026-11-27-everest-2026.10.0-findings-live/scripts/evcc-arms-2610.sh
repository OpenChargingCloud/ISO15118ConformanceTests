#!/usr/bin/env bash
# The still-present EVerest findings that need a V2G session, against the 2026.10.0 release, our EVCC
# driven by fwd-2610.sh (fwd-2610v.sh for the TLS PnC arm). One manager start per arm.
#   evcc-arms-2610.sh [arm ...]     (no argument = all)
set -u
E=/home/ahzf/everest
C=$E/configs-2610
P=$E/dist-2610/etc/everest/certs
OUT=$E/run/2610-repro
F="bash $E/fwd-2610.sh"
FV="bash $E/fwd-2610v.sh"
mkdir -p "$OUT/tls"
cp "$P/ca/v2g/V2G_ROOT_CA.pem" "$OUT/tls/v2g-root.pem"

# d20-eim-rejection: the stock -20 DC config with their DummyTokenValidator answering Invalid
sed -e 's/validation_result: Accepted/validation_result: Invalid/' \
    -e 's/validation_reason: Token seems valid/validation_reason: Token rejected for the d20-eim-rejection reproduction/' \
    $C/config-d20-fwd2610.yaml > $C/config-d20-eim-reject-2610.yaml
grep -q "validation_result: Invalid" $C/config-d20-eim-reject-2610.yaml || { echo "FAIL: reject config"; exit 1; }

TLS="V2G_INTEROP_TLS=1 V2G_INTEROP_TLS_TRUST=$OUT/tls/v2g-root.pem"
PNC="V2G_INTEROP_CONTRACT_CERT=$P/client/mo/MO_CERT_CHAIN.p12 V2G_INTEROP_CONTRACT_PASS=123456"
UPD="V2G_INTEROP_PROVISION=update V2G_INTEROP_PROVISION_PARAMSET=1 V2G_INTEROP_PROVISION_CERT=$P/client/mo/MO_CERT_CHAIN.p12 V2G_INTEROP_PROVISION_PASS=123456 V2G_INTEROP_PROVISION_EMAID=UKSWI123456791A"

WANTED=" $* "
want() { [ "$WANTED" = "  " ] || [[ "$WANTED" == *" $1 "* ]]; }
run() { local arm=$1; shift; echo "################ $arm  $(date -Is)"; "$@" 2>&1; mkdir -p "$OUT/$arm"; cp -r $E/run/2610-fwd/$arm/. "$OUT/$arm/" 2>/dev/null; cp -r $E/run/2610-pnc2/$arm/. "$OUT/$arm/" 2>/dev/null; }

# ---- Evse15118D20 (-20)
# d20-meter-info: control, then the same session asking MeterInfoRequested in every charge-loop request
want meter-ctl     && run meter-ctl     env PROTO=20 MODE=dc $F meter-ctl     $C/config-d20-fwd2610.yaml
want meter         && run meter         env PROTO=20 MODE=dc $F meter         $C/config-d20-fwd2610.yaml V2G_INTEROP_METER=1
# d20-sequence-timeout: our EV goes silent after one charge-loop iteration, connection held open 90 s
want seqto-dc      && run seqto-dc      env PROTO=20 MODE=dc $F seqto-dc      $C/config-d20-fwd2610.yaml V2G_INTEROP_SILENT=90
want seqto-ac      && run seqto-ac      env PROTO=20 MODE=ac TRIGGER=1 $F seqto-ac $C/config-ac20-fwd2610.yaml V2G_INTEROP_SILENT=90
# d20-eim-rejection: their validator rejects the EIM token; our car keeps asking up to 240 s
want eim-reject    && run eim-reject    env PROTO=20 MODE=dc $F eim-reject    $C/config-d20-eim-reject-2610.yaml V2G_INTEROP_ONGOING=240
# d20-ac-contactor-edge: CP at state C from the plug-in, so the contactor closes before PowerDelivery
want contactor-edge && run contactor-edge env PROTO=20 MODE=ac TRIGGER=0 $F contactor-edge $C/config-ac20-fwd2610.yaml

# ---- EvseV2G (-2)
# evsev2g-session-id-zero: every request after SessionSetup carries eight zero bytes; control: a wrong id
want sid-zero      && run sid-zero      env PROTO=2 MODE=dc $F sid-zero       $C/config-dc2-fwd2610.yaml V2G_INTEROP_SESSIONID=zero
want sid-wrong     && run sid-wrong     env PROTO=2 MODE=dc $F sid-wrong      $C/config-dc2-fwd2610.yaml V2G_INTEROP_SESSIONID=0123456789abcdef
# evsev2g-certificate-update: PnC over TLS 1.2, CertificateUpdateReq under parameter set 1
want certupd       && run certupd       env SDPSEC=00 $FV certupd             $C/config-dc2-pnc-hop-fwd2610.yaml $TLS $PNC $UPD
# evsev2g-metering-chain: PnC over TLS 1.2 with EvseManager's ev_receipt_required on, so the station
# asks for a MeteringReceiptReq and the MeterInfo it sends is the one the car signs back
sed '/^      charge_mode: DC/a\      ev_receipt_required: true' $C/config-dc2-pnc-hop-fwd2610.yaml > $C/config-dc2-pnc-receipt-2610.yaml
grep -q "^      ev_receipt_required: true" $C/config-dc2-pnc-receipt-2610.yaml || { echo "FAIL: receipt config"; exit 1; }
want receipt       && run receipt       env SDPSEC=00 $FV receipt             $C/config-dc2-pnc-receipt-2610.yaml $TLS $PNC
# after our [V2G2-790] fix (stack evcc2-receipt-last-meterinfo): the same arm, twice, and then the
# receipts signed over a MeterReading one higher than the one sent ([V2G2-904], the coming-back half)
want receipt-fixed && run receipt-fixed env SDPSEC=00 SESSIONS=2 $FV receipt-fixed $C/config-dc2-pnc-receipt-2610.yaml $TLS $PNC
want receipt-tamper && run receipt-tamper env SDPSEC=00 SESSIONS=2 $FV receipt-tamper $C/config-dc2-pnc-receipt-2610.yaml $TLS $PNC V2G_INTEROP_RECEIPT_TAMPER=1
# evsemanager-renegotiation-supply: EV-initiated renegotiation with CableCheck; second session shows Inoperative
want reneg         && run reneg         env PROTO=2 MODE=dc SESSIONS=2 $FV reneg $C/config-dc2-fwd2610.yaml V2G_INTEROP_RENEGOTIATE=1
echo "=== EVCC ARMS DONE $(date -Is)"
