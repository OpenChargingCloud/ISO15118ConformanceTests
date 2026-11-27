#!/usr/bin/env bash
# Run as root: capture tcp port 64110 on every interface while the user runs one arm, then decode.
#   isomux4-capture.sh <arm-name>      (the arm itself is started separately, as the normal user)
set -u
ARM=$1
OUT=/home/ahzf/everest/run/2610-isomux4/capture-$ARM
mkdir -p "$OUT"; chown ahzf:ahzf "$OUT"
timeout 150 tcpdump -i any -U -w "$OUT/capture.pcap" 'tcp port 64110' > "$OUT/tcpdump.log" 2>&1
chown ahzf:ahzf "$OUT"/*
echo "captured $(stat -c %s "$OUT/capture.pcap") bytes"
