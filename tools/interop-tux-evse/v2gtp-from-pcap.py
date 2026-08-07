#!/usr/bin/env python3
"""Pull V2GTP frames out of a pcap, per direction, without scapy.

Reads a libpcap file, keeps Ethernet/IPv6/TCP payloads, reassembles each direction by sequence
number, and splits the byte stream on the V2GTP header (0x01 0xFE, payload type, 4-byte length).
Prints the first N frames per direction as hex, which is all that is needed to decode the
SupportedAppProtocol handshake with somebody else's codec.
"""
import struct
import sys
from collections import defaultdict

PCAP_MAGIC = {0xa1b2c3d4: ("<", 1), 0xd4c3b2a1: (">", 1),
              0xa1b23c4d: ("<", 1000), 0x4d3cb2a1: (">", 1000)}


def packets(path):
    with open(path, "rb") as fh:
        raw = fh.read()
    magic = struct.unpack("<I", raw[:4])[0]
    if magic not in PCAP_MAGIC:
        magic = struct.unpack(">I", raw[:4])[0]
    if magic not in PCAP_MAGIC:
        sys.exit(f"not a libpcap file (magic {magic:#x})")
    endian, _ = PCAP_MAGIC[magic]
    linktype = struct.unpack(endian + "I", raw[20:24])[0]
    off = 24
    while off + 16 <= len(raw):
        _, _, caplen, _ = struct.unpack(endian + "IIII", raw[off:off + 16])
        off += 16
        yield linktype, raw[off:off + caplen]
        off += caplen


def tcp_segments(path):
    """Yields (src_port, dst_port, seq, payload) for every TCP segment carrying data."""
    for linktype, pkt in packets(path):
        if linktype != 1 or len(pkt) < 14:          # Ethernet only; these captures are
            continue
        ethertype = struct.unpack(">H", pkt[12:14])[0]
        p = pkt[14:]
        if ethertype == 0x86DD:                      # IPv6
            if len(p) < 40 or p[6] != 6:             # next header must be TCP
                continue
            p = p[40:]
        elif ethertype == 0x0800:                    # IPv4
            if len(p) < 20:
                continue
            ihl = (p[0] & 0x0F) * 4
            if p[9] != 6:
                continue
            p = p[ihl:]
        else:
            continue
        if len(p) < 20:
            continue
        sport, dport, seq = struct.unpack(">HHI", p[:8])
        data_off = (p[12] >> 4) * 4
        payload = p[data_off:]
        if payload:
            yield sport, dport, seq, payload


def streams(path):
    """Reassembles each direction into one byte string, ordered by sequence number."""
    chunks = defaultdict(dict)
    for sport, dport, seq, payload in tcp_segments(path):
        chunks[(sport, dport)].setdefault(seq, payload)
    return {k: b"".join(v[s] for s in sorted(v)) for k, v in chunks.items()}


def v2gtp_frames(blob):
    """Splits a reassembled stream on the V2GTP header. Stops at the first malformed one."""
    off = 0
    while off + 8 <= len(blob):
        if blob[off] != 0x01 or blob[off + 1] != 0xFE:
            off += 1
            continue
        payload_type = struct.unpack(">H", blob[off + 2:off + 4])[0]
        length = struct.unpack(">I", blob[off + 4:off + 8])[0]
        if length > 0x40000 or off + 8 + length > len(blob):
            break
        yield payload_type, blob[off:off + 8 + length]
        off += 8 + length


def main():
    path = sys.argv[1]
    limit = int(sys.argv[2]) if len(sys.argv) > 2 else 3
    for (sport, dport), blob in sorted(streams(path).items()):
        frames = list(v2gtp_frames(blob))
        if not frames:
            continue
        print(f"=== {sport} -> {dport}: {len(blob)} byte(s), {len(frames)} V2GTP frame(s)")
        for i, (ptype, frame) in enumerate(frames[:limit]):
            print(f"  [{i}] payloadType=0x{ptype:04x} bytes={len(frame)}")
            print(f"      {frame.hex()}")
        print()


if __name__ == "__main__":
    main()
