#!/usr/bin/env bash
# inspect.sh arm [arm ...] — verdict, our key lines and the flow tail for each arm
cd /home/ahzf/everest/run/2610-repro || exit 1
for a in "$@"; do
  for s in 1 2; do
    [ -f "$a/our-evcc-$s.log" ] || continue
    echo "=== $a s$s"
    grep -A3 "Error Message" "$a/our-evcc-$s.log" | head -5 | cut -c1-300
    grep -E "^ ?(TLS|SAP|PnC|Provisioning|Meter|Renegot|Silent|Ongoing)[^ ]*:" "$a/our-evcc-$s.log" | sort -u | cut -c1-220
    f=$(ls $a/rec-$s/*.flow.md 2>/dev/null | head -1)
    [ -n "$f" ] && { echo "--- flow ($(grep -cE '^\| [0-9]' "$f") rows)"; grep -E '^\| [0-9]' "$f" | tail -${TAIL:-8}; }
  done
done
