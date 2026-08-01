# eVDriveFlow's SECC, headless, TLS off — for a TCP-only interop run against our EVCC.
#
# Their own environment.yml is conda and linux-64 pinned (libgcc-ng, ld_impl_linux-64), so it cannot
# resolve on an ARM Mac at all. This reproduces the parts that matter instead: Python 3.8.10 as pinned,
# a JVM for Nagasena (their EXI is OpenEXI, and the jars ship in shared/lib), and the subset of their
# pip list the headless SECC path actually imports — PyQt5/matplotlib/superqt/numpy are GUI-only.
FROM python:3.8-slim-bookworm

RUN apt-get update && apt-get install -y --no-install-recommends \
        default-jre-headless build-essential socat iproute2 iputils-ping \
        libxml2-dev libxslt1-dev zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

# Their pins where they still build, which is most of them. xsdata is the one that matters most for
# wire behaviour — it is the data binding the messages are marshalled through — so it stays pinned.
RUN pip install --no-cache-dir \
        hexdump==3.3 \
        transitions==0.8.8 \
        netifaces==0.11.0 \
        scapy==2.4.5 \
        xsdata==21.8 \
        jpype1==1.3.0 \
        lxml \
        python-dotenv==0.19.0

WORKDIR /app
COPY eVDriveFlow /app

# TLS off (their own testing switch), and the interface a container actually has.
RUN sed -i 's/^SECURITY_PROTOCOL = 0x00/SECURITY_PROTOCOL = 0x10/' shared/global_values.py \
 && sed -i 's/^\( *\)interface = .*/\1interface = eth0/' secc/evse_config.ini \
 && grep -n "SECURITY_PROTOCOL = " shared/global_values.py \
 && cat secc/evse_config.ini

COPY finding1.py /tmp/finding1.py
RUN python3 /tmp/finding1.py

# The JVM classpath in shared/message_handling.py is relative ("../shared/lib/"), so the SECC has to be
# started from inside secc/.
WORKDIR /app/secc
CMD ["python3", "start_evse.py"]
