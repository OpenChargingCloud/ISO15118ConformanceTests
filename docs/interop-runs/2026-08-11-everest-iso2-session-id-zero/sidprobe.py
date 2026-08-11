"""Does an ISO 15118-2 station check the SessionID of a mid-session request? [V2G2-460]

Three arms, one variable. Each sends SupportedAppProtocolReq, SessionSetupReq, and then the message
the station is actually waiting for — ServiceDiscoveryReq — differing only in the SessionID it carries:

    correct   the id the station just issued          -> must be served
    wrong     that id with one bit flipped            -> [V2G2-460]: FAILED_UnknownSession
    zero      eight zero bytes                        -> [V2G2-460]: FAILED_UnknownSession

`correct` proves the request is valid and in sequence, so a refusal in the other arms is about the id
and nothing else. `wrong` proves the station checks at all. `zero` is the question.

Frames are our own recorded -2 DC session (Vectors/Session.iso2-dc-eim.trace.json), unmodified except
for the SessionID field.

    python3 sidprobe.py <host> <port> correct|wrong|zero
"""
import binascii
import socket
import sys

# our recorded -2 DC session, verbatim
SAP = bytes.fromhex("01fe8001000000248000ebab9371d34b9b79d189a98989c1d191d191818999d26b9b3a232b30020000040040")
SETUP = bytes.fromhex("01fe800100000015809802000000000000000011d01aaf37bc04080c00")
DISCOVERY = bytes.fromhex("01fe80010000000d8098020282c3034383c40451b8")
SETUP_RES = bytes.fromhex("01fe80010000001e8098020282c3034383c40451e0202d1114a905090ca914c4101e5ad940c0")

# The SessionID is 64 bits at *bit* offset 26 of the payload — it is NOT byte-aligned, and assuming
# it was is how the first version of this probe measured the wrong thing. It sent 0x…0001 where it
# meant zero, the station refused it correctly, and the run looked like a clean negative.
#
# What settles it is the station's own log. In the first run EvseV2G printed
#     Created new session with id 0x5b5f4c291d84794d
# while a byte-aligned read of the same SessionSetupRes gave 0x16d7d30a47611e53 — the same value
# shifted right by two. Reading 64 bits at payload bit 26 reproduces their number exactly, digit for
# digit. `--expect <hex>` re-checks that against the live log on every run, so a future framing change
# stops the probe instead of quietly moving it.
SID_BIT = (8 + 3) * 8 + 2          # V2GTP header is 8 bytes; then payload bit 26


def _bits(frame):
    return int.from_bytes(frame, "big"), len(frame) * 8


def get_session_id(frame):
    v, n = _bits(frame)
    return ((v >> (n - SID_BIT - 64)) & ((1 << 64) - 1)).to_bytes(8, "big")


def set_session_id(frame, sid):
    v, n = _bits(frame)
    shift = n - SID_BIT - 64
    mask = ((1 << 64) - 1) << shift
    v = (v & ~mask) | (int.from_bytes(sid, "big") << shift)
    return v.to_bytes(len(frame), "big")


assert get_session_id(SETUP) == b"\x00" * 8, "SessionSetupReq should carry the all-zero id"
assert get_session_id(SETUP_RES) == get_session_id(DISCOVERY), \
    "the id the station issued and the id the next request echoes must be the same 64 bits"
assert set_session_id(DISCOVERY, get_session_id(DISCOVERY)) == DISCOVERY, "splice is not lossless"
assert get_session_id(set_session_id(DISCOVERY, b"\x00" * 8)) == b"\x00" * 8, "zero does not survive"
print(f"splice self-check ok — the vector's own id is "
      f"{binascii.hexlify(get_session_id(DISCOVERY)).decode()}")


def recv_frame(s, tag):
    s.settimeout(10)
    try:
        head = b""
        while len(head) < 8:
            b = s.recv(8 - len(head))
            if not b:
                print(f"  {tag}: peer closed with {len(head)} header byte(s) — NO RESPONSE")
                return None
            head += b
        n = int.from_bytes(head[4:8], "big")
        body = b""
        while len(body) < n:
            b = s.recv(n - len(body))
            if not b:
                print(f"  {tag}: peer closed mid-body")
                return None
            body += b
        print(f"  {tag}: {len(head + body)} B  {binascii.hexlify(head + body).decode()}")
        return head + body
    except socket.timeout:
        print(f"  {tag}: nothing within 10 s (connection still open)")
        return None


def main():
    host, port, arm = sys.argv[1], int(sys.argv[2]), sys.argv[3]
    info = socket.getaddrinfo(host, port, socket.AF_INET6, socket.SOCK_STREAM)[0]
    s = socket.socket(info[0], info[1])
    s.settimeout(10)
    s.connect(info[4])
    print(f"connected to [{host}]:{port}  arm={arm}")

    s.sendall(SAP)
    recv_frame(s, "SupportedAppProtocolRes")
    s.sendall(SETUP)
    setup_res = recv_frame(s, "SessionSetupRes")
    if not setup_res:
        sys.exit("no SessionSetupRes — nothing to probe")

    live = get_session_id(setup_res)
    print(f"  the station issued SessionID {binascii.hexlify(live).decode()}")

    # cross-check the framing against the station's own log, if the caller passed what it printed
    if "--expect" in sys.argv:
        want = sys.argv[sys.argv.index("--expect") + 1].lower().removeprefix("0x")
        got = binascii.hexlify(live).decode()
        if got != want:
            sys.exit(f"FRAMING MISMATCH: their log says {want}, this probe read {got} — "
                     f"the SessionID is not where SID_BIT says it is; do not trust any arm")
        print(f"  framing cross-check ok: matches the id in their own log")

    if arm == "correct":
        sid, expect = live, "served (this arm is the baseline)"
    elif arm == "wrong":
        sid = bytes([live[0] ^ 0x01]) + live[1:]
        expect = "FAILED_UnknownSession per [V2G2-460]"
    elif arm == "zero":
        sid = b"\x00" * 8
        expect = "FAILED_UnknownSession per [V2G2-460] — zero is not equal to the stored id"
    else:
        sys.exit("arm must be correct|wrong|zero")

    req = set_session_id(DISCOVERY, sid)
    print(f"sending ServiceDiscoveryReq — in sequence, SessionID {binascii.hexlify(sid).decode()}")
    print(f"  expected: {expect}")
    print(f"  {binascii.hexlify(req).decode()}")
    s.sendall(req)

    res = recv_frame(s, "response")
    if res is None:
        print("VERDICT: no response at all")
    else:
        print(f"VERDICT: {len(res)} bytes came back — compare against the other arms and their log")

    s.settimeout(3)
    try:
        extra = s.recv(64)
        print("after that:", "connection closed by peer" if not extra else binascii.hexlify(extra).decode())
    except socket.timeout:
        print("after that: connection still open after 3 s")
    s.close()


main()
