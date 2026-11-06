#!/usr/bin/env python3
"""Per-message turnaround of both peers, from a packet capture of one V2GTP session.

    v2gtp-turnaround.py <capture.pcap> <flow.md> [--port 55000] [--csv out.csv]

Why a capture: our recorder keeps two octet streams and no clock (see any `*.flow.md`: "No timings"),
and a peer's own log measures what that peer thinks it did. The wire is the one clock both sides
share. For request *i* the **EV's** turnaround is the time from the last byte of response *i-1*
leaving the station to the first byte of request *i* arriving — what Table 216/217's
`V2G_EVCC_Sequence_Performance_Time` and `V2G_SECC_Sequence_Timeout` are both about — and the
**station's** response time is request *i* in to response *i* out.

Names come from the recording's `flow.md`, by position, which is how that file pairs them too. The
script refuses a capture whose frame counts disagree with the flow, rather than mislabel rows.

Reads classic pcap as `tcpdump -w` writes it: LINUX_SLL2 (`-i any`, tcpdump >= 4.99), LINUX_SLL,
Ethernet or raw IP. Standard library only.
"""
import argparse, re, statistics, struct, sys
from collections import defaultdict


def packets(path):
    with open(path, "rb") as f:
        hdr = f.read(24)
        magic = struct.unpack("<I", hdr[:4])[0]
        if magic in (0xA1B2C3D4, 0xA1B23C4D):
            e = "<"
        elif magic in (0xD4C3B2A1, 0x4D3CB2A1):
            e = ">"
        else:
            sys.exit("not a classic pcap file (pcapng is not supported; use tcpdump -w)")
        nano = magic in (0xA1B23C4D, 0x4D3CB2A1)
        linktype = struct.unpack(e + "I", hdr[20:24])[0]
        while True:
            rh = f.read(16)
            if len(rh) < 16:
                return
            sec, frac, incl, _orig = struct.unpack(e + "IIII", rh)
            data = f.read(incl)
            yield sec + frac / (1e9 if nano else 1e6), linktype, data


def l3(linktype, data):
    """-> (pkttype or None, ethertype, l3 bytes)"""
    if linktype == 276:                      # LINUX_SLL2
        return data[10], struct.unpack(">H", data[0:2])[0], data[20:]
    if linktype == 113:                      # LINUX_SLL
        return struct.unpack(">H", data[0:2])[0], struct.unpack(">H", data[14:16])[0], data[16:]
    if linktype == 1:                        # Ethernet
        return None, struct.unpack(">H", data[12:14])[0], data[14:]
    if linktype in (101, 12, 14):            # raw IP
        return None, 0x86DD if data[0] >> 4 == 6 else 0x0800, data
    sys.exit(f"unsupported link type {linktype}")


def tcp_segments(path, port):
    seen = set()
    for ts, lt, data in packets(path):
        _pt, et, ip = l3(lt, data)
        if et == 0x86DD:
            if ip[6] != 6:
                continue
            src, dst, tcp = ip[8:24], ip[24:40], ip[40:]
        elif et == 0x0800:
            if ip[9] != 6:
                continue
            ihl = (ip[0] & 0x0F) * 4
            src, dst, tcp = ip[12:16], ip[16:20], ip[ihl:]
        else:
            continue
        sport, dport, seq = struct.unpack(">HHI", tcp[0:8])
        if port not in (sport, dport):
            continue
        payload = tcp[(tcp[12] >> 4) * 4:]
        if not payload:
            continue
        key = (sport, dport, seq, len(payload))
        if key in seen:                      # -i any can see a loopback segment twice
            continue
        seen.add(key)
        yield ts, ("ev" if dport == port else "secc"), seq, payload


def v2gtp_frames(segments):
    """-> {direction: [(t_first_byte, t_last_byte, payload_type, length)]}"""
    out = defaultdict(list)
    buf = defaultdict(bytearray)
    start = {}
    nxt = {}
    for ts, d, seq, payload in sorted(segments, key=lambda s: s[0]):
        if d in nxt and seq != nxt[d]:
            if (seq - nxt[d]) % 2**32 > 2**31:   # retransmission of bytes already taken
                continue
            sys.exit(f"gap in the {d} stream at seq {seq} (expected {nxt[d]}) — capture lost a segment")
        nxt[d] = (seq + len(payload)) % 2**32
        if not buf[d]:
            start[d] = ts
        buf[d] += payload
        while len(buf[d]) >= 8:
            if buf[d][0] != 0x01 or buf[d][1] != 0xFE:
                sys.exit(f"{d}: not a V2GTP header: {bytes(buf[d][:8]).hex()}")
            ptype, plen = struct.unpack(">HI", bytes(buf[d][2:8]))
            if len(buf[d]) < 8 + plen:
                break
            out[d].append((start[d], ts, ptype, 8 + plen))
            del buf[d][:8 + plen]
            start[d] = ts
    return out


def flow_names(path):
    rows = []
    for line in open(path, encoding="utf-8"):
        m = re.match(r"\|\s*(\d+)\s*\|\s*([^|]+?)\s*\|\s*([^|]*?)\s*\|", line)
        if m:
            rows.append((m.group(2), m.group(3)))
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("pcap")
    ap.add_argument("flow")
    ap.add_argument("--port", type=int, default=55000)
    ap.add_argument("--csv")
    a = ap.parse_args()

    fr = v2gtp_frames(list(tcp_segments(a.pcap, a.port)))
    req, res = fr["ev"], fr["secc"]
    names = flow_names(a.flow)
    if len(req) != len(names):
        sys.exit(f"capture has {len(req)} EV frames, flow has {len(names)} rows — not the same session")
    if len(res) not in (len(req), len(req) - 1):
        sys.exit(f"capture has {len(req)} requests and {len(res)} responses")

    rows = []
    for i, (rq, rs_name) in enumerate(names):
        t_req = req[i][0]
        ev_turn = (t_req - res[i - 1][1]) * 1000 if i > 0 else None
        secc = (res[i][0] - req[i][1]) * 1000 if i < len(res) else None
        rows.append((i, rq, ev_turn, secc))

    if a.csv:
        with open(a.csv, "w", encoding="utf-8", newline="\n") as f:
            f.write("index,request,ev_turnaround_ms,secc_response_ms\n")
            for i, rq, e, s in rows:
                f.write(f"{i},{rq},{'' if e is None else f'{e:.3f}'},{'' if s is None else f'{s:.3f}'}\n")

    by = defaultdict(lambda: ([], []))
    order = []
    for _i, rq, e, s in rows:
        if rq not in by:
            order.append(rq)
        if e is not None:
            by[rq][0].append(e)
        if s is not None:
            by[rq][1].append(s)

    def fmt(v):
        if not v:
            return "—"
        if len(v) == 1:
            return f"{v[0]:.1f}"
        return f"{statistics.median(v):.1f} (min {min(v):.1f}, max {max(v):.1f})"

    span = (req[-1][0] - req[0][0])
    print(f"{len(req)} requests, {len(res)} responses, first request to last: {span:.3f} s")
    print()
    print("| request | n | EV turnaround, ms — median (min, max) | our response, ms — median (min, max) |")
    print("|---|---:|---|---|")
    for rq in order:
        e, s = by[rq]
        print(f"| {rq} | {max(len(e), len(s))} | {fmt(e)} | {fmt(s)} |")


if __name__ == "__main__":
    main()
