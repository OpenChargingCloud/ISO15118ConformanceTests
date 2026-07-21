#!/usr/bin/env python3
# Minimal SECC-side SDP responder for the REVERSE live interop run (Josev EVCC -> our SECC): it joins the V2G
# SDP multicast group on an interface and answers each EVCC SDP_Request with an SDP_Response advertising OUR
# SECC's link-local IPv6 + TCP port (NO_TLS, TCP). This isolates the (separately-tracked) WWCP EVCC/SECC SDP
# multicast interface-binding bug from the actual session interop -- the mirror of the forward run's SDP
# request helper. See docs/interop-runs/2026-07-21-iso20-dc-tcp-reverse/notes.md.
#
# Usage: sdp-responder.py <iface> <secc-tcp-port> [tls|notls]   (e.g. sdp-responder.py eth0 55000 tls)
import socket, struct, sys, subprocess, re

iface = sys.argv[1]
port = int(sys.argv[2])
security = 0x00 if (len(sys.argv) > 3 and sys.argv[3].lower() == "tls") else 0x10  # 0x00=TLS, 0x10=NO_TLS
ifidx = socket.if_nametoindex(iface)

# our own link-local (fe80::…) on <iface> — the address the EVCC should TCP-connect to
out = subprocess.check_output(["ip", "-6", "addr", "show", iface]).decode()
lladdr = re.search(r"inet6 (fe80:[0-9a-f:]+)/\d+", out).group(1)

# SDP response payload: 16-byte IPv6 addr, 2-byte port, security (0x00 TLS / 0x10 NO_TLS), transport=0x00 (TCP)
payload = socket.inet_pton(socket.AF_INET6, lladdr) + struct.pack("!H", port) + bytes([security, 0x00])
resp = bytes([0x01, 0xFE, 0x90, 0x01]) + struct.pack("!I", len(payload)) + payload

s = socket.socket(socket.AF_INET6, socket.SOCK_DGRAM)
s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
s.bind(("::", 15118))
mreq = socket.inet_pton(socket.AF_INET6, "ff02::1") + struct.pack("I", ifidx)
s.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_JOIN_GROUP, mreq)
sec_name = "TLS" if security == 0x00 else "NO_TLS"
print(f">>> SDP responder ready on {iface} (ff02::1:15118); advertising [{lladdr}%{iface}]:{port} ({sec_name})", flush=True)

while True:
    data, sender = s.recvfrom(1024)
    if len(data) >= 8 and data[2] == 0x90 and data[3] == 0x00:  # SDP request payload type 0x9000
        s.sendto(resp, sender)
        print(f">>> answered SDP_Request from {sender[0]} -> [{lladdr}%{iface}]:{port}", flush=True)
