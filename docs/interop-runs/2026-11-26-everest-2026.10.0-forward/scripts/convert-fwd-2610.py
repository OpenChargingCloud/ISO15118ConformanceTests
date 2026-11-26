#!/usr/bin/env python3
# Forward-run configs for the everest-core 2026.10.0 release, which has no PyEvJosev.
# In every forward config the car module is only the parked idle car (auto_exec: false; our EVCC is
# the car under test), so replace it with Ev15118 on lo, plaintext, offering the station's protocols.
# Only active_modules.iso15118_car changes; the layout block is dropped (cosmetic, editor-only).
import sys, yaml

SRC = "/home/ahzf/everest/configs-ours"
DST = "/home/ahzf/everest/configs-2610"

ARMS = {
    "config-dc2-ours":       "ISO15118_2",
    "config-ac2-ours":       "ISO15118_2",
    "config-ac2-tls-ours":   "ISO15118_2",
    "config-dc2-pnc-ours":   "ISO15118_2",
    "config-d20-ours":       "ISO15118_20",
    "config-d20-tls-ours":   "ISO15118_20",
    "config-ac20-ours":      "ISO15118_20",
    "config-ac20-tls-ours":  "ISO15118_20",
    "config-mcs-ours":       "ISO15118_20",
    "config-mux-ours":       "ISO15118_20,ISO15118_2",
    "config-mux-tls-ours":   "ISO15118_20,ISO15118_2",
}

for name, protos in ARMS.items():
    d = yaml.safe_load(open(f"{SRC}/{name}.yaml"))
    car = d["active_modules"].get("iso15118_car")
    if car is None:
        print(f"{name}: no iso15118_car, copied unchanged")
    else:
        old = car.get("module")
        d["active_modules"]["iso15118_car"] = {
            "module": "Ev15118",
            "config_module": {"device": "lo", "supported_protocols": protos, "tls": "off"},
            **({"connections": car["connections"]} if "connections" in car else {}),
        }
        print(f"{name}: iso15118_car {old} -> Ev15118 ({protos})")
    d.pop("x-module-layout", None)
    out = f"{DST}/{name.replace('-ours', '-fwd2610')}.yaml"
    with open(out, "w") as f:
        f.write(f"# 2026-11-26: {name}.yaml for the 2026.10.0 release (no PyEvJosev): the parked idle car\n"
                f"# is Ev15118 on lo instead. Nothing else changed.\n")
        yaml.safe_dump(d, f, sort_keys=False)
    yaml.safe_load(open(out))
print("all ok")
