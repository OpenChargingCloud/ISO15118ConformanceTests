#!/usr/bin/env python3
# One line per forward session: arm, session, test verdict, exchanges, non-OK codes, the last pair.
import glob, os, re

ROOT = "/home/ahzf/everest/run/2610-fwd"
for arm in sorted(os.listdir(ROOT)):
    d = os.path.join(ROOT, arm)
    if not os.path.isdir(d):
        continue
    for log in sorted(glob.glob(f"{d}/our-evcc-*.log")):
        s = re.search(r"our-evcc-(\d+)", log).group(1)
        text = open(log, errors="replace").read()
        verdict = "PASS" if re.search(r"^\s*Passed OurEvcc", text, re.M) else \
                  "FAIL" if re.search(r"^\s*Failed OurEvcc", text, re.M) else "??"
        rows, bad, svc = [], [], ""
        for fm in glob.glob(f"{d}/rec-{s}/*.flow.md"):
            for line in open(fm, errors="replace"):
                m = re.match(r"\|\s*\d+\s*\|\s*(\S+)\s*\|\s*(\S*)\s*\|\s*(\S*)\s*\|", line)
                if m:
                    rows.append(m.groups())
                    if m.group(3) and not m.group(3).startswith("OK"):
                        bad.append(f"{m.group(2)}={m.group(3)}")
        m = re.search(r"(selected service[^\n]{0,60}|negotiated[^\n]{0,60})", text, re.I)
        svc = m.group(1).strip() if m else ""
        last = f"{rows[-1][0]}/{rows[-1][2]}" if rows else "-"
        cl = sum(1 for r in rows if r[0] in ("CurrentDemandReq", "ChargingStatusReq", "DC_ChargeLoopReq", "AC_ChargeLoopReq"))
        err = ""
        if verdict != "PASS":
            m = re.search(r"Error Message:\s*\n\s*(.{0,200})", text)
            err = m.group(1).strip() if m else ""
        print(f"{arm:18} s{s} {verdict:4} {len(rows):3} pairs, {cl:3} loops, last {last:40} "
              f"{('non-OK: ' + ', '.join(sorted(set(bad)))) if bad else ''} {svc} {err}")
