"""Send EvseV2G an ISO 15118-2 request it is not waiting for, and see what comes back.

[V2G2-459] / [V2G2-538]: the SECC shall answer with the corresponding response message
carrying FAILED_SequenceError, and only then terminate ([V2G2-539]).

Frames are our own recorded -2 DC session (Vectors/Session.iso2-dc-eim.trace.json); the only
edit is the SessionID, spliced from their SessionSetupRes into the out-of-order request.
"""
import socket, sys, time, binascii

HOST, PORT = sys.argv[1], int(sys.argv[2])
ARM = sys.argv[3] if len(sys.argv) > 3 else "authorization"

SAP   = bytes.fromhex("01fe8001000000248000ebab9371d34b9b79d189a98989c1d191d191818999d26b9b3a232b30020000040040")
SETUP = bytes.fromhex("01fe800100000015809802000000000000000011d01aaf37bc04080c00")
# expected next after SessionSetupRes is ServiceDiscoveryReq; these are not that.
OUT_OF_ORDER = {
    # AuthorizationReq — four messages too early
    "authorization":  bytes.fromhex("01fe80010000000d8098020282c3034383c4045008"),
    # ChargeParameterDiscoveryReq — five too early
    "chargeparams":   bytes.fromhex("01fe80010000001d8098020282c3034383c4045094ca400640618640088c40f40313205000"),
}[ARM]

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
        print(f"  {tag}: {binascii.hexlify(head + body).decode()}")
        return head + body
    except socket.timeout:
        print(f"  {tag}: nothing within 10 s (connection still open)")
        return None

info = socket.getaddrinfo(HOST, PORT, socket.AF_INET6, socket.SOCK_STREAM)[0]
s = socket.socket(info[0], info[1])
s.settimeout(10)
s.connect(info[4])
print(f"connected to [{HOST}]:{PORT}")

s.sendall(SAP);   sap_res   = recv_frame(s, "SupportedAppProtocolRes")
s.sendall(SETUP); setup_res = recv_frame(s, "SessionSetupRes")
if not setup_res:
    sys.exit("no SessionSetupRes — nothing to probe")

# their SessionID: payload bytes 3..10, same offset as in every -2 message of this shape
sid = setup_res[8 + 3: 8 + 11]
print(f"  their SessionID: {binascii.hexlify(sid).decode()}")

req = bytearray(OUT_OF_ORDER)
req[8 + 3: 8 + 11] = sid
print(f"sending {ARM} out of order (the station is waiting for ServiceDiscoveryReq):")
print(f"  {binascii.hexlify(bytes(req)).decode()}")
s.sendall(bytes(req))

res = recv_frame(s, "response")
if res is None:
    print("VERDICT: no response message — [V2G2-538] wants one before the session ends")
else:
    print("VERDICT: a response came back; decode the ResponseCode to see whether it is FAILED_SequenceError")

time.sleep(1)
s.settimeout(3)
try:
    extra = s.recv(64)
    print("after that:", "connection closed by peer" if not extra else binascii.hexlify(extra).decode())
except socket.timeout:
    print("after that: connection still open after 3 s")
s.close()
