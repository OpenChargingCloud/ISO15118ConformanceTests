#!/usr/bin/env python3
"""The SupportedAppProtocolReq and Res out of a `tcpdump -i any` capture (Linux cooked, SLL or SLL2).

Plaintext only: under TLS the frame is ciphertext and nothing is found, which is itself the
observation for the TLS arm. EXI is bit-packed, so a namespace string sits at any bit offset; each
character of a schema-informed string is one 8-bit unsigned (7 data bits, top bit clear) once aligned.
Scan all eight shifts for "urn:" and read ASCII until a non-printable byte.

  sap-offer.py capture.pcap
"""
import struct
import sys

def packets(path):
    raw = open(path, "rb").read()
    magic = struct.unpack("<I", raw[:4])[0]
    endian = "<" if magic in (0xa1b2c3d4, 0xa1b23c4d) else ">"
    linktype = struct.unpack(endian + "I", raw[20:24])[0]
    off = 24
    while off + 16 <= len(raw):
        _, _, caplen, _ = struct.unpack(endian + "IIII", raw[off:off + 16])
        off += 16
        yield linktype, raw[off:off + caplen]
        off += caplen

def tcp_payloads(path):
    for linktype, pkt in packets(path):
        hdr = {113: 16, 276: 20, 1: 14}.get(linktype)
        if hdr is None or len(pkt) < hdr + 40:
            continue
        ip = pkt[hdr:]
        if ip[0] >> 4 != 6 or ip[6] != 6:          # IPv6, next header TCP
            continue
        tcp = ip[40:]
        sport, dport = struct.unpack(">HH", tcp[:4])
        data = tcp[(tcp[12] >> 4) * 4:]
        if data:
            yield sport, dport, data

def strings_at_any_shift(b):
    found = []
    as_int = int.from_bytes(b, "big")
    n = len(b) * 8
    for shift in range(8):
        v = (as_int << shift) & ((1 << n) - 1)
        s = v.to_bytes(len(b), "big")
        i = s.find(b"urn:")
        while i >= 0:
            j = i
            while j < len(s) and 0x20 < s[j] < 0x7f:
                j += 1
            found.append(s[i:j].decode())
            i = s.find(b"urn:", j)
    return found

seen = 0
for sport, dport, data in tcp_payloads(sys.argv[1]):
    if data[:4] != b"\x01\xfe\x80\x01":
        continue
    length = struct.unpack(">I", data[4:8])[0]
    body = data[8:8 + length]
    who = "EV -> SECC" if dport < sport or dport == 50000 else "SECC -> EV"
    print(f"{who}  {sport}->{dport}  V2GTP 0x8001, {length} B: {body.hex()}")
    for s in strings_at_any_shift(body):
        print("    namespace:", s)
    seen += 1
    if seen >= 2:
        break
if seen == 0:
    print("no plaintext V2GTP 0x8001 frame in this capture")
