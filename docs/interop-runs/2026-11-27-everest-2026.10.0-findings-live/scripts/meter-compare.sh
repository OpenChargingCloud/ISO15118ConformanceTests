#!/usr/bin/env bash
# d20-meter-info: our charge-loop requests and their responses, control vs MeterInfoRequested, side by side.
cd /home/ahzf/everest/run/2610-repro || exit 1
for a in meter-ctl meter; do
  F=$(ls $a/rec-1/*.frames.log | head -1)
  echo "=== $a"
  awk '/^\[[0-9]+\] DC_ChargeLoop(Req|Res)/{h=$1" "$2; getline; gsub(/ /,"",$0); print h, length($0)/2, $0}' "$F" > /tmp/$a.loop
  cat /tmp/$a.loop | cut -c1-200
done
echo "=== responses identical between the arms?"
diff <(grep Res /tmp/meter-ctl.loop | awk '{print $4}') <(grep Res /tmp/meter.loop | awk '{print $4}') && echo "IDENTICAL"
echo "=== requests: first differing byte"
python3 -I - <<'PY'
a=[l.split()[3] for l in open('/tmp/meter-ctl.loop') if 'Req' in l]
b=[l.split()[3] for l in open('/tmp/meter.loop') if 'Req' in l]
for x,y in zip(a,b):
    d=[i for i in range(0,min(len(x),len(y)),2) if x[i:i+2]!=y[i:i+2]]
    print(len(x)//2, len(y)//2, [(i//2, x[i:i+2], y[i:i+2]) for i in d][:4])
PY
