"""Read a TLS 1.2 handshake out of a Linux-cooked (SLL) or Ethernet pcap: the ClientHello's trusted_ca_keys
(RFC 6066, extension 3) and the server's Certificate message, written as PEM for openssl.
    python3 -I tls-handshake.py <capture.pcap> <server-port> <out-prefix>
"""
import struct, sys, binascii, base64

path, port, prefix = sys.argv[1], int(sys.argv[2]), sys.argv[3]
data = open(path, "rb").read()
magic = data[:4]
endian = "<" if magic in (b"\xd4\xc3\xb2\xa1", b"\x4d\x3c\xb2\xa1") else ">"
linktype = struct.unpack(endian + "I", data[20:24])[0]
off = 24
streams = {"c2s": {}, "s2c": {}}
while off + 16 <= len(data):
    _, _, incl, _ = struct.unpack(endian + "IIII", data[off:off + 16]); off += 16
    pkt = data[off:off + incl]; off += incl
    if linktype == 113:   l2, proto = 16, struct.unpack(">H", pkt[14:16])[0]
    elif linktype == 276: l2, proto = 20, struct.unpack(">H", pkt[0:2])[0]
    else:                 l2, proto = 14, struct.unpack(">H", pkt[12:14])[0]
    if proto == 0x86DD:   ip, ihl, nxt = pkt[l2:], 40, pkt[l2 + 6]
    elif proto == 0x0800: ip = pkt[l2:]; ihl, nxt = (ip[0] & 15) * 4, ip[9]
    else: continue
    if nxt != 6: continue
    tcp = ip[ihl:]
    sport, dport, seq = struct.unpack(">HHI", tcp[:8])
    payload = tcp[(tcp[12] >> 4) * 4:]
    if not payload: continue
    d = "c2s" if dport == port else "s2c" if sport == port else None
    if d: streams[d].setdefault(seq, payload)

def stream(d):
    return b"".join(p for _, p in sorted(streams[d].items()))

def handshakes(raw):
    out, i, hs = [], 0, b""
    while i + 5 <= len(raw):
        ctype, _, ln = raw[i], raw[i + 1:i + 3], struct.unpack(">H", raw[i + 3:i + 5])[0]
        if ctype == 22: hs += raw[i + 5:i + 5 + ln]
        elif ctype == 20: break            # ChangeCipherSpec: what follows is encrypted
        i += 5 + ln
    j = 0
    while j + 4 <= len(hs):
        t, ln = hs[j], int.from_bytes(hs[j + 1:j + 4], "big")
        out.append((t, hs[j + 4:j + 4 + ln])); j += 4 + ln
    return out

for t, body in handshakes(stream("c2s")):
    if t != 1: continue
    p = 2 + 32; p += 1 + body[p]; p += 2 + struct.unpack(">H", body[p:p + 2])[0]; p += 1 + body[p]
    end = p + 2 + struct.unpack(">H", body[p:p + 2])[0]; p += 2
    print("ClientHello extensions:", end=" ")
    found = False
    while p < end:
        et, el = struct.unpack(">HH", body[p:p + 4]); ext = body[p + 4:p + 4 + el]; p += 4 + el
        print(et, end=" ")
        if et == 3:
            found = True
            tck = ext
    print()
    if found:
        print("trusted_ca_keys extension:", binascii.hexlify(tck).decode())
        q, lst = 2, struct.unpack(">H", tck[:2])[0]
        while q < 2 + lst:
            idt = tck[q]; q += 1
            if idt == 3: print("  cert_sha1_hash", binascii.hexlify(tck[q:q + 20]).decode()); q += 20
            elif idt == 0: print("  pre_agreed");
            else: ln = struct.unpack(">H", tck[q:q + 2])[0]; print("  type", idt, "len", ln); q += 2 + ln
    else:
        print("no trusted_ca_keys extension")

for t, body in handshakes(stream("s2c")):
    if t != 11: continue
    total = int.from_bytes(body[:3], "big"); p = 3; n = 0
    with open(prefix + ".served-chain.pem", "w") as f:
        while p < 3 + total:
            ln = int.from_bytes(body[p:p + 3], "big"); der = body[p + 3:p + 3 + ln]; p += 3 + ln; n += 1
            f.write("-----BEGIN CERTIFICATE-----\n" + "\n".join(
                base64.b64encode(der).decode()[k:k + 64] for k in range(0, len(base64.b64encode(der)), 64)) +
                "\n-----END CERTIFICATE-----\n")
    print(f"server Certificate: {n} certificate(s) -> {prefix}.served-chain.pem")
