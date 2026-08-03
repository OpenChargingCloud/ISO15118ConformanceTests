#!/bin/sh
# Asks an EVerest SECC for its V2G endpoint over SDP, and prints "<address> <port>".
#
# Why this exists. EvseV2G binds its TCP server at startup and logs the port, so a relay can be pointed
# at it directly. **Evse15118D20 does not**: libiso15118 creates the TCP server only when an SDP request
# arrives, and picks the port then. Without an SDP request there is nothing to relay to.
#
# Send it as a real EVCC does — **multicast to ff02::1**. A unicast SDP request is answered correctly and
# then shuts their whole event loop down:
#
#   [ERRO] iso15118_charge :: Shutdown loop() because of: Read on sdp server socket failed
#                             (reason: Resource temporarily unavailable)
#
# The sockets stay bound afterwards, so the station still accepts TCP connections and simply never
# answers — which from the outside looks like a dead peer rather than a crash. Reproduced twice on
# libiso15118 v0.9.0 (everest-core 2025.10.0); with multicast it does not happen. See
# docs/interop-runs/2026-08-03-everest-iso20-dc-full-charge/notes.md.
#
# Run it from a container on the same bridge as EVerest — needs socat, not much else:
#
#   docker run -d --name ev-relay --network v2gnet -p 15141:15140 v2g-socat sleep infinity
#   docker cp sdp-probe.sh ev-relay:/tmp/ && docker exec ev-relay sh /tmp/sdp-probe.sh eth0
#
# Their session is single-use: while one is open they answer further requests with "Ignoring sdp request
# message because a session is already created and running", which is fine — the endpoint stays valid.

IFACE=${1:-eth0}
SECURITY=${SECURITY:-10}     # 10 = no TLS, 00 = TLS
TRANSPORT=00                 # TCP

REPLY=$(printf "\\x01\\xfe\\x90\\x00\\x00\\x00\\x00\\x02\\x$SECURITY\\x$TRANSPORT" \
        | socat -T3 - "UDP6-DATAGRAM:[ff02::1%$IFACE]:15118,bind=:15119" | od -An -tx1 | tr -d ' \n')

if [ ${#REPLY} -lt 56 ]; then
  echo "no SDP response (got '${REPLY}')" >&2
  exit 1
fi

# 8-byte V2GTP header, then 16 bytes of IPv6, 2 bytes of port, security, transport.
ADDR=$(echo "$REPLY" | cut -c17-48 | sed 's/..../&:/g; s/:$//')
PORT=$((0x$(echo "$REPLY" | cut -c49-52)))
SEC=$(echo "$REPLY" | cut -c53-54)

echo "[$ADDR%$IFACE]:$PORT  (security=$SEC)"
