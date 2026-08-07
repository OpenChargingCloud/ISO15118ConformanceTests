#!/bin/bash
# eVDriveFlow's EV over TLS, against our SECC.
#
# Differences from run.sh:
#   * SECURITY_PROTOCOL back to 0x00 (their own switch; the image had it at 0x10 to get a first
#     plain-TCP session at all),
#   * the SDP shim answers with security byte 0x00 so their EV opens a TLS socket,
#   * stdin held open, because of the 2026-08-06 finding.
# Their EV verifies our station against shared/certificates/certs/v2gRootCACert.pem and presents
# vehicleCertChain.pem — so our side must serve their seccCertChain.pem. Nothing here is ours.
set -u
LOG=/home/ahzf/edf/ev-tls.log

docker rm -f edf-ev >/dev/null 2>&1
docker run -d --name edf-ev --network edfnet edf-ev-unpatched sleep infinity >/dev/null

# TLS on, in their copy inside the throwaway container
docker exec edf-ev sed -i 's/^SECURITY_PROTOCOL = 0x10/SECURITY_PROTOCOL = 0x00/' /app/shared/global_values.py
docker exec edf-ev grep -n "^SECURITY_PROTOCOL" /app/shared/global_values.py

# the freshly generated PKI (theirs, their generator) — the image still carries the expired 2022 one
docker cp /home/ahzf/edf/eVDriveFlow/shared/certificates edf-ev:/app/shared/

# Optional: give their simulated battery a state of charge. Their EVEmulator starts at present_soc = 0
# (the GUI's SOC field is what normally sets it), and their discharge envelope is an SOC-interpolated
# curve — so a headless EV declares EVMaximumDischargePower = 0 and a BPT session negotiates a car that
# cannot give anything back. SOC=<n> patches that one line in their copy, inside the throwaway
# container, and is recorded as a deviation.
if [ "${SOC:-}" != "" ]; then
  docker exec edf-ev sed -i "s/^        self.present_soc = 0$/        self.present_soc = $SOC/" \
      /app/evcc/ev_dummy_controller.py
  docker exec edf-ev grep -n "self.present_soc = " /app/evcc/ev_dummy_controller.py | head -2
fi
docker exec edf-ev sh -c "cd /app/shared/certificates/certs && openssl x509 -in seccCert.pem -noout -enddate 2>/dev/null || echo '(no openssl in image; dates checked on the host)'"

docker exec -d edf-ev socat TCP6-LISTEN:15118,fork,reuseaddr TCP:172.30.0.1:55000
docker exec -d edf-ev python3 /usr/local/bin/sdp-responder.py eth0 15118 tls
sleep 2

docker exec -d edf-ev sh -c "mkfifo -m 600 /tmp/kb 2>/dev/null; (sleep 600 > /tmp/kb &) ; cd /app/evcc && python3 start_ev.py < /tmp/kb > /tmp/ev.log 2>&1"

sleep 30
docker exec edf-ev sh -c "cat /tmp/ev.log" 2>&1 | sed 's/\x1b\[[0-9;]*m//g' > "$LOG"
echo "=== their EV over TLS:"
grep -E "Sent |Received [A-Z]|SSL|ssl|TLS|Traceback|Error|error" "$LOG" | head -30
echo "=== log: $LOG"
