#!/usr/bin/env python3
# Configs for the NoTLS arms against 2026.10.0: their EV (Ev15118) against their own -20 station
# (Evse15118D20), both from the release's own SIL configs, changed only in the transport switches and
# in auto_exec, so the session starts at boot without an MQTT driver.
import yaml
SRC = "/home/ahzf/everest/dist-2610/etc/everest"
OUT = "/home/ahzf/everest/configs-2610"

def load(name):
    return yaml.safe_load(open(f"{SRC}/{name}"))

def write(d, name, comment):
    d["active_modules"]["ev_manager"]["config_module"]["auto_exec"] = True
    with open(f"{OUT}/{name}", "w") as f:
        f.write(comment)
        yaml.safe_dump(d, f, sort_keys=False)
    print("wrote", name)

def arm(base, station, ev, name, comment):
    d = load(base)
    m = d["active_modules"]
    for k, v in station.items():
        if v is None:
            m["iso15118_charger"]["config_module"].pop(k, None)
        else:
            m["iso15118_charger"]["config_module"][k] = v
    for k, v in ev.items():
        if v is None:
            m["iso15118_car"]["config_module"].pop(k, None)
        else:
            m["iso15118_car"]["config_module"][k] = v
    write(d, name, comment)

HEAD = "# 2026-11-27 NoTLS arm against 2026.10.0, from {base}: {what}. auto_exec on.\n"

base = "config-sil-dc-d20-plaintext.yaml"
arm(base, {"tls_negotiation_strategy": "ENFORCE_NO_TLS"}, {"tls": "prefer"},
    "config-notls-prefer.yaml", HEAD.format(base=base, what="station ENFORCE_NO_TLS, EV tls: prefer"))
arm(base, {"tls_negotiation_strategy": "ENFORCE_NO_TLS"},
    {"tls": "prefer", "supported_protocols": "ISO15118_20,ISO15118_2"},
    "config-notls-prefer-both.yaml",
    HEAD.format(base=base, what="station ENFORCE_NO_TLS, EV tls: prefer offering -20 and -2"))
arm(base, {"tls_negotiation_strategy": "ENFORCE_NO_TLS"}, {"tls": "require"},
    "config-notls-require.yaml", HEAD.format(base=base, what="station ENFORCE_NO_TLS, EV tls: require (control)"))
base = "config-sil-dc-d20-tls.yaml"
arm(base, {}, {"tls": "prefer", "tls_version": None},
    "config-tls-prefer.yaml", HEAD.format(base=base, what="station ENFORCE_TLS 1.3, EV tls: prefer, version auto (control)"))
