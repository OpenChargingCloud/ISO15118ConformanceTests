#!/bin/bash
# Build eVDriveFlow's EV, UNPATCHED, for the stdin experiment.
#
# Neither of the 2026-08-01 workarounds is needed here: finding 1 (the service_id filter) sits in
# ServiceDiscovery, which is past the wall, and finding 4 (raise on an unsupported authorization
# service) does not trigger when our SECC offers EIM only. So this image is their code as published.
set -eu
CTX=/home/ahzf/edf/ctx
rm -rf "$CTX"; mkdir -p "$CTX"
cp -r /home/ahzf/edf/eVDriveFlow "$CTX/eVDriveFlow"
rm -rf "$CTX/eVDriveFlow/.git"
cp /home/ahzf/i15118/tools/interop-josev/sdp-responder.py "$CTX/sdp-responder.py"

cat > "$CTX/Dockerfile" <<'EOF'
FROM python:3.8-slim-bookworm
RUN apt-get update && apt-get install -y --no-install-recommends \
        default-jre-headless build-essential socat iproute2 iputils-ping \
        libxml2-dev libxslt1-dev zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
RUN pip install --no-cache-dir \
        hexdump==3.3 transitions==0.8.8 netifaces==0.11.0 scapy==2.4.5 \
        xsdata==21.8 jpype1==1.3.0 lxml numpy python-dotenv==0.19.0
WORKDIR /app
COPY eVDriveFlow /app
# TLS off (their own testing switch) and the interface a container actually has.
RUN sed -i 's/^SECURITY_PROTOCOL = 0x00/SECURITY_PROTOCOL = 0x10/' shared/global_values.py \
 && sed -i 's/^\( *\)interface = .*/\1interface = eth0/' secc/evse_config.ini \
 && sed -i 's/^\( *\)interface = .*/\1interface = eth0/' evcc/ev_config.ini \
 && grep -n "SECURITY_PROTOCOL = " shared/global_values.py
COPY sdp-responder.py /usr/local/bin/sdp-responder.py
WORKDIR /app/evcc
CMD ["python3", "start_ev.py"]
EOF

cd "$CTX"
docker build -q -t edf-ev-unpatched . 2>&1 | tail -3
docker images edf-ev-unpatched --format "built {{.Repository}} {{.Size}}"
