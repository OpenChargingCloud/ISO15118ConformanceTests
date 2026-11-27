#!/usr/bin/env python3
# config-d20-pnc-fwd2610.yaml: the -20 DC mutual-TLS-1.3 station of the 2026.10.0 forward run, made to
# offer Plug & Charge, which Evse15118D20 does from 2026.10.0 on (ISO15118_chargerImpl.cpp:1684).
#   - EvseManager offers Contract only (payment_enable_contract on, payment_enable_eim off);
#   - the EIM token is parked on a connector that does not exist, or the SIL car's plug-in authorizes
#     the session before our car arrives and Contract is never offered (the -2 PnC run's trap);
#   - auth listens to EvseManager's own token_provider, without which no PnC token ever reaches the
#     validator (tools/interop-everest/README.md, "Deciding a Plug & Charge contract").
# The validator stays DummyTokenValidator, Accepted: this arm asks whether -20 PnC runs, not who decides.
import yaml
C = "/home/ahzf/everest/configs-2610"
d = yaml.safe_load(open(f"{C}/config-d20-tls-fwd2610.yaml"))
m = d["active_modules"]
m["evse_manager"]["config_module"]["payment_enable_contract"] = True
m["evse_manager"]["config_module"]["payment_enable_eim"] = False
m["token_provider"]["config_implementation"]["main"]["connector_id"] = 2
m["auth"]["connections"]["token_provider"].append({"module_id": "evse_manager", "implementation_id": "token_provider"})
out = f"{C}/config-d20-pnc-fwd2610.yaml"
with open(out, "w") as f:
    f.write("# 2026-11-27: -20 DC Plug & Charge over mutual TLS 1.3 against 2026.10.0; see make-d20-pnc.py.\n")
    yaml.safe_dump(d, f, sort_keys=False)
yaml.safe_load(open(out))
print("wrote", out)
